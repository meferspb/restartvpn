using System;
using System.Collections.Generic;
using System.Linq;
using RestartVpn.Core.Policy;
using RestartVpn.Core.Settings;
using RestartVpn.Core.Vpn;
using Xunit;

namespace RestartVpn.Tests
{
    public class DecisionEngineTests
    {
        private static DecisionEngine Engine(AppSettings s) => new DecisionEngine(s, () => 0.5);

        [Fact]
        public void Recovered_target_does_not_keep_incident_alive()
        {
            // Regression for the legacy bug: B failed once 10 s ago and recovered.
            // Only A is down now, so the VPN must NOT be restarted.
            var s = TestHelpers.AutoSettings();
            var engine = Engine(s);
            var targets = new List<TargetSnapshot> { TestHelpers.Target("a", 3), TestHelpers.Target("b", 0) };

            var decision = engine.Evaluate(targets, TestHelpers.Connected(), TestHelpers.T0);

            Assert.Equal(DecisionKind.ServiceAlert, decision.Kind);
            Assert.Contains("quorum", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void All_targets_down_restarts_vpn()
        {
            var s = TestHelpers.AutoSettings();
            var engine = Engine(s);
            var targets = new List<TargetSnapshot> { TestHelpers.Target("a", 3), TestHelpers.Target("b", 4) };

            var decision = engine.Evaluate(targets, TestHelpers.Connected(), TestHelpers.T0);

            Assert.Equal(DecisionKind.RestartSoft, decision.Kind);
        }

        [Fact]
        public void Missing_process_connects_without_killing_anything()
        {
            var s = TestHelpers.AutoSettings();
            var engine = Engine(s);
            var targets = new List<TargetSnapshot> { TestHelpers.Target("a", 3) };

            var decision = engine.Evaluate(targets, TestHelpers.NoProcess(), TestHelpers.T0);

            Assert.Equal(DecisionKind.Connect, decision.Kind);
        }

        [Fact]
        public void Fatal_auth_error_is_not_restarted()
        {
            var s = TestHelpers.AutoSettings();
            var engine = Engine(s);
            var vpn = TestHelpers.Connected();
            vpn.State = VpnState.Error;
            vpn.Evidence = VpnLogEvidence.FatalAuth;
            vpn.EvidenceDetail = "AUTH_FAILED";

            var decision = engine.Evaluate(new List<TargetSnapshot> { TestHelpers.Target("a", 3) }, vpn, TestHelpers.T0);

            Assert.Equal(DecisionKind.VpnAlert, decision.Kind);
        }

        [Fact]
        public void Tunnel_up_and_all_sites_down_restarts_by_default()
        {
            // The main use case: DPI blocking keeps the tunnel "up" while every site is blocked.
            var s = TestHelpers.AutoSettings();
            var engine = Engine(s);

            var decision = engine.Evaluate(
                new List<TargetSnapshot> { TestHelpers.Target("a", 3), TestHelpers.Target("b", 3) },
                TestHelpers.Connected(), TestHelpers.T0);

            Assert.Equal(DecisionKind.RestartSoft, decision.Kind);
        }

        [Fact]
        public void Tunnel_up_is_only_an_alert_when_tunnel_evidence_is_required()
        {
            var s = TestHelpers.AutoSettings();
            s.Policy.RequireTunnelEvidence = true;
            var engine = Engine(s);
            var targets = new List<TargetSnapshot> { TestHelpers.Target("a", 3), TestHelpers.Target("b", 3) };

            var cautious = engine.Evaluate(targets, TestHelpers.Connected(), TestHelpers.T0);
            Assert.Equal(DecisionKind.ServiceAlert, cautious.Kind);

            s.Policy.RestartOnServiceOutage = true;
            var optedIn = Engine(s).Evaluate(targets, TestHelpers.Connected(), TestHelpers.T0);
            Assert.Equal(DecisionKind.RestartSoft, optedIn.Kind);
        }

        [Fact]
        public void Monitor_only_never_acts()
        {
            var s = TestHelpers.AutoSettings();
            s.Mode = "MonitorOnly";
            var engine = Engine(s);

            var decision = engine.Evaluate(
                new List<TargetSnapshot> { TestHelpers.Target("a", 3) },
                TestHelpers.NoProcess(), TestHelpers.T0);

            Assert.Equal(DecisionKind.ServiceAlert, decision.Kind);
        }

        [Fact]
        public void Backoff_blocks_immediate_retry_and_escalates_to_hard()
        {
            var s = TestHelpers.AutoSettings();
            var engine = Engine(s);
            var targets = new List<TargetSnapshot> { TestHelpers.Target("a", 3) };

            var first = engine.Evaluate(targets, TestHelpers.Connected(), TestHelpers.T0);
            Assert.Equal(DecisionKind.RestartSoft, first.Kind);

            engine.RecordAttemptResult(false, TestHelpers.T0);

            var waiting = engine.Evaluate(targets, TestHelpers.Connected(), TestHelpers.T0.AddSeconds(5));
            Assert.Equal(DecisionKind.WaitingBackoff, waiting.Kind);

            var later = engine.Evaluate(targets, TestHelpers.Connected(), TestHelpers.T0.AddSeconds(61));
            Assert.Equal(DecisionKind.RestartHard, later.Kind);
        }

        [Fact]
        public void Circuit_breaker_opens_after_failed_restarts()
        {
            var s = TestHelpers.AutoSettings();
            s.Policy.FailedRestartsBeforeCircuit = 2;
            s.Policy.CircuitCooldownSec = 900;
            var engine = Engine(s);
            var targets = new List<TargetSnapshot> { TestHelpers.Target("a", 3) };

            engine.RecordAttemptResult(false, TestHelpers.T0);
            engine.RecordAttemptResult(false, TestHelpers.T0.AddSeconds(30));

            var open = engine.Evaluate(targets, TestHelpers.Connected(), TestHelpers.T0.AddSeconds(60));
            Assert.Equal(DecisionKind.CircuitOpen, open.Kind);

            var afterCooldown = engine.Evaluate(targets, TestHelpers.Connected(), TestHelpers.T0.AddSeconds(1000));
            Assert.NotEqual(DecisionKind.CircuitOpen, afterCooldown.Kind);
        }

        [Fact]
        public void Majority_quorum_works()
        {
            var s = TestHelpers.AutoSettings();
            s.Policy.Quorum = "Majority";
            var engine = Engine(s);

            var decision = engine.Evaluate(
                new List<TargetSnapshot> { TestHelpers.Target("a", 3), TestHelpers.Target("b", 0), TestHelpers.Target("c", 0) },
                TestHelpers.Connected(), TestHelpers.T0);

            Assert.Equal(DecisionKind.ServiceAlert, decision.Kind);
        }

        [Fact]
        public void Unknown_process_list_is_never_killed()
        {
            var s = TestHelpers.AutoSettings();
            var engine = Engine(s);
            var vpn = new VpnStatus
            {
                State = VpnState.Unknown,
                AnyOpenVpnProcess = true,
                ProfileProcessPresent = false,
                Detail = "cannot read command lines"
            };

            var decision = engine.Evaluate(new List<TargetSnapshot> { TestHelpers.Target("a", 3) }, vpn, TestHelpers.T0);

            Assert.Equal(DecisionKind.VpnAlert, decision.Kind);
        }
    }
}
