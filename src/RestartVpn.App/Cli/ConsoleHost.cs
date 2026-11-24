using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RestartVpn.App.Cli
{
    /// <summary>
    /// WinExe has no console of its own. For CLI modes we either attach to the parent console
    /// (started from cmd/PowerShell) or allocate a new one (started from Explorer).
    /// </summary>
    public static class ConsoleHost
    {
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        public static bool HasConsole { get; private set; }

        public static bool Ensure(bool allocateIfMissing = true)
        {
            if (HasConsole) return true;

            bool attached = AttachConsole(AttachParentProcess);
            if (!attached && allocateIfMissing) attached = AllocConsole();
            if (!attached) return false;

            try
            {
                var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
                var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
                Console.SetOut(stdout);
                Console.SetError(stderr);
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch { }

            HasConsole = true;
            return true;
        }

        public static void Release()
        {
            if (!HasConsole) return;
            try { FreeConsole(); } catch { }
            HasConsole = false;
        }
    }
}
