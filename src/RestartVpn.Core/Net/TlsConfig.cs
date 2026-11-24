using System;
using System.Net;

namespace RestartVpn.Core.Net
{
    public static class TlsConfig
    {
        /// <summary>
        /// Enables TLS 1.2 (and 1.1/1.0 for old servers) for the whole process. On Windows 7
        /// this is required unless the OS was patched to enable TLS 1.2 for Schannel.
        /// </summary>
        public static void EnableModernTls(bool includeTls12)
        {
            try
            {
                SecurityProtocolType value = SecurityProtocolType.Tls;
                if (includeTls12) value |= SecurityProtocolType.Tls12;
                try { value |= SecurityProtocolType.Tls11; } catch { }
                ServicePointManager.SecurityProtocol = value;
            }
            catch { }
        }
    }
}
