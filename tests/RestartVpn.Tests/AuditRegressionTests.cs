using System;
using System.Linq;
using RestartVpn.Core.Policy;
using RestartVpn.Core.Settings;
using RestartVpn.Core.Vpn;
using Xunit;

namespace RestartVpn.Tests
{
    /// <summary>Regressions for defects found by the 2026-11-24 audit.</summary>
    public class AuditRegressionTests
    {
        [Fact]
        public void Failed_connect_respects_the_attempt_gap()
        {
            var s = TestHelpers.AutoSettings();
            s.Policy.ConnectCooldownSec = 1;      // shorter than the exponential gap
            s.Policy.MinRestartGapSec = 60;
            s.Policy.MaxRestartGapSec = 300;
            var engine = new DecisionEngine(s, () => 0.5);

            engine.RecordConnectAttempt(TestHelpers.T0);
            engine.RecordAttemptResult(false, TestHelpers.T0);

            var decision = engine.Evaluate(
                new[] { TestHelpers.Target("a", 3) },
                TestHelpers.NoProcess(),
                TestHelpers.T0.AddSeconds(2));

            Assert.Equal(DecisionKind.WaitingBackoff, decision.Kind);
        }

        [Fact]
        public void Stale_fatal_log_line_does_not_block_restarts()
        {
            var s = TestHelpers.AutoSettings();
            var engine = new DecisionEngine(s, () => 0.5);
            var vpn = TestHelpers.Connected();
            vpn.Evidence = VpnLogEvidence.FatalAuth;
            vpn.EvidenceDetail = "AUTH_FAILED";
            vpn.EvidenceFresh = false;

            var decision = engine.Evaluate(new[] { TestHelpers.Target("a", 3) }, vpn, TestHelpers.T0);

            Assert.Equal(DecisionKind.RestartSoft, decision.Kind);
        }

        [Fact]
        public void Profile_name_with_path_separators_is_rejected()
        {
            var s = TestHelpers.AutoSettings();
            s.Vpn.ProfileName = "../other";

            var issues = SettingsValidator.Validate(s);

            Assert.Contains(issues, i => i.Path == "vpn.profileName" && i.Severity == IssueSeverity.Error);
        }
    }
}
