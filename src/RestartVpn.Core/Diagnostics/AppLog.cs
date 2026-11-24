using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace RestartVpn.Core.Diagnostics
{
    public enum LogLevel { Error = 0, Warn = 1, Info = 2, Debug = 3 }

    /// <summary>
    /// Thread-safe file logger with size-based rotation that also works while the process runs
    /// (the legacy script only rotated at startup, so a long-running monitor grew without bound).
    /// </summary>
    public sealed class AppLog : IDisposable
    {
        private readonly object _gate = new object();
        private readonly string _dir;
        private readonly LogLevel _minLevel;
        private readonly long _maxBytes;
        private readonly int _keep;
        private readonly bool _jsonl;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        private StreamWriter _text;
        private StreamWriter _events;
        private bool _disposed;

        public string TextFilePath { get; private set; }
        public string EventFilePath { get; private set; }
        public string LastError { get; private set; }

        public AppLog(string directory, LogLevel minLevel, long maxBytes, int keep, bool jsonl, string filePrefix = null)
        {
            _dir = directory;
            _minLevel = minLevel;
            _maxBytes = Math.Max(64 * 1024, maxBytes);
            _keep = Math.Max(1, keep);
            _jsonl = jsonl;

            try { Directory.CreateDirectory(_dir); } catch { }

            // Two profiles may run at the same time; separate files avoid interleaved writes.
            string prefix = SanitizePrefix(filePrefix);
            TextFilePath = Path.Combine(_dir, prefix == null ? "restartvpn.log" : "restartvpn-" + prefix + ".log");
            EventFilePath = Path.Combine(_dir, prefix == null ? "events.jsonl" : "restartvpn-" + prefix + ".events.jsonl");
            OpenText();
            if (_jsonl) OpenEvents();
        }

        public bool IsEnabled(LogLevel level) => level <= _minLevel;

        public void Error(string message, Exception ex = null) => Write(LogLevel.Error, message, ex);
        public void Warn(string message, Exception ex = null) => Write(LogLevel.Warn, message, ex);
        public void Info(string message) => Write(LogLevel.Info, message, null);
        public void Debug(string message) => Write(LogLevel.Debug, message, null);

        public void Write(LogLevel level, string message, Exception ex)
        {
            if (level > _minLevel) return;
            string line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)
                          + " [" + LevelTag(level) + "] " + message;
            if (ex != null) line += " | " + ex.GetType().Name + ": " + ex.Message;

            lock (_gate)
            {
                if (_disposed) return;
                try
                {
                    if (_text == null) OpenText();
                    _text.WriteLine(line);
                    _text.Flush();
                    RotateIfNeeded(ref _text, TextFilePath, false);
                }
                catch (Exception io)
                {
                    LastError = io.Message;
                }
            }
        }

        /// <summary>Appends a structured event (JSON Lines). Useful for post-mortems and dashboards.</summary>
        public void Event(string type, string message, IDictionary<string, object> data = null)
        {
            if (!_jsonl) return;
            var payload = new Dictionary<string, object>
            {
                ["ts"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["type"] = type,
                ["msg"] = message
            };
            if (data != null) payload["data"] = data;

            string line;
            try { line = _json.Serialize(payload); }
            catch { return; }

            lock (_gate)
            {
                if (_disposed) return;
                try
                {
                    if (_events == null) OpenEvents();
                    _events.WriteLine(line);
                    _events.Flush();
                    RotateIfNeeded(ref _events, EventFilePath, true);
                }
                catch (Exception io)
                {
                    LastError = io.Message;
                }
            }
        }

        private void RotateIfNeeded(ref StreamWriter writer, string basePath, bool isEvent)
        {
            string path = writer == null ? null : (isEvent ? EventFilePath : TextFilePath);
            if (path == null) return;

            long length;
            try { length = new FileInfo(path).Length; }
            catch { return; }

            if (length <= _maxBytes) return;

            try
            {
                writer.Flush();
                writer.Dispose();
                writer = null;

                string dir = Path.GetDirectoryName(path);
                string name = Path.GetFileNameWithoutExtension(path);
                string ext = Path.GetExtension(path);
                string rotated = Path.Combine(dir, name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ext);
                File.Move(path, rotated);

                var old = new List<FileInfo>(new DirectoryInfo(dir).GetFiles(name + "-*" + ext));
                old.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (int i = _keep; i < old.Count; i++)
                {
                    try { old[i].Delete(); } catch { }
                }
            }
            catch (Exception io)
            {
                LastError = io.Message;
            }
        }

        private void OpenText()
        {
            try
            {
                var stream = new FileStream(TextFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _text = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception io) { LastError = io.Message; _text = null; }
        }

        private void OpenEvents()
        {
            try
            {
                var stream = new FileStream(EventFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _events = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception io) { LastError = io.Message; _events = null; }
        }

        private static string SanitizePrefix(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var sb = new StringBuilder(value.Length);
            foreach (char c in value.Trim())
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            string result = sb.ToString().Trim();
            return result.Length == 0 ? null : result;
        }

        private static string LevelTag(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Error: return "ERROR";
                case LogLevel.Warn: return "WARN ";
                case LogLevel.Debug: return "DEBUG";
                default: return "INFO ";
            }
        }

        public static LogLevel ParseLevel(string value)
        {
            if (string.Equals(value, "Error", StringComparison.OrdinalIgnoreCase)) return LogLevel.Error;
            if (string.Equals(value, "Warn", StringComparison.OrdinalIgnoreCase)) return LogLevel.Warn;
            if (string.Equals(value, "Debug", StringComparison.OrdinalIgnoreCase)) return LogLevel.Debug;
            return LogLevel.Info;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                try { _text?.Dispose(); } catch { }
                try { _events?.Dispose(); } catch { }
                _text = null;
                _events = null;
            }
        }
    }
}
