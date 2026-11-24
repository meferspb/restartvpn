using RestartVpn.Core.Vpn;
using Xunit;

namespace RestartVpn.Tests
{
    public class OpenVpnCommandLineTests
    {
        private const string Profile = "my-profile";
        private const string ProfilePath = @"C:\Users\user\OpenVPN\config\my-profile.ovpn";

        [Fact]
        public void Matches_own_profile()
        {
            string cmd = "openvpn.exe --config \"" + ProfilePath + "\" --service 0";
            Assert.True(OpenVpnCommandLine.MatchesProfile(cmd, Profile, ProfilePath));
        }

        [Fact]
        public void Does_not_match_similar_profile()
        {
            string cmd = "openvpn.exe --config \"C:\\Users\\user\\OpenVPN\\config\\my-profile-backup.ovpn\"";
            Assert.False(OpenVpnCommandLine.MatchesProfile(cmd, Profile, ""));
            Assert.False(OpenVpnCommandLine.MatchesProfile(cmd, Profile, ProfilePath));
        }

        [Fact]
        public void Matches_by_file_name_when_path_is_unknown()
        {
            string cmd = "openvpn.exe --config \"D:\\vpn\\my-profile.ovpn\"";
            Assert.True(OpenVpnCommandLine.MatchesProfile(cmd, Profile, ""));
        }

        [Fact]
        public void Supports_config_equals_syntax()
        {
            string cmd = "openvpn.exe --config=C:\\vpn\\my-profile.ovpn";
            Assert.True(OpenVpnCommandLine.MatchesProfile(cmd, Profile, ""));
        }

        [Fact]
        public void Tokenizes_quoted_arguments()
        {
            var tokens = OpenVpnCommandLine.Tokenize("openvpn.exe --config \"C:\\a b\\p.ovpn\" --verb 3");
            Assert.Equal(5, tokens.Count);
            Assert.Equal("--config", tokens[1]);
            Assert.Equal(@"C:\a b\p.ovpn", tokens[2]);
        }

        [Fact]
        public void Empty_or_foreign_command_line_never_matches()
        {
            Assert.False(OpenVpnCommandLine.MatchesProfile("", Profile, ProfilePath));
            Assert.False(OpenVpnCommandLine.MatchesProfile("openvpn.exe --config \"C:\\other.ovpn\"", Profile, ProfilePath));
        }
    }
}