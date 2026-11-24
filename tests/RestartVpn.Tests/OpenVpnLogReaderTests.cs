using System;
using System.IO;
using RestartVpn.Core.Vpn;
using Xunit;

namespace RestartVpn.Tests
{
    public class OpenVpnLogReaderTests
    {
        [Fact]
        public void Reads_tail_and_refreshes_when_file_changes()
        {
            string dir = Path.Combine(Path.GetTempPath(), "restartvpn-logread-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "test.log");
            try
            {
                File.WriteAllText(path, "2026-11-24 10:00:00 AUTH_FAILED\n");
                var reader = new OpenVpnLogReader();
                var first = reader.Read("test", dir);
                Assert.Equal(VpnLogEvidence.FatalAuth, first.Kind);

                File.AppendAllText(path, "2026-11-24 10:00:05 Initialization Sequence Completed\n");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));

                var second = reader.Read("test", dir);
                Assert.Equal(VpnLogEvidence.Connected, second.Kind);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
