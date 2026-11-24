using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RestartVpn.Core.Settings;
using Xunit;

namespace RestartVpn.Tests
{
    public class SettingsTests
    {
        [Fact]
        public void Round_trip_preserves_values()
        {
            var s = new AppSettings();
            s.Mode = "AutoRestart";
            s.Vpn.ProfileName = "my-profile";
            s.Targets.Add(new TargetSettings
            {
                Name = "example",
                Url = "https://example.com/",
                TimeoutMs = 6000,
                ExpectedStatus = new[] { 200, 302 },
                Headers = new Dictionary<string, string> { ["X-Test"] = "1" }
            });

            string json = SettingsStore.Serialize(s);
            var back = SettingsStore.Deserialize(json);

            Assert.Equal("AutoRestart", back.Mode);
            Assert.Equal("my-profile", back.Vpn.ProfileName);
            Assert.Single(back.Targets);
            Assert.Equal("example", back.Targets[0].Name);
            Assert.Equal(new[] { 200, 302 }, back.Targets[0].ExpectedStatus);
            Assert.Equal("1", back.Targets[0].Headers["X-Test"]);
        }

        [Fact]
        public void Pretty_json_is_still_valid_json()
        {
            var s = new AppSettings();
            string json = SettingsStore.Serialize(s);
            Assert.Contains("\n", json);
            var back = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<object>(json);
            Assert.NotNull(back);
        }

        [Fact]
        public void Validator_rejects_bad_url_and_timeouts()
        {
            var s = new AppSettings();
            s.Targets.Add(new TargetSettings { Name = "x", Url = "ftp://example.com", TimeoutMs = 10 });
            var issues = SettingsValidator.Validate(s);

            Assert.Contains(issues, i => i.Path == "targets[0].url");
            Assert.Contains(issues, i => i.Path == "targets[0].timeoutMs");
        }

        [Fact]
        public void Validator_requires_enabled_target()
        {
            var s = new AppSettings();
            var issues = SettingsValidator.Validate(s);
            Assert.Contains(issues, i => i.Path == "targets" && i.Severity == IssueSeverity.Error);
        }

        [Fact]
        public void Partial_json_gets_defaults()
        {
            var s = SettingsStore.Deserialize("{\"mode\":\"AutoRestart\"}");
            Assert.NotNull(s.Vpn);
            Assert.NotNull(s.Policy);
            Assert.NotNull(s.Probe);
        }
    }
}
