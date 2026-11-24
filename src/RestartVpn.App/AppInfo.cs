using System;
using System.Reflection;

namespace RestartVpn.App
{
    public static class AppInfo
    {
        public const string Name = "RestartVPN";
        public const string RepositoryUrl = "https://github.com/meferspb/restartvpn";

        public static string Version
        {
            get
            {
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var attr = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                        asm, typeof(AssemblyInformationalVersionAttribute));
                    if (attr != null && !string.IsNullOrEmpty(attr.InformationalVersion))
                        return attr.InformationalVersion;
                    return asm.GetName().Version.ToString(3);
                }
                catch { return "0.1.1"; }
            }
        }
    }
}
