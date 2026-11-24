using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RestartVpn.Core.Net;
using RestartVpn.Core.Settings;
using Xunit;

namespace RestartVpn.Tests
{
    public class HttpProbeTests
    {
        private static int StartServer(string body, int statusCode, bool respond)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            Task.Run(async () =>
            {
                try
                {
                    using (var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false))
                    using (var stream = client.GetStream())
                    {
                        var buffer = new byte[4096];
                        await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                        if (!respond) return;

                        byte[] payload = Encoding.UTF8.GetBytes(body);
                        string header = "HTTP/1.1 " + statusCode + " X\r\nContent-Length: " + payload.Length +
                                        "\r\nConnection: close\r\n\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), 0, header.Length).ConfigureAwait(false);
                        await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
                    }
                }
                catch { }
                finally { try { listener.Stop(); } catch { } }
            });

            return port;
        }

        private static TargetSettings Target(int port, int timeoutMs = 3000) => new TargetSettings
        {
            Name = "test",
            Url = "http://127.0.0.1:" + port + "/",
            TimeoutMs = timeoutMs,
            RetryTimeoutMs = timeoutMs
        };

        [Fact]
        public async Task Http_200_is_available()
        {
            int port = StartServer("hello", 200, true);
            using (var probe = new HttpProbe(new ProbeSettings()))
            {
                var result = await probe.ProbeAsync(Target(port), CancellationToken.None);
                Assert.True(result.Success);
                Assert.Equal(200, result.StatusCode);
                Assert.Equal(ProbeOutcome.Ok, result.Outcome);
            }
        }

        [Fact]
        public async Task Http_500_is_a_failure_by_default()
        {
            var settings = new ProbeSettings { Treat5xxAsFailure = true };
            int port = StartServer("down", 503, true);
            using (var probe = new HttpProbe(settings))
            {
                var result = await probe.ProbeAsync(Target(port), CancellationToken.None);
                Assert.False(result.Success);
                Assert.Equal(ProbeOutcome.HttpStatus, result.Outcome);
            }
        }

        [Fact]
        public async Task Timeout_is_classified()
        {
            int port = StartServer("", 200, false);
            using (var probe = new HttpProbe(new ProbeSettings()))
            {
                var result = await probe.ProbeAsync(Target(port, 700), CancellationToken.None);
                Assert.False(result.Success);
                Assert.Equal(ProbeOutcome.Timeout, result.Outcome);
            }
        }

        [Fact]
        public async Task Body_pattern_mismatch_fails()
        {
            int port = StartServer("hello", 200, true);
            var target = Target(port);
            target.BodyPattern = "bye";
            using (var probe = new HttpProbe(new ProbeSettings()))
            {
                var result = await probe.ProbeAsync(target, CancellationToken.None);
                Assert.False(result.Success);
                Assert.Equal(ProbeOutcome.BodyMismatch, result.Outcome);
            }
        }
    }
}
