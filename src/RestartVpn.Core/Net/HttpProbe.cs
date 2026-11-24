using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RestartVpn.Core.Settings;

namespace RestartVpn.Core.Net
{
    /// <summary>
    /// In-process HTTP availability probe. Unlike the legacy curl-based check this does not
    /// spawn a process every few seconds, does not download the whole body and works on
    /// Windows 7 where curl.exe does not exist.
    /// </summary>
    public sealed class HttpProbe : IDisposable
    {
        private readonly ProbeSettings _settings;
        private readonly HttpClient _client;
        private bool _disposed;

        public HttpProbe(ProbeSettings settings)
        {
            _settings = settings ?? new ProbeSettings();
            TlsConfig.EnableModernTls(_settings.Tls12);

            var handler = new HttpClientHandler
            {
                UseProxy = _settings.UseSystemProxy,
                AllowAutoRedirect = _settings.FollowRedirects,
                MaxAutomaticRedirections = Math.Max(1, _settings.MaxRedirects),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = false
            };

            _client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            if (!string.IsNullOrWhiteSpace(_settings.UserAgent))
                _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _settings.UserAgent);
        }

        public async Task<ProbeResult> ProbeAsync(TargetSettings target, CancellationToken cancellationToken)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            string name = string.IsNullOrWhiteSpace(target.Name) ? SafeHost(target.Url) : target.Name;
            var sw = Stopwatch.StartNew();

            try
            {
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                using (var request = BuildRequest(target))
                {
                    timeoutCts.CancelAfter(Math.Max(500, target.TimeoutMs));
                    using (var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false))
                    {
                        int code = (int)response.StatusCode;
                        long latency = sw.ElapsedMilliseconds;

                        bool codeOk = IsStatusCodeAccepted(target, code);
                        if (!codeOk)
                        {
                            return new ProbeResult
                            {
                                TargetName = name,
                                Url = target.Url,
                                Success = false,
                                Outcome = ProbeOutcome.HttpStatus,
                                StatusCode = code,
                                LatencyMs = latency,
                                Detail = "HTTP " + code,
                                TimestampUtc = DateTimeOffset.UtcNow
                            };
                        }

                        if (!string.IsNullOrEmpty(target.BodyPattern) && !string.Equals(target.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
                        {
                            string body = await ReadLimitedAsync(response, _settings.MaxBodyBytes, timeoutCts.Token).ConfigureAwait(false);
                            if (!Regex.IsMatch(body ?? string.Empty, target.BodyPattern))
                            {
                                return new ProbeResult
                                {
                                    TargetName = name,
                                    Url = target.Url,
                                    Success = false,
                                    Outcome = ProbeOutcome.BodyMismatch,
                                    StatusCode = code,
                                    LatencyMs = sw.ElapsedMilliseconds,
                                    Detail = "HTTP " + code + ", body pattern not found",
                                    TimestampUtc = DateTimeOffset.UtcNow
                                };
                            }
                        }

                        return new ProbeResult
                        {
                            TargetName = name,
                            Url = target.Url,
                            Success = true,
                            Outcome = ProbeOutcome.Ok,
                            StatusCode = code,
                            LatencyMs = sw.ElapsedMilliseconds,
                            Detail = "HTTP " + code,
                            TimestampUtc = DateTimeOffset.UtcNow
                        };
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                return ProbeResult.Fail(name, target.Url, ProbeOutcome.Timeout, "timeout after " + target.TimeoutMs + " ms", sw.ElapsedMilliseconds);
            }
            catch (HttpRequestException ex)
            {
                var outcome = Classify(ex);
                return ProbeResult.Fail(name, target.Url, outcome, Explain(ex), sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                return ProbeResult.Fail(name, target.Url, ProbeOutcome.Unknown, ex.GetType().Name + ": " + ex.Message, sw.ElapsedMilliseconds);
            }
        }

        private HttpRequestMessage BuildRequest(TargetSettings target)
        {
            var method = new HttpMethod(string.Equals(target.Method, "HEAD", StringComparison.OrdinalIgnoreCase) ? "HEAD" : "GET");
            var request = new HttpRequestMessage(method, target.Url);
            if (target.Headers != null)
            {
                foreach (var pair in target.Headers)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                    request.Headers.TryAddWithoutValidation(pair.Key, pair.Value ?? string.Empty);
                }
            }
            return request;
        }

        private bool IsStatusCodeAccepted(TargetSettings target, int code)
        {
            if (target.ExpectedStatus != null && target.ExpectedStatus.Length > 0)
            {
                foreach (int expected in target.ExpectedStatus)
                    if (expected == code) return true;
                return false;
            }

            if (code >= 500 && code <= 599)
                return !(target.Treat5xxAsFailure || _settings.Treat5xxAsFailure);

            return code >= 100 && code <= 599;
        }

        private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
        {
            if (maxBytes <= 0) return string.Empty;
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var buffer = new MemoryStream())
            using (var limited = new LimitedStream(stream, maxBytes))
            {
                await limited.CopyToAsync(buffer, 81920, ct).ConfigureAwait(false);
                return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }

        private static ProbeOutcome Classify(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (e is AuthenticationException) return ProbeOutcome.TlsFailure;
                if (e is SocketException se)
                {
                    switch (se.SocketErrorCode)
                    {
                        case SocketError.HostNotFound:
                        case SocketError.NoData:
                        case SocketError.TryAgain:
                            return ProbeOutcome.DnsFailure;
                        case SocketError.TimedOut:
                            return ProbeOutcome.Timeout;
                        default:
                            return ProbeOutcome.ConnectionFailure;
                    }
                }
                if (e is WebException we)
                {
                    if (we.Status == WebExceptionStatus.TrustFailure) return ProbeOutcome.TlsFailure;
                    if (we.Status == WebExceptionStatus.NameResolutionFailure) return ProbeOutcome.DnsFailure;
                    if (we.Status == WebExceptionStatus.Timeout) return ProbeOutcome.Timeout;
                }
            }

            string text = (ex.Message ?? string.Empty).ToLowerInvariant();
            if (text.Contains("ssl") || text.Contains("tls") || text.Contains("certificate")) return ProbeOutcome.TlsFailure;
            if (text.Contains("name or service") || text.Contains("not known") || text.Contains("dns")) return ProbeOutcome.DnsFailure;
            return ProbeOutcome.ConnectionFailure;
        }

        private static string Explain(Exception ex)
        {
            string message = ex.Message;
            for (Exception e = ex.InnerException; e != null; e = e.InnerException)
                message = e.Message;
            return string.IsNullOrWhiteSpace(message) ? ex.GetType().Name : message.Trim();
        }

        private static string SafeHost(string url)
        {
            try { return new Uri(url).Host; } catch { return url; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _client.Dispose();
        }

        /// <summary>Stream wrapper that stops reading after a fixed number of bytes.</summary>
        private sealed class LimitedStream : Stream
        {
            private readonly Stream _inner;
            private long _remaining;

            public LimitedStream(Stream inner, long limit)
            {
                _inner = inner;
                _remaining = limit;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_remaining <= 0) return 0;
                int toRead = (int)Math.Min(count, _remaining);
                int read = _inner.Read(buffer, offset, toRead);
                _remaining -= read;
                return read;
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
