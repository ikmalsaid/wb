using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using CefSharp;
using wb.Core;

namespace wb.Proxy
{
    public class ProxyManager
    {
        private const string ConfigFileName = "proxy.json";
        private readonly object _lock = new object();
        private ProxyConfig _currentConfig;

        public event EventHandler<ProxyConfig> ConfigChanged;

        public ProxyManager()
        {
            _currentConfig = StorageHelper.LoadObject<ProxyConfig>(ConfigFileName) ?? new ProxyConfig();
            ApplySystemNetProxy(_currentConfig);
        }

        public ProxyConfig CurrentConfig
        {
            get
            {
                lock (_lock)
                {
                    return _currentConfig.Clone();
                }
            }
        }

        public bool HasCredentials
        {
            get
            {
                lock (_lock)
                {
                    return _currentConfig != null &&
                           _currentConfig.IsEnabled &&
                           _currentConfig.Mode == ProxyMode.Manual &&
                           _currentConfig.UseAuth &&
                           !string.IsNullOrEmpty(_currentConfig.Username);
                }
            }
        }

        public string Username
        {
            get
            {
                lock (_lock)
                {
                    return _currentConfig != null ? _currentConfig.Username : string.Empty;
                }
            }
        }

        public string Password
        {
            get
            {
                lock (_lock)
                {
                    return _currentConfig != null ? _currentConfig.Password : string.Empty;
                }
            }
        }

        public void SaveAndApply(ProxyConfig newConfig)
        {
            if (newConfig == null) return;

            lock (_lock)
            {
                _currentConfig = newConfig.Clone();
                StorageHelper.SaveObject(ConfigFileName, _currentConfig);
            }

            ApplyProxy();

            EventHandler<ProxyConfig> handler = ConfigChanged;
            if (handler != null)
            {
                try
                {
                    handler(this, _currentConfig.Clone());
                }
                catch { }
            }
        }

        public void ApplyCommandLineArgs(IDictionary<string, string> commandLineArgs)
        {
            if (commandLineArgs == null) return;

            ProxyConfig config;
            lock (_lock)
            {
                config = _currentConfig.Clone();
            }

            if (!config.IsEnabled || config.Mode == ProxyMode.System)
            {
                return;
            }

            try
            {
                // Block WebRTC non-proxied UDP leakage which exposes the user's real ISP address
                commandLineArgs["webrtc-ip-handling-policy"] = "disable_non_proxied_udp";
                commandLineArgs["force-webrtc-ip-handling-policy"] = "disable_non_proxied_udp";

                // Suppress background local DNS prefetching over the local ISP connection
                commandLineArgs["dns-prefetch-disable"] = "1";

                switch (config.Mode)
                {
                    case ProxyMode.Direct:
                        commandLineArgs["no-proxy-server"] = "1";
                        break;

                    case ProxyMode.AutoDetect:
                        commandLineArgs["proxy-auto-detect"] = "1";
                        break;

                    case ProxyMode.PacScript:
                        if (!string.IsNullOrEmpty(config.PacUrl))
                        {
                            commandLineArgs["proxy-pac-url"] = config.PacUrl;
                        }
                        break;

                    case ProxyMode.Manual:
                        string server = config.GetServerUrl();
                        if (!string.IsNullOrEmpty(server))
                        {
                            commandLineArgs["proxy-server"] = server;
                        }
                        if (!string.IsNullOrEmpty(config.BypassList))
                        {
                            commandLineArgs["proxy-bypass-list"] = config.BypassList;
                        }
                        break;
                }
            }
            catch { }
        }

        public bool ApplyProxy(IRequestContext reqContext = null)
        {
            ProxyConfig config;
            lock (_lock)
            {
                config = _currentConfig.Clone();
            }

            IDictionary<string, object> dict = config.ToChromiumDictionary();
            string webrtcPolicy = (config.IsEnabled && config.Mode != ProxyMode.System && config.Mode != ProxyMode.Direct)
                ? "disable_non_proxied_udp"
                : "default_public_interface_only";

            bool applied = false;
            if (reqContext != null && !reqContext.IsDisposed)
            {
                ApplyContextPreferencesSafe(reqContext, dict, webrtcPolicy);
                applied = true;
            }

            IRequestContext globalContext = GetGlobalRequestContextSafe();
            if (globalContext != null && !globalContext.IsDisposed && globalContext != reqContext)
            {
                ApplyContextPreferencesSafe(globalContext, dict, webrtcPolicy);
                applied = true;
            }

            ApplySystemNetProxy(config);

            return applied;
        }

        private static void ApplySystemNetProxy(ProxyConfig config)
        {
            if (config == null) return;

            try
            {
                if (!config.IsEnabled || config.Mode == ProxyMode.System)
                {
                    WebRequest.DefaultWebProxy = WebRequest.GetSystemWebProxy();
                    return;
                }

                if (config.Mode == ProxyMode.Direct)
                {
                    WebRequest.DefaultWebProxy = null;
                    return;
                }

                if (config.Mode == ProxyMode.Manual)
                {
                    string scheme = (config.Scheme ?? "http").ToLowerInvariant();
                    if (scheme == "http" || scheme == "https")
                    {
                        var webProxy = new WebProxy(config.Host, config.Port > 0 ? config.Port : 8080);
                        if (!string.IsNullOrEmpty(config.BypassList))
                        {
                            string[] bypass = config.BypassList.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            webProxy.BypassList = bypass;
                        }
                        if (config.UseAuth && !string.IsNullOrEmpty(config.Username))
                        {
                            webProxy.Credentials = new NetworkCredential(config.Username, config.Password ?? string.Empty);
                        }
                        WebRequest.DefaultWebProxy = webProxy;
                    }
                }
            }
            catch { }
        }

        public static void ApplyContextPreferencesSafe(IRequestContext context, IDictionary<string, object> proxyDict, string webrtcPolicy)
        {
            if (context == null || context.IsDisposed) return;

            try
            {
                TaskFactory uiFactory = GetUIThreadTaskFactorySafe();
                if (uiFactory != null)
                {
                    uiFactory.StartNew(new Action(() =>
                    {
                        try
                        {
                            if (context != null && !context.IsDisposed)
                            {
                                string err;
                                context.SetPreference("proxy", proxyDict, out err);
                                if (!string.IsNullOrEmpty(webrtcPolicy))
                                {
                                    context.SetPreference("webrtc.ip_handling_policy", webrtcPolicy, out err);
                                }
                            }
                        }
                        catch { }
                    }));
                }
                else
                {
                    string err;
                    context.SetPreference("proxy", proxyDict, out err);
                    if (!string.IsNullOrEmpty(webrtcPolicy))
                    {
                        context.SetPreference("webrtc.ip_handling_policy", webrtcPolicy, out err);
                    }
                }
            }
            catch { }
        }

        public Task<Tuple<bool, string, long>> TestConnectionAsync(ProxyConfig testConfig)
        {
            return Task.Run(() =>
            {
                if (testConfig == null)
                {
                    return Tuple.Create(false, "Invalid configuration", 0L);
                }

                if (testConfig.Mode == ProxyMode.Direct)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var req = (HttpWebRequest)WebRequest.Create("http://www.google.com/generate_204");
                        req.Timeout = 5000;
                        req.Proxy = null;
                        using (var resp = (HttpWebResponse)req.GetResponse())
                        {
                            sw.Stop();
                            return Tuple.Create(true, "Direct connection successful", sw.ElapsedMilliseconds);
                        }
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        return Tuple.Create(false, "Direct connection failed: " + ex.Message, sw.ElapsedMilliseconds);
                    }
                }

                if (testConfig.Mode == ProxyMode.System || testConfig.Mode == ProxyMode.AutoDetect)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var req = (HttpWebRequest)WebRequest.Create("http://www.google.com/generate_204");
                        req.Timeout = 5000;
                        req.Proxy = WebRequest.GetSystemWebProxy();
                        using (var resp = (HttpWebResponse)req.GetResponse())
                        {
                            sw.Stop();
                            return Tuple.Create(true, "System proxy connected", sw.ElapsedMilliseconds);
                        }
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        return Tuple.Create(false, "System proxy connection failed: " + ex.Message, sw.ElapsedMilliseconds);
                    }
                }

                if (testConfig.Mode == ProxyMode.PacScript)
                {
                    if (string.IsNullOrEmpty(testConfig.PacUrl))
                    {
                        return Tuple.Create(false, "PAC URL cannot be empty", 0L);
                    }
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var req = (HttpWebRequest)WebRequest.Create(testConfig.PacUrl);
                        req.Timeout = 5000;
                        using (var resp = (HttpWebResponse)req.GetResponse())
                        {
                            sw.Stop();
                            return Tuple.Create(true, "PAC script reachable", sw.ElapsedMilliseconds);
                        }
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        return Tuple.Create(false, "Cannot reach PAC script: " + ex.Message, sw.ElapsedMilliseconds);
                    }
                }

                if (testConfig.Mode == ProxyMode.Manual)
                {
                    if (string.IsNullOrEmpty(testConfig.Host))
                    {
                        return Tuple.Create(false, "Proxy host cannot be empty", 0L);
                    }

                    string scheme = string.IsNullOrEmpty(testConfig.Scheme) ? "http" : testConfig.Scheme.ToLowerInvariant();
                    int port = testConfig.Port > 0 ? testConfig.Port : (scheme.StartsWith("socks") ? 1080 : 8080);

                    if (scheme.StartsWith("socks"))
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            using (var tcp = new TcpClient())
                            {
                                var connectTask = tcp.ConnectAsync(testConfig.Host, port);
                                if (!connectTask.Wait(5000))
                                {
                                    sw.Stop();
                                    return Tuple.Create(false, string.Format("Connection to {0}:{1} timed out", testConfig.Host, port), sw.ElapsedMilliseconds);
                                }

                                if (scheme == "socks5")
                                {
                                    var stream = tcp.GetStream();
                                    stream.Write(new byte[] { 0x05, 0x01, 0x00 }, 0, 3);
                                    byte[] reply = new byte[2];
                                    int read = stream.Read(reply, 0, 2);
                                    if (read < 2 || reply[0] != 0x05)
                                    {
                                        sw.Stop();
                                        return Tuple.Create(false, "Unexpected response from SOCKS5 server", sw.ElapsedMilliseconds);
                                    }
                                    if (reply[1] != 0x00)
                                    {
                                        sw.Stop();
                                        return Tuple.Create(false, "SOCKS5 authentication required or method rejected", sw.ElapsedMilliseconds);
                                    }

                                    // Verify outbound tunneling capability (without this, dead proxies pass handshake but fail browsing)
                                    byte[] connectCmd = new byte[] {
                                        0x05, 0x01, 0x00, 0x01,
                                        0x01, 0x01, 0x01, 0x01, // 1.1.1.1
                                        0x00, 0x50              // Port 80
                                    };
                                    stream.Write(connectCmd, 0, connectCmd.Length);
                                    byte[] connReply = new byte[10];
                                    int connRead = stream.Read(connReply, 0, 10);
                                    sw.Stop();

                                    if (connRead >= 2 && connReply[0] == 0x05)
                                    {
                                        byte rep = connReply[1];
                                        if (rep == 0x00)
                                        {
                                            return Tuple.Create(true, "SOCKS5 proxy connected & tunnel verified", sw.ElapsedMilliseconds);
                                        }
                                        string repDesc;
                                        switch (rep)
                                        {
                                            case 1: repDesc = "General SOCKS server failure"; break;
                                            case 2: repDesc = "Connection not allowed by ruleset"; break;
                                            case 3: repDesc = "Network unreachable"; break;
                                            case 4: repDesc = "Host unreachable"; break;
                                            case 5: repDesc = "Connection refused"; break;
                                            case 6: repDesc = "TTL expired"; break;
                                            case 7: repDesc = "Command not supported"; break;
                                            case 8: repDesc = "Address type not supported"; break;
                                            default: repDesc = "Error " + rep; break;
                                        }
                                        return Tuple.Create(false, "SOCKS5 tunnel failed: " + repDesc, sw.ElapsedMilliseconds);
                                    }
                                    return Tuple.Create(false, "SOCKS5 server closed connection during tunnel setup", sw.ElapsedMilliseconds);
                                }

                                sw.Stop();
                                return Tuple.Create(true, string.Format("{0} proxy port open", scheme.ToUpperInvariant()), sw.ElapsedMilliseconds);
                            }
                        }
                        catch (Exception ex)
                        {
                            sw.Stop();
                            return Tuple.Create(false, string.Format("Cannot reach {0}:{1} ({2})", testConfig.Host, port, ex.Message), sw.ElapsedMilliseconds);
                        }
                    }

                    // HTTP / HTTPS proxy test
                    var swHttp = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var testUri = new Uri("http://www.google.com/generate_204");
                        var request = (HttpWebRequest)WebRequest.Create(testUri);
                        request.Timeout = 5000;
                        request.ReadWriteTimeout = 5000;

                        var webProxy = new WebProxy(testConfig.Host, port);
                        if (testConfig.UseAuth && !string.IsNullOrEmpty(testConfig.Username))
                        {
                            webProxy.Credentials = new NetworkCredential(testConfig.Username, testConfig.Password ?? string.Empty);
                        }
                        request.Proxy = webProxy;

                        using (var response = (HttpWebResponse)request.GetResponse())
                        {
                            swHttp.Stop();
                            return Tuple.Create(true, "Connected successfully", swHttp.ElapsedMilliseconds);
                        }
                    }
                    catch (WebException wex)
                    {
                        swHttp.Stop();
                        if (wex.Response != null && wex.Response is HttpWebResponse)
                        {
                            HttpWebResponse httpResp = (HttpWebResponse)wex.Response;
                            if ((int)httpResp.StatusCode == 407)
                            {
                                return Tuple.Create(false, "Proxy authentication required (HTTP 407)", swHttp.ElapsedMilliseconds);
                            }
                            if ((int)httpResp.StatusCode == 200 || (int)httpResp.StatusCode == 204)
                            {
                                return Tuple.Create(true, "Connected successfully", swHttp.ElapsedMilliseconds);
                            }
                            return Tuple.Create(true, string.Format("Proxy responded with HTTP {0}", (int)httpResp.StatusCode), swHttp.ElapsedMilliseconds);
                        }
                        if (wex.Status == WebExceptionStatus.ConnectFailure || wex.Status == WebExceptionStatus.Timeout || wex.Message.IndexOf("connect", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return Tuple.Create(false, string.Format("Cannot connect to proxy {0}:{1} ({2})", testConfig.Host, port, wex.Message), swHttp.ElapsedMilliseconds);
                        }
                        return Tuple.Create(false, string.Format("Proxy error {0}:{1}: {2}", testConfig.Host, port, wex.Message), swHttp.ElapsedMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        swHttp.Stop();
                        return Tuple.Create(false, ex.Message, swHttp.ElapsedMilliseconds);
                    }
                }

                return Tuple.Create(true, "OK", 0L);
            });
        }

        private static IRequestContext GetGlobalRequestContextSafe()
        {
            try
            {
                Type cefType = FindCefType();
                if (cefType != null)
                {
                    PropertyInfo isInitProp = cefType.GetProperty("IsInitialized", BindingFlags.Public | BindingFlags.Static);
                    if (isInitProp != null && true.Equals(isInitProp.GetValue(null, null)))
                    {
                        MethodInfo method = cefType.GetMethod("GetGlobalRequestContext", Type.EmptyTypes);
                        if (method != null)
                        {
                            return method.Invoke(null, null) as IRequestContext;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static TaskFactory GetUIThreadTaskFactorySafe()
        {
            try
            {
                Type cefType = FindCefType();
                if (cefType != null)
                {
                    PropertyInfo prop = cefType.GetProperty("UIThreadTaskFactory", BindingFlags.Public | BindingFlags.Static);
                    if (prop != null)
                    {
                        return prop.GetValue(null, null) as TaskFactory;
                    }
                }
            }
            catch { }
            return null;
        }

        private static Type FindCefType()
        {
            Type t = Type.GetType("CefSharp.Cef, wb.Core") ?? Type.GetType("CefSharp.Cef, CefSharp.Core");
            if (t != null) return t;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    t = assemblies[i].GetType("CefSharp.Cef");
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
