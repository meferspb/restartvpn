using System;
using System.IO;
using RestartVpn.Core.Diagnostics;
using Xunit;

namespace RestartVpn.Tests
{
    public class LogIsolationTests
    {
        [Fact]
        public void Different_profiles_use_different_log_files()
        {
            string dir = Path.Combine(Path.GetTempPath(), "restartvpn-log-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var a = new AppLog(dir, LogLevel.Info, 64 * 1024, 2, true, "alpha"))
                using (var b = new AppLog(dir, LogLevel.Info, 64 * 1024, 2, true, "beta"))
                {
                    a.Info("alpha line");
                    b.Info("beta line");
                }

                Assert.True(File.Exists(Path.Combine(dir, "restartvpn-alpha.log")));
                Assert.True(File.Exists(Path.Combine(dir, "restartvpn-beta.log")));
                Assert.True(File.Exists(Path.Combine(dir, "restartvpn-alpha.events.jsonl")));
                Assert.True(File.Exists(Path.Combine(dir, "restartvpn-beta.events.jsonl")));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
