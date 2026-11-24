using System;
using System.IO;
using System.Linq;
using RestartVpn.Core.Diagnostics;
using RestartVpn.Core.Settings;
using Xunit;

namespace RestartVpn.Tests
{
    public class JsonAndLogTests
    {
        [Fact]
        public void Prettifier_keeps_strings_intact()
        {
            string compact = "{\"a\":1,\"b\":\"x, y\",\"c\":[true,null],\"d\":\"v\\\"q\"}";
            string pretty = JsonText.Prettify(compact);
            var parsed = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<object>(pretty);
            Assert.NotNull(parsed);
            Assert.Contains("\n", pretty);
        }

        [Fact]
        public void Log_rotates_while_running()
        {
            string dir = Path.Combine(Path.GetTempPath(), "restartvpn-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var log = new AppLog(dir, LogLevel.Debug, 64 * 1024, 2, false))
                {
                    string filler = new string('x', 200);
                    for (int i = 0; i < 500; i++) log.Info(filler);
                }

                var files = Directory.GetFiles(dir, "restartvpn*.log");
                Assert.True(files.Length >= 2, "expected a rotated log file, got: " + string.Join(", ", files));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}