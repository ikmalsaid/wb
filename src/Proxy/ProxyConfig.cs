using System;
using System.Collections.Generic;

namespace wb.Proxy
{
    public class ProxyConfig
    {
        public bool IsEnabled { get; set; }
        public ProxyMode Mode { get; set; }
        public string Scheme { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string BypassList { get; set; }
        public string PacUrl { get; set; }
        public bool UseAuth { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

        public ProxyConfig()
        {
            IsEnabled = true;
            Mode = ProxyMode.System;
            Scheme = "http";
            Host = string.Empty;
            Port = 8080;
            BypassList = "<local>;localhost;127.0.0.1";
            PacUrl = string.Empty;
            UseAuth = false;
            Username = string.Empty;
            Password = string.Empty;
        }

        public string GetServerUrl()
        {
            if (string.IsNullOrEmpty(Host))
            {
                return string.Empty;
            }

            string scheme = string.IsNullOrEmpty(Scheme) ? "http" : Scheme.ToLowerInvariant();
            int port = Port > 0 ? Port : (scheme == "socks5" || scheme == "socks4" ? 1080 : 8080);
            return string.Format("{0}://{1}:{2}", scheme, Host.Trim(), port);
        }

        public IDictionary<string, object> ToChromiumDictionary()
        {
            var dict = new Dictionary<string, object>();

            if (!IsEnabled || Mode == ProxyMode.System)
            {
                dict["mode"] = "system";
                return dict;
            }

            switch (Mode)
            {
                case ProxyMode.Direct:
                    dict["mode"] = "direct";
                    break;

                case ProxyMode.AutoDetect:
                    dict["mode"] = "auto_detect";
                    break;

                case ProxyMode.PacScript:
                    dict["mode"] = "pac_script";
                    dict["pac_url"] = PacUrl ?? string.Empty;
                    break;

                case ProxyMode.Manual:
                    dict["mode"] = "fixed_servers";
                    string server = GetServerUrl();
                    if (!string.IsNullOrEmpty(server))
                    {
                        dict["server"] = server;
                    }
                    if (!string.IsNullOrEmpty(BypassList))
                    {
                        dict["bypass_list"] = BypassList;
                    }
                    break;

                default:
                    dict["mode"] = "system";
                    break;
            }

            return dict;
        }

        public ProxyConfig Clone()
        {
            return new ProxyConfig
            {
                IsEnabled = this.IsEnabled,
                Mode = this.Mode,
                Scheme = this.Scheme,
                Host = this.Host,
                Port = this.Port,
                BypassList = this.BypassList,
                PacUrl = this.PacUrl,
                UseAuth = this.UseAuth,
                Username = this.Username,
                Password = this.Password
            };
        }
    }
}
