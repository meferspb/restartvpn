using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace RestartVpn.App.Install
{
    /// <summary>
    /// One instance per settings file. A second launch asks the running instance to show
    /// its window instead of silently doing nothing.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private Mutex _mutex;
        private EventWaitHandle _showEvent;
        private Thread _listener;
        private volatile bool _stop;

        public event Action ShowRequested;

        public static bool TryAcquire(string settingsPath, out SingleInstance instance)
        {
            instance = null;
            string key = Hash(settingsPath ?? string.Empty);
            string mutexName = "Local\\RestartVPN_" + key;
            string showName = "Local\\RestartVPN_Show_" + key;

            var candidate = new SingleInstance();
            try
            {
                candidate._mutex = new Mutex(false, mutexName);
                bool acquired;
                try { acquired = candidate._mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }

                if (!acquired)
                {
                    candidate._mutex.Dispose();
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting(showName))
                            ev.Set();
                    }
                    catch { }
                    return false;
                }
            }
            catch
            {
                // If the kernel object cannot be created, do not block the app.
                try { candidate._mutex?.Dispose(); } catch { }
                instance = candidate;
                return true;
            }

            try
            {
                candidate._showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, showName);
                candidate._listener = new Thread(candidate.Listen) { IsBackground = true, Name = "RestartVPN-show" };
                candidate._listener.Start();
            }
            catch { }

            instance = candidate;
            return true;
        }

        private void Listen()
        {
            while (!_stop)
            {
                try
                {
                    if (_showEvent != null && _showEvent.WaitOne(500))
                    {
                        var handler = ShowRequested;
                        if (handler != null) { try { handler(); } catch { } }
                    }
                }
                catch { return; }
            }
        }

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToLowerInvariant()));
                var sb = new StringBuilder();
                for (int i = 0; i < 6; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public void Dispose()
        {
            _stop = true;
            try { _showEvent?.Set(); } catch { }
            try { if (_listener != null && !_listener.Join(1000)) { } } catch { }
            try { _showEvent?.Dispose(); } catch { }
            try { _mutex?.ReleaseMutex(); } catch { }
            try { _mutex?.Dispose(); } catch { }
        }
    }
}
