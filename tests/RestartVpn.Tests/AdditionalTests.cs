using System;
using System.IO;
using RestartVpn.Core.Policy;
using RestartVpn.Core.Settings;
using RestartVpn.Core.Vpn;
using Xunit;

namespace RestartVpn.Tests
{
    public class AdditionalPolicyTests
    {
        [Fact]
        public void Stale_failure_does_not_trigger_restart()
        {
            var s = TestHelpers.AutoSettings();
            var engine = new DecisionEngine(s, () => 0.5);
            var target = TestHelpers.Target("a", 3);
            target.LastFailureAt = TestHelpers.T0.AddMinutes(-10);

            var decision = engine.Evaluate(new[] { target }, TestHelpers.NoProcess(), TestHelpers.T0);

            Assert.Equal(DecisionKind.None, decision.Kind);
        }

        [Fact]
        public void Count_quorum_is_supported()
        {
            var s = TestHelpers.AutoSettings();
            s.Policy.Quorum = "Count:2";
            var engine = new DecisionEngine(s, () => 0.5);
            var targets = new[]
            {
                TestHelpers.Target("a", 3),
                TestHelpers.Target("b", 3),
                TestHelpers.Target("c", 0)
            };

            var decision = engine.Evaluate(targets, TestHelpers.Connected(), TestHelpers.T0);
            Assert.Equal(DecisionKind.RestartSoft, decision.Kind);
        }
    }

    public class SettingsPersistenceTests
    {
        [Fact]
        public void Save_and_load_round_trip_on_disk()
        {
            string dir = Path.Combine(Path.GetTempPath(), "restartvpn-save-" + Guid.NewGuid().ToString("N"));
            try
            {
                var paths = AppPaths.ForDataDirectory(dir);
                var store = new SettingsStore(paths);
                var settings = TestHelpers.AutoSettings();
                settings.Targets.Add(new TargetSettings { Name = "a", Url = "https://example.com/" });

                store.Save(settings);
                Assert.True(File.Exists(paths.SettingsPath));

                var loaded = store.Load();
                Assert.True(loaded.Existed);
                Assert.Equal("AutoRestart", loaded.Settings.Mode);
                Assert.Single(loaded.Settings.Targets);
                Assert.Equal("https://example.com/", loaded.Settings.Targets[0].Url);

                store.Save(settings);
                Assert.True(File.Exists(paths.SettingsPath + ".bak"));
                Assert.False(loaded.HasErrors);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }

    public class ProfileSafetyTests
    {
        [Fact]
        public void Profile_name_in_directory_does_not_match_other_file()
        {
            string cmd = "openvpn.exe --config \"C:\\profiles\\my-profile\\other.ovpn\"";
            Assert.False(OpenVpnCommandLine.MatchesProfile(cmd, "my-profile", ""));
        }
    }

    public class JsonEdgeCaseTests
    {
        [Fact]
        public void Empty_containers_keep_valid_json()
        {
            string pretty = JsonText.Prettify("{\"a\":{},\"b\":[],\"c\":1}");
            var parsed = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<object>(pretty);
            Assert.NotNull(parsed);
        }
    }
}
