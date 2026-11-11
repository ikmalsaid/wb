using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;
using wb.Core;
using wb.UI;

namespace wb.Browser
{
    public enum WebErrorKind
    {
        InsecureWarning,
        NotFound,
        ConnectionRefused,
        TimedOut,
        InternetOffline,
        Generic
    }

    public class WebErrorInfo
    {
        public WebErrorKind Kind { get; set; }
        public string TargetUrl { get; set; }
        public string Domain { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string TechnicalCode { get; set; }
        public string Description { get; set; }
        public string TechnicalDetails { get; set; }

        public WebErrorInfo()
        {
            Kind = WebErrorKind.Generic;
            Title = AppStrings.WebError.DefaultPageTitle;
            Subtitle = AppStrings.WebError.DefaultSubtitle;
        }
    }

    public class ActionButton
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public string Href { get; set; }
        public string OnClick { get; set; }
        public bool IsPrimary { get; set; }
    }

    public class UnifiedErrorModel
    {
        public string PageTitle { get; set; }
        public string BadgeIconSvg { get; set; }
        public string ThemeVariant { get; set; } // "accent", "warning", "danger"
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string TargetUrl { get; set; }
        public string Domain { get; set; }
        public string FormattedUrlHtml { get; set; }
        public string Description { get; set; }
        public string FilterSource { get; set; }
        public string MatchedRule { get; set; }
        public string ErrorCode { get; set; }
        public string RedirectUrl { get; set; }
        public bool ShowPermanentWhitelistCheckbox { get; set; }
        public string WhitelistCheckboxText { get; set; }
        public string AdvancedDetails { get; set; }
        public string DangerBypassUrl { get; set; }
        public string DangerBypassText { get; set; }
        public List<ActionButton> Actions { get; set; }
        public string CustomScript { get; set; }

        public UnifiedErrorModel()
        {
            ThemeVariant = "accent";
            Actions = new List<ActionButton>();
        }
    }

    public static class PhosphorErrorIcons
    {
        public const string WarningCircle = @"<svg viewBox=""0 0 256 256""><path d=""M128,24A104,104,0,1,0,232,128,104.11,104.11,0,0,0,128,24Zm0,192a88,88,0,1,1,88-88A88.1,88.1,0,0,1,128,216Zm-8-80V80a8,8,0,0,1,16,0v56a8,8,0,0,1-16,0Zm20,36a12,12,0,1,1-12-12A12,12,0,0,1,140,172Z""/></svg>";
        public const string WifiSlash = @"<svg viewBox=""0 0 256 256""><path d=""M213.92,210.62l-160-176A8,8,0,1,0,42.08,45.38L62.77,68.13C41.27,77.58,22.3,92.51,13.88,100.16a8,8,0,0,0,0,11.75l23.53,21.4a8,8,0,0,0,10.74,0l.21-.19a160,160,0,0,1,27.13-18.42l15.17,16.69a128.47,128.47,0,0,0-19.16,13.25l-.23.21a8,8,0,0,0,0,11.77L94.75,178a8,8,0,0,0,10.76,0l.3-.27a96.65,96.65,0,0,1,12.72-9.67l26.47,29.11-12,10.92a8,8,0,0,0,0,11.77l10.66,9.7a8,8,0,0,0,10.78,0l26.83-24.4,32.73,36A8,8,0,1,0,213.92,210.62ZM242.12,100.16C217.47,77.74,180.59,64,141.28,64a180.89,180.89,0,0,0-36.42,3.71l13.11,14.42a166.45,166.45,0,0,1,23.31-1.74c35,0,67.65,12.2,89.65,32.2l-23.53,21.4a128.53,128.53,0,0,0-57.19-23.36l13.14,14.45A112.56,112.56,0,0,1,190.72,138l-23.5,21.37a96.48,96.48,0,0,0-23.55-12.74l28.66,31.52,14.61-13.28a8,8,0,0,0,0-11.77l-23.47-21.34a112.35,112.35,0,0,0-17.65-12.72l12.78-11.62a8,8,0,0,0,0-11.75Z""/></svg>";
        public const string Plugs = @"<svg viewBox=""0 0 256 256""><path d=""M229.66,138.34l-32-32a8,8,0,0,0-11.32,11.32l9.34,9.34L160,162.69V136a8,8,0,0,0-16,0v42.69L117.66,152.34a8,8,0,0,0-11.32,11.32l40,40a8,8,0,0,0,11.32,0l40-40,9.34,9.34a8,8,0,0,0,11.32-11.32ZM96,120V77.31L138.34,120a8,8,0,0,0,11.32-11.32l-40-40a8,8,0,0,0-11.32,0L58.34,108.69a8,8,0,0,0,11.32,11.32L79,110.69V120a8,8,0,0,0,16,0ZM48,80a8,8,0,0,0,8-8V32a8,8,0,0,0-16,0V72A8,8,0,0,0,48,80Zm80-48a8,8,0,0,0-8,8V64a8,8,0,0,0,16,0V40A8,8,0,0,0,128,32Z""/></svg>";
        public const string Clock = @"<svg viewBox=""0 0 256 256""><path d=""M128,24A104,104,0,1,0,232,128,104.11,104.11,0,0,0,128,24Zm0,192a88,88,0,1,1,88-88A88.1,88.1,0,0,1,128,216Zm64-88a8,8,0,0,1-8,8H128a8,8,0,0,1-8-8V72a8,8,0,0,1,16,0v48h48A8,8,0,0,1,192,128Z""/></svg>";
        public const string Lock = @"<svg viewBox=""0 0 256 256""><path d=""M208,80H176V56a48,48,0,0,0-96,0V80H48A16,16,0,0,0,32,96V208a16,16,0,0,0,16,16H208a16,16,0,0,0,16-16V96A16,16,0,0,0,208,80ZM96,56a32,32,0,0,1,64,0V80H96ZM208,208H48V96H208V208Z""/></svg>";
        public const string ShieldWarning = @"<svg viewBox=""0 0 256 256""><path d=""M208,40H48A16,16,0,0,0,32,56v56c0,52.72,25.52,84.67,46.93,102.19,23.06,18.86,46,25.27,47,25.53a8,8,0,0,0,4.2,0c1-.26,23.91-6.67,47-25.53C198.48,196.67,224,164.72,224,112V56A16,16,0,0,0,208,40Zm0,72c0,37.07-13.66,67.16-40.6,89.42A129.3,129.3,0,0,1,128,223.62a128.25,128.25,0,0,1-38.92-21.81C61.82,179.51,48,149.3,48,112l0-56,160,0ZM120,88v48a8,8,0,0,0,16,0V88a8,8,0,0,0-16,0Zm8,80a10,10,0,1,0,10,10A10,10,0,0,0,128,168Z""/></svg>";
    }

    public static class WebErrorBypassManager
    {
        private static readonly HashSet<string> _temporarilyAllowedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _temporarilyAllowedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _bypassedSslHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        public static void AllowUrlTemporarily(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            lock (_lock)
            {
                _temporarilyAllowedUrls.Add(url);
                string domain = ExtractDomain(url);
                if (!string.IsNullOrEmpty(domain))
                {
                    _temporarilyAllowedDomains.Add(domain);
                }
            }
        }

        public static bool IsUrlAllowed(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            lock (_lock)
            {
                if (_temporarilyAllowedUrls.Contains(url)) return true;
                string domain = ExtractDomain(url);
                if (string.IsNullOrEmpty(domain)) return false;

                string cur = domain;
                while (!string.IsNullOrEmpty(cur))
                {
                    if (_temporarilyAllowedDomains.Contains(cur)) return true;
                    int dotIdx = cur.IndexOf('.');
                    if (dotIdx <= 0 || dotIdx >= cur.Length - 1) break;
                    string parent = cur.Substring(dotIdx + 1);
                    if (!parent.Contains(".")) break;
                    cur = parent;
                }
                return false;
            }
        }

        public static bool RemoveDomain(string domainOrUrl)
        {
            if (string.IsNullOrEmpty(domainOrUrl)) return false;
            string domain = ExtractDomain(domainOrUrl);
            if (string.IsNullOrEmpty(domain)) return false;

            bool removed = false;
            lock (_lock)
            {
                // 1. Remove exact domain and any matching parent or child subdomains
                List<string> domsToRemove = new List<string>();
                foreach (string d in _temporarilyAllowedDomains)
                {
                    if (d.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                        d.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase) ||
                        domain.EndsWith("." + d, StringComparison.OrdinalIgnoreCase))
                    {
                        domsToRemove.Add(d);
                    }
                }
                for (int i = 0; i < domsToRemove.Count; i++)
                {
                    if (_temporarilyAllowedDomains.Remove(domsToRemove[i]))
                    {
                        removed = true;
                    }
                }

                // 2. Remove all URLs matching this domain or its subdomains/parents
                List<string> urlsToRemove = new List<string>();
                foreach (string u in _temporarilyAllowedUrls)
                {
                    string uDom = ExtractDomain(u);
                    if (uDom.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                        uDom.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase) ||
                        domain.EndsWith("." + uDom, StringComparison.OrdinalIgnoreCase))
                    {
                        urlsToRemove.Add(u);
                    }
                }
                for (int i = 0; i < urlsToRemove.Count; i++)
                {
                    if (_temporarilyAllowedUrls.Remove(urlsToRemove[i]))
                    {
                        removed = true;
                    }
                }
            }
            return removed;
        }

        public static bool RemoveUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            lock (_lock)
            {
                return _temporarilyAllowedUrls.Remove(url);
            }
        }

        public static List<string> GetAllowedDomains()
        {
            lock (_lock)
            {
                return new List<string>(_temporarilyAllowedDomains);
            }
        }

        public static void AllowSslHostTemporarily(string host)
        {
            if (string.IsNullOrEmpty(host)) return;
            lock (_lock)
            {
                _bypassedSslHosts.Add(host.Trim().ToLowerInvariant());
            }
        }

        public static bool IsSslHostAllowed(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            lock (_lock)
            {
                return _bypassedSslHosts.Contains(host.Trim().ToLowerInvariant());
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _temporarilyAllowedUrls.Clear();
                _temporarilyAllowedDomains.Clear();
                _bypassedSslHosts.Clear();
            }
        }

        private static string ExtractDomain(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            string norm = UrlHelper.NormalizeDomain(url);
            if (!string.IsNullOrEmpty(norm)) return norm;
            string extracted = UrlHelper.ExtractDomain(url);
            return !string.IsNullOrEmpty(extracted) ? extracted : string.Empty;
        }
    }

    public static class WebErrorPage
    {
        private static string _cachedStaticCss;
        private static readonly object _cssLock = new object();

private static readonly byte[] _compressedCssBytes = new byte[] {
            0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0xCD, 0x59, 0xDB, 0x6E, 0xE3, 0x36, 0x10, 0x7D, 0xDF, 0xAF, 
            0x20, 0xB0, 0x28, 0xB2, 0xBB, 0xB0, 0x0C, 0xDB, 0x89, 0x1D, 0xAF, 0xF7, 0xA9, 0x2D, 0xB0, 0x68, 0x1F, 0xB6, 0x0F, 0x0D, 
            0xFA, 0x01, 0x94, 0x44, 0xC9, 0x6C, 0x24, 0x52, 0x20, 0x29, 0x3B, 0x6E, 0xD1, 0x7F, 0xEF, 0x90, 0x92, 0x1D, 0x89, 0xA4, 
            0x2E, 0xBE, 0xA4, 0x5D, 0x3D, 0xC4, 0x89, 0x45, 0x51, 0xC3, 0x99, 0x33, 0x67, 0xCE, 0x4C, 0x10, 0xAA, 0xAE, 0x4F, 0x13, 
            0xF4, 0x69, 0xB3, 0x09, 0x49, 0xC2, 0x05, 0x31, 0xBF, 0xE2, 0x44, 0x11, 0x81, 0xFE, 0x7E, 0x87, 0x1A, 0x57, 0xC8, 0x5F, 
            0x02, 0x49, 0xFF, 0xA2, 0x2C, 0xDD, 0xC0, 0xEF, 0x22, 0x26, 0x22, 0x80, 0xAF, 0xBE, 0xB4, 0xD6, 0xE4, 0x58, 0xA4, 0x94, 
            0x6D, 0xD0, 0xAC, 0xFD, 0x75, 0x81, 0xE3, 0xD8, 0x3C, 0xD7, 0xF8, 0xFE, 0x9F, 0x77, 0xAF, 0x1B, 0xC7, 0x87, 0x09, 0x0A, 
            0x4B, 0xA5, 0x38, 0x9B, 0x20, 0xCA, 0x8A, 0x52, 0x4D, 0x90, 0x24, 0x19, 0x89, 0xE0, 0x53, 0x91, 0x17, 0x85, 0x05, 0xC1, 
            0x96, 0x31, 0x09, 0x67, 0x2A, 0x48, 0x70, 0x4E, 0xB3, 0xC3, 0x06, 0xDD, 0x3D, 0x91, 0x94, 0x13, 0xF4, 0xC7, 0xAF, 0x77, 
            0x13, 0x14, 0xE0, 0xA2, 0xC8, 0x48, 0x20, 0x0F, 0x52, 0x91, 0x7C, 0x82, 0x7E, 0xCA, 0x28, 0x7B, 0xFE, 0x86, 0xA3, 0x27, 
            0xF3, 0xF7, 0x57, 0x78, 0x6A, 0x82, 0x7E, 0xE7, 0x21, 0x57, 0x7C, 0x82, 0xEE, 0x7E, 0x21, 0xD9, 0x8E, 0x28, 0x1A, 0x61, 
            0xF4, 0x1B, 0x29, 0x09, 0x3C, 0xFC, 0xA3, 0xA0, 0x38, 0x83, 0x77, 0x63, 0x26, 0x03, 0x49, 0x04, 0x4D, 0xBC, 0xE6, 0x5E, 
            0x6E, 0x28, 0x65, 0x5B, 0xD8, 0x55, 0x75, 0x39, 0xC1, 0x76, 0x38, 0x8E, 0x9E, 0x53, 0xC1, 0x4B, 0x16, 0x07, 0x11, 0xCF, 
            0xB8, 0xD8, 0xA0, 0x1D, 0x16, 0x1F, 0x82, 0x20, 0x4C, 0xAB, 0xBF, 0x3F, 0xB6, 0x9D, 0xDC, 0x5A, 0xA3, 0xAD, 0x09, 0x0A, 
            0x41, 0x21, 0x20, 0x07, 0x6B, 0x5D, 0x4E, 0x59, 0xB0, 0x25, 0x34, 0xDD, 0xAA, 0x0D, 0x9A, 0xCF, 0x66, 0xBB, 0x6D, 0xFB, 
            0x76, 0x4C, 0x65, 0x91, 0x61, 0x30, 0x36, 0xC9, 0x88, 0x15, 0x5D, 0xFD, 0x4D, 0x10, 0x53, 0x01, 0xE7, 0xA5, 0x1C, 0xA2, 
            0x0C, 0x6F, 0x2C, 0x73, 0xD6, 0x5E, 0x83, 0x33, 0x9A, 0xB2, 0x80, 0x82, 0xB3, 0x25, 0x2C, 0x20, 0x0C, 0x70, 0xD4, 0x5E, 
            0xF0, 0x67, 0x29, 0x15, 0x4D, 0x0E, 0x70, 0x04, 0xB8, 0xC7, 0x94, 0x7F, 0xD1, 0x09, 0x30, 0x0B, 0x41, 0x72, 0x34, 0x9F, 
            0x2E, 0xE1, 0xA3, 0xBD, 0xA2, 0x84, 0xF8, 0x04, 0x95, 0xEF, 0x37, 0x88, 0x71, 0x46, 0x7C, 0x4E, 0x9D, 0xEA, 0x97, 0x60, 
            0xCA, 0x1C, 0x2C, 0xE7, 0xF8, 0x25, 0xD8, 0xD3, 0x58, 0x6D, 0x37, 0x68, 0xF5, 0x30, 0x2B, 0xAC, 0x73, 0xD6, 0x77, 0xC0, 
            0x39, 0x3F, 0x7C, 0xE9, 0x88, 0xC8, 0xD1, 0xCF, 0x11, 0x16, 0x31, 0x04, 0xC4, 0x72, 0x71, 0x95, 0x1E, 0xB0, 0x43, 0xF1, 
            0x82, 0x24, 0xCF, 0x68, 0xDC, 0x5A, 0x6D, 0x6E, 0x7A, 0x9F, 0x08, 0x04, 0x8E, 0x69, 0x09, 0x8E, 0x5B, 0xD9, 0x26, 0xBD, 
            0x3A, 0x64, 0xBA, 0xD0, 0xCE, 0x38, 0x7E, 0xDA, 0xBB, 0x40, 0x8A, 0x6E, 0x71, 0xCC, 0xF7, 0xB6, 0x4F, 0xF4, 0x75, 0x45, 
            0x64, 0x1B, 0x2E, 0xDD, 0x12, 0x6C, 0x2C, 0xE5, 0x7B, 0xCB, 0xA7, 0x3D, 0xDB, 0xB7, 0x40, 0x61, 0xDE, 0x25, 0x21, 0x57, 
            0x54, 0x7B, 0x51, 0x8A, 0x0B, 0xF0, 0xD8, 0x74, 0xEE, 0x1E, 0xAB, 0x62, 0x15, 0x70, 0x1C, 0x24, 0x5E, 0xAE, 0xD7, 0x58, 
            0x47, 0x77, 0xAD, 0xA3, 0x10, 0x78, 0xCB, 0xBC, 0x3A, 0xA8, 0x6E, 0xB4, 0x8F, 0xB9, 0xE0, 0xDE, 0xD1, 0x99, 0xD2, 0xF9, 
            0xD8, 0xD8, 0xE3, 0x5E, 0x9C, 0x03, 0xF5, 0xA9, 0x15, 0x07, 0xB7, 0x2C, 0x9A, 0x6F, 0xEF, 0x38, 0xAE, 0xDC, 0xA5, 0xD7, 
            0x1F, 0x39, 0xA1, 0x59, 0x76, 0xE2, 0x19, 0x1C, 0xA7, 0xC4, 0xA1, 0x9A, 0xC6, 0xDB, 0x15, 0x55, 0x40, 0xB5, 0x3A, 0x21, 
            0x8A, 0xF1, 0x58, 0x18, 0x43, 0x22, 0x06, 0x0A, 0xB3, 0xEE, 0x30, 0x6F, 0xE7, 0x3E, 0x96, 0x85, 0xE2, 0x44, 0x34, 0x3A, 
            0xEE, 0x5D, 0x04, 0x99, 0xFB, 0xFB, 0xFA, 0xD4, 0xAB, 0x99, 0x55, 0x9E, 0xC6, 0x32, 0x67, 0x46, 0x94, 0xD2, 0xAC, 0x53, 
            0xE0, 0xC8, 0xE4, 0x62, 0x00, 0x26, 0xDA, 0x0E, 0x84, 0x7A, 0x43, 0x5E, 0xE9, 0x15, 0x8E, 0xE0, 0x31, 0x64, 0x44, 0x39, 
            0x98, 0xCA, 0x32, 0x34, 0xEE, 0xED, 0x3E, 0xE8, 0x6C, 0xFA, 0x79, 0xE1, 0x1C, 0xD4, 0x3D, 0x8A, 0x24, 0x00, 0x8E, 0xD8, 
            0x73, 0x98, 0xB6, 0xA1, 0x0F, 0x97, 0x1A, 0x5A, 0x8A, 0x4C, 0xEB, 0x80, 0xCE, 0xDA, 0xD5, 0x62, 0x4A, 0xCA, 0x98, 0x96, 
            0x0D, 0x37, 0xE7, 0xCB, 0x87, 0x4E, 0xBE, 0x9C, 0x4D, 0x57, 0x86, 0x2F, 0x67, 0xD3, 0xF5, 0xF9, 0xC4, 0xA2, 0xAF, 0x9B, 
            0xE6, 0xB9, 0x86, 0x0E, 0x09, 0x42, 0xA2, 0xF6, 0x84, 0xF8, 0x31, 0xFF, 0x78, 0x0E, 0xAB, 0x5B, 0x61, 0xD0, 0xF1, 0xEE, 
            0x93, 0x1F, 0x3F, 0x73, 0x06, 0xDE, 0xC5, 0x12, 0xA4, 0xCF, 0xD3, 0x57, 0xF4, 0x8D, 0x33, 0x0E, 0x9A, 0x07, 0x3E, 0x70, 
            0x04, 0x6A, 0x28, 0x87, 0x3F, 0x8D, 0x79, 0x1E, 0x1C, 0x1C, 0x01, 0xB7, 0x7E, 0x18, 0x01, 0x38, 0x7F, 0xEE, 0xEC, 0xB9, 
            0x0E, 0x25, 0xE8, 0xA3, 0x67, 0x50, 0x8F, 0xFA, 0x23, 0xC0, 0x59, 0xD6, 0x53, 0xD2, 0xF5, 0x56, 0xFD, 0x07, 0x0D, 0x7D, 
            0x47, 0x3D, 0xE6, 0xF8, 0x63, 0x47, 0x8E, 0xBF, 0x4F, 0xCC, 0xD5, 0x21, 0x16, 0x8A, 0x43, 0x10, 0x2A, 0xBB, 0x70, 0xBC, 
            0x5D, 0xD1, 0x1F, 0x9F, 0xAC, 0xA3, 0xE1, 0x0E, 0x77, 0x40, 0xB7, 0x38, 0xAC, 0xDE, 0x0C, 0xA2, 0x07, 0x61, 0xFD, 0xC9, 
            0x6E, 0x4C, 0x2D, 0x85, 0xD4, 0xB6, 0x16, 0x9C, 0xBA, 0x30, 0x57, 0x02, 0x04, 0x33, 0xAD, 0xE8, 0x1C, 0xA2, 0x0A, 0xEF, 
            0x98, 0x2F, 0x25, 0x22, 0x58, 0x5A, 0x58, 0xDA, 0x6F, 0x21, 0x57, 0x0C, 0x7D, 0x12, 0x8D, 0xE4, 0xBD, 0xC0, 0xC5, 0x05, 
            0x48, 0x3F, 0x86, 0x69, 0xB3, 0xE5, 0x3B, 0xB7, 0x49, 0x71, 0x82, 0x05, 0x2B, 0x03, 0xB3, 0xF2, 0x42, 0xB9, 0x5C, 0xBB, 
            0xFE, 0x88, 0x9F, 0x65, 0xB2, 0xBA, 0x5F, 0xAD, 0xBD, 0x86, 0x51, 0x96, 0x70, 0x13, 0xED, 0xEF, 0x95, 0x0B, 0xE7, 0x95, 
            0x98, 0xBE, 0x40, 0x61, 0xE9, 0xAB, 0xCD, 0x04, 0xEB, 0xDB, 0x95, 0x9E, 0xE5, 0x05, 0x20, 0x78, 0xF5, 0xB5, 0x2D, 0x3D, 
            0x46, 0x17, 0xAE, 0x9C, 0x28, 0x7C, 0x85, 0x8C, 0xF5, 0xF1, 0x7D, 0xCD, 0xE1, 0x9D, 0xFE, 0x35, 0x5A, 0x0E, 0x2A, 0xD2, 
            0x80, 0x6F, 0x17, 0x5D, 0xEA, 0xD6, 0x98, 0x9C, 0xE1, 0x90, 0x64, 0x96, 0xD1, 0xAE, 0xEF, 0xF3, 0x52, 0x91, 0xF8, 0xE3, 
            0x39, 0x69, 0xDE, 0x78, 0x0F, 0xA8, 0x40, 0x2D, 0x74, 0x0A, 0x10, 0x83, 0x3D, 0x58, 0x16, 0x69, 0x88, 0x3F, 0x2C, 0x1E, 
            0x16, 0x13, 0x34, 0xBF, 0xFF, 0xAC, 0x7F, 0xCC, 0x26, 0x3A, 0xF5, 0x17, 0x83, 0x80, 0xF6, 0x3F, 0x78, 0xEF, 0x4F, 0xD0, 
            0xF7, 0xC9, 0x62, 0x1D, 0xAE, 0x17, 0x5D, 0xED, 0x10, 0x6C, 0xFA, 0x68, 0x03, 0xDE, 0x4A, 0x88, 0x7B, 0x2F, 0x1D, 0xDE, 
            0xAE, 0x28, 0x76, 0x45, 0x2B, 0xE2, 0x31, 0xF1, 0xF9, 0xF0, 0x84, 0x30, 0xCA, 0x4C, 0x26, 0x84, 0x19, 0x8F, 0x9E, 0xFF, 
            0xAB, 0x46, 0x73, 0x1C, 0x52, 0xDE, 0xC4, 0xBF, 0x63, 0x9C, 0xF9, 0xE8, 0xF2, 0x8A, 0x95, 0x3B, 0x9D, 0xAD, 0x9F, 0x20, 
            0x55, 0x57, 0x31, 0x4A, 0x8D, 0xE2, 0x48, 0xE7, 0x6F, 0xA0, 0x55, 0x76, 0x46, 0x46, 0x3A, 0xB5, 0x7E, 0x26, 0xCD, 0xF8, 
            0xFE, 0x7F, 0x53, 0xA3, 0x6D, 0xE4, 0xDD, 0x56, 0xAD, 0x76, 0xB2, 0x97, 0x69, 0xD9, 0x74, 0xB5, 0xDE, 0xA0, 0x0B, 0x6B, 
            0xF6, 0x29, 0x36, 0x9E, 0xC6, 0xBC, 0x85, 0xC8, 0xCA, 0xC7, 0x3E, 0xDE, 0x3A, 0x2A, 0xBB, 0x90, 0x67, 0x71, 0xFF, 0x3B, 
            0x3C, 0x32, 0x78, 0x7C, 0x6D, 0x1A, 0xCD, 0x91, 0xA7, 0xD7, 0xE9, 0x09, 0xE3, 0xD9, 0x47, 0x32, 0x36, 0xC4, 0x60, 0x83, 
            0xC0, 0x95, 0x6E, 0x02, 0x6C, 0x12, 0xA1, 0xE9, 0xE0, 0xC6, 0x54, 0x35, 0xA0, 0xBB, 0x9B, 0x74, 0xB5, 0x25, 0xD1, 0xB3, 
            0x8E, 0xA6, 0x5B, 0x13, 0xCF, 0x18, 0xC0, 0x44, 0xD0, 0xF2, 0xF0, 0x3C, 0x38, 0x6D, 0xE6, 0xAB, 0x56, 0x36, 0x01, 0x5E, 
            0x81, 0x55, 0x17, 0xAC, 0xBD, 0x4A, 0xB5, 0x9D, 0x3E, 0x6E, 0x1D, 0x1E, 0x8F, 0x93, 0x9E, 0xF9, 0xA3, 0x13, 0xB7, 0x3E, 
            0x18, 0xF9, 0xFD, 0x65, 0x66, 0xCB, 0x36, 0xA8, 0xFA, 0x0E, 0x76, 0x1C, 0x59, 0x2E, 0xBB, 0x46, 0x3D, 0xEE, 0x9D, 0x9A, 
            0xCE, 0x86, 0xB1, 0xDA, 0x47, 0xF7, 0x3E, 0xFC, 0x0C, 0xB5, 0xA1, 0x7D, 0xEE, 0xC0, 0x66, 0x2A, 0x24, 0xDF, 0x46, 0x97, 
            0xAD, 0xCE, 0xA4, 0xB6, 0x76, 0xE5, 0xE9, 0xC6, 0xBC, 0xDB, 0x33, 0xDE, 0x00, 0xE1, 0xE7, 0xCD, 0xC9, 0xEB, 0x54, 0xF0, 
            0xAB, 0xFB, 0x21, 0xE5, 0xDE, 0xE2, 0xD7, 0xA5, 0xDD, 0x39, 0x0F, 0x37, 0x87, 0x43, 0xE5, 0xCF, 0x21, 0x3B, 0x37, 0x5F, 
            0x6E, 0xD2, 0x5F, 0xBA, 0x85, 0xDB, 0x3C, 0x59, 0x60, 0x01, 0xAE, 0x1B, 0x5B, 0xB5, 0xF4, 0x65, 0x0F, 0xF1, 0xBA, 0xA2, 
            0x7E, 0x6C, 0x19, 0x07, 0xE5, 0x46, 0xBD, 0xCE, 0x34, 0xA3, 0x8E, 0x88, 0x6B, 0x65, 0x60, 0x73, 0xA5, 0xF6, 0x5B, 0x5F, 
            0x29, 0x74, 0x06, 0x99, 0x43, 0x81, 0x18, 0x55, 0xAB, 0x1B, 0xE7, 0xF2, 0xB6, 0xD8, 0x55, 0x73, 0xA0, 0xAB, 0x89, 0xB6, 
            0x82, 0x11, 0x29, 0x3F, 0x00, 0xEA, 0xFC, 0x13, 0x62, 0xBD, 0xD7, 0x89, 0x41, 0xAF, 0x9A, 0xAB, 0xB4, 0x79, 0xEA, 0x1C, 
            0x7D, 0xDB, 0xDB, 0xD6, 0x5F, 0xEF, 0xAA, 0xD3, 0xF1, 0xAE, 0x9C, 0x47, 0x0C, 0xCC, 0x19, 0x1A, 0x67, 0xEB, 0x9B, 0x61, 
            0xC1, 0xEE, 0x53, 0x20, 0x1F, 0x1C, 0x66, 0xC4, 0x1E, 0x43, 0x70, 0x3D, 0xB6, 0x56, 0x07, 0xCD, 0x01, 0xF7, 0x4B, 0x7F, 
            0xF6, 0x31, 0xAE, 0xB4, 0x3C, 0xE0, 0x7B, 0x12, 0x5B, 0x3C, 0x53, 0xA5, 0x65, 0x40, 0x76, 0x90, 0x49, 0xB2, 0xC7, 0x23, 
            0x38, 0xDE, 0x61, 0x16, 0x91, 0x58, 0xBB, 0x45, 0x27, 0xAC, 0x5F, 0x48, 0x18, 0x46, 0xF5, 0xEA, 0xDB, 0xDA, 0x0B, 0xD5, 
            0x82, 0xD1, 0x4D, 0x4D, 0x4D, 0x83, 0xF5, 0x63, 0x5D, 0x34, 0x7D, 0xB2, 0x4D, 0xF1, 0x34, 0x75, 0x26, 0xEB, 0xCD, 0x48, 
            0xB9, 0x8C, 0x70, 0xA4, 0x16, 0x0F, 0x73, 0x9D, 0x27, 0x2D, 0x3B, 0x9A, 0xFC, 0xD3, 0xFD, 0xCB, 0xA7, 0x70, 0x57, 0x17, 
            0x48, 0x47, 0x21, 0xF8, 0xFE, 0x6D, 0xAF, 0x2F, 0x5E, 0x2A, 0xCD, 0x90, 0x63, 0x60, 0x50, 0xB9, 0xDA, 0x9B, 0x1A, 0x63, 
            0x32, 0xD5, 0xB7, 0x65, 0x5D, 0x10, 0xBB, 0x2A, 0xAE, 0x1B, 0xA1, 0x76, 0x19, 0xF7, 0x0D, 0xD8, 0xBF, 0x97, 0x01, 0x9D, 
            0xB7, 0xA5, 0x3B, 0x7F, 0x02, 0x7F, 0xE1, 0xBF, 0x7C, 0xFA, 0x6B, 0xE2, 0x78, 0x09, 0x67, 0x07, 0xEA, 0xE2, 0x09, 0x9D, 
            0xBD, 0xD3, 0x94, 0x17, 0xA4, 0x53, 0x69, 0x59, 0x53, 0x14, 0xDF, 0x36, 0xE1, 0xA1, 0xC0, 0x52, 0xF6, 0x50, 0x92, 0x17, 
            0x1D, 0x2D, 0x72, 0xF1, 0x8A, 0x48, 0x9B, 0xB4, 0x62, 0x2C, 0xB7, 0xA4, 0x1F, 0x16, 0x0D, 0xF3, 0x62, 0xCC, 0x52, 0x8D, 
            0x33, 0x63, 0xDC, 0x60, 0x37, 0xE9, 0xFF, 0xA7, 0xAC, 0xBE, 0xCE, 0x6A, 0x29, 0xFB, 0xF0, 0x34, 0xD8, 0x40, 0x75, 0xC7, 
            0xEE, 0x5F, 0xDD, 0x24, 0xCC, 0xC5, 0x51, 0x24, 0x00, 0x00, 
        };

        private static string GetStaticCss()
        {
            if (_cachedStaticCss != null) return _cachedStaticCss;
            lock (_cssLock)
            {
                if (_cachedStaticCss == null)
                {
                    _cachedStaticCss = CompressedStringHelper.Decompress(_compressedCssBytes);
                }
                return _cachedStaticCss;
            }
        }

        public static bool IsChromiumInternalErrorPage(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.StartsWith("chrome-error://", StringComparison.OrdinalIgnoreCase) ||
                   url.IndexOf("chromewebdata", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsErrorPage(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.IndexOf("weberror_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("strictblock_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.StartsWith("wb-error:", StringComparison.OrdinalIgnoreCase) ||
                   IsChromiumInternalErrorPage(url);
        }

        public static Bitmap GetWarningFavicon(int size = 16)
        {
            return MaterialIcons.GetIcon("warning", size, MaterialColors.StatusWarning)
                ?? MaterialIcons.GetIcon("warning-circle", size, MaterialColors.StatusWarning);
        }

        public static Bitmap GetStopFavicon(int size = 16)
        {
            return MaterialIcons.GetIcon("stop", size, MaterialColors.StatusDanger)
                ?? MaterialIcons.GetIcon("x-circle", size, MaterialColors.StatusDanger);
        }

        public static Bitmap GetFavicon(string themeVariant, int size = 16)
        {
            if (string.Equals(themeVariant, "warning", StringComparison.OrdinalIgnoreCase))
            {
                return GetWarningFavicon(size);
            }
            return GetStopFavicon(size);
        }

        public static Bitmap GetFavicon(WebErrorKind kind, int size = 16)
        {
            if (kind == WebErrorKind.InsecureWarning)
            {
                return GetWarningFavicon(size);
            }
            return GetStopFavicon(size);
        }

        public static Bitmap GetFavicon(UnifiedErrorModel model, int size = 16)
        {
            if (model == null) return GetStopFavicon(size);
            return GetFavicon(model.ThemeVariant, size);
        }

        public static string GetFaviconDataUri(Image img)
        {
            if (img == null) return string.Empty;
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    img.Save(ms, ImageFormat.Png);
                    return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string CreateErrorFile(WebErrorInfo info)
        {
            if (info == null) info = new WebErrorInfo();
            string prefix = (info.Kind == WebErrorKind.InsecureWarning) ? "weberror_warning" : "weberror_stop";
            return StorageHelper.CreateTempHtmlFile(Path.Combine("cache", "errors"), prefix, GenerateHtml(info));
        }

        public static WebErrorInfo CreateForSslError(string targetUrl, string errorCodeName = null, string certDetails = null)
        {
            string domain = ExtractHost(targetUrl);
            return new WebErrorInfo
            {
                Kind = WebErrorKind.InsecureWarning,
                TargetUrl = targetUrl ?? "wb://newtab",
                Domain = domain,
                Title = AppStrings.WebError.SslTitle,
                Subtitle = string.Format(AppStrings.WebError.SslSubtitleFormat, domain),
                TechnicalCode = !string.IsNullOrEmpty(errorCodeName) ? errorCodeName : "NET::ERR_CERT_COMMON_NAME_INVALID",
                TechnicalDetails = certDetails ?? AppStrings.WebError.SslDetailsDefault,
                Description = string.Format(AppStrings.WebError.SslDescriptionFormat, domain)
            };
        }

        public static WebErrorInfo CreateFromCefError(string errorCodeName, int errorCode, string failedUrl, string errorText = null)
        {
            string domain = ExtractHost(failedUrl);
            WebErrorKind kind = WebErrorKind.Generic;
            string title = AppStrings.WebError.GenericTitle;
            string subtitle = string.Format(AppStrings.WebError.GenericSubtitleFormat, domain);
            string description = AppStrings.WebError.GenericDescription;

            string code = !string.IsNullOrEmpty(errorCodeName) ? errorCodeName : ("ERR_" + errorCode);

            // CefErrorCode mapping
            if (errorCode == -105 || 
                errorCode == -137 ||
                code.IndexOf("NameNotResolved", StringComparison.OrdinalIgnoreCase) >= 0 ||
                code.IndexOf("NameResolutionFailed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                code.IndexOf("NXDOMAIN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                code.IndexOf("DNS_PROBE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (errorText != null && (errorText.IndexOf("NXDOMAIN", StringComparison.OrdinalIgnoreCase) >= 0 || errorText.IndexOf("DNS_PROBE", StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                kind = WebErrorKind.NotFound;
                title = AppStrings.WebError.DnsTitle;
                subtitle = string.Format(AppStrings.WebError.DnsSubtitleFormat, domain);
                description = AppStrings.WebError.DnsDescription;
                if (code.IndexOf("NXDOMAIN", StringComparison.OrdinalIgnoreCase) >= 0 || (errorText != null && errorText.IndexOf("NXDOMAIN", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    code = "DNS_PROBE_FINISHED_NXDOMAIN";
                }
                else if (code.IndexOf("DNS_PROBE_FINISHED_BAD_CONFIG", StringComparison.OrdinalIgnoreCase) >= 0 || (errorText != null && errorText.IndexOf("DNS_PROBE_FINISHED_BAD_CONFIG", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    code = "DNS_PROBE_FINISHED_BAD_CONFIG";
                }
                else
                {
                    code = "ERR_NAME_NOT_RESOLVED";
                }
            }
            else if (errorCode == -106 || 
                     code.IndexOf("InternetDisconnected", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     code.IndexOf("DNS_PROBE_FINISHED_NO_INTERNET", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (errorText != null && errorText.IndexOf("DNS_PROBE_FINISHED_NO_INTERNET", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                kind = WebErrorKind.InternetOffline;
                title = AppStrings.WebError.OfflineTitle;
                subtitle = AppStrings.WebError.OfflineSubtitle;
                description = AppStrings.WebError.OfflineDescription;
                code = (code.IndexOf("DNS_PROBE", StringComparison.OrdinalIgnoreCase) >= 0 || (errorText != null && errorText.IndexOf("DNS_PROBE", StringComparison.OrdinalIgnoreCase) >= 0))
                    ? "DNS_PROBE_FINISHED_NO_INTERNET"
                    : "ERR_INTERNET_DISCONNECTED";
            }
            else if (errorCode == -102 || code.IndexOf("ConnectionRefused", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = WebErrorKind.ConnectionRefused;
                title = AppStrings.WebError.ConnectionRefusedTitle;
                subtitle = string.Format(AppStrings.WebError.ConnectionRefusedSubtitleFormat, domain);
                description = AppStrings.WebError.ConnectionRefusedDescription;
                code = "ERR_CONNECTION_REFUSED";
            }
            else if (errorCode == -7 || errorCode == -118 || code.IndexOf("TimedOut", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = WebErrorKind.TimedOut;
                title = AppStrings.WebError.TimedOutTitle;
                subtitle = string.Format(AppStrings.WebError.TimedOutSubtitleFormat, domain);
                description = AppStrings.WebError.TimedOutDescription;
                code = "ERR_CONNECTION_TIMED_OUT";
            }
            else if (errorCode == -109 || code.IndexOf("AddressUnreachable", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = WebErrorKind.NotFound;
                title = AppStrings.WebError.AddressUnreachableTitle;
                subtitle = string.Format(AppStrings.WebError.AddressUnreachableSubtitleFormat, domain);
                description = AppStrings.WebError.AddressUnreachableDescription;
                code = "ERR_ADDRESS_UNREACHABLE";
            }
            else if (errorCode >= -299 && errorCode <= -200)
            {
                return CreateForSslError(failedUrl, code, errorText);
            }

            return new WebErrorInfo
            {
                Kind = kind,
                TargetUrl = failedUrl ?? "wb://newtab",
                Domain = domain,
                Title = title,
                Subtitle = subtitle,
                TechnicalCode = code,
                Description = description,
                TechnicalDetails = null
            };
        }

        public static string GenerateHtml(WebErrorInfo info)
        {
            if (info == null) info = new WebErrorInfo();

            var model = new UnifiedErrorModel
            {
                PageTitle = info.Title ?? AppStrings.WebError.DefaultPageTitle,
                Title = info.Title ?? AppStrings.WebError.DefaultPageTitle,
                Subtitle = info.Subtitle ?? "",
                TargetUrl = info.TargetUrl ?? "wb://newtab",
                Domain = info.Domain,
                Description = info.Description ?? "",
                ErrorCode = info.TechnicalCode ?? "ERR_UNKNOWN",
                AdvancedDetails = null
            };

            if (info.Kind == WebErrorKind.InsecureWarning)
            {
                model.ThemeVariant = "warning";
                model.BadgeIconSvg = PhosphorErrorIcons.Lock;
                model.AdvancedDetails = info.TechnicalDetails;
                model.Actions.Add(new ActionButton { Text = AppStrings.WebError.BtnGoBackSafe, Href = "wb-action:goback", IsPrimary = true });
                model.Actions.Add(new ActionButton { Text = AppStrings.WebError.BtnCloseTab, Href = "wb-action:closetab", IsPrimary = false });
                model.Actions.Add(new ActionButton { Text = AppStrings.WebError.BtnGoHome, Href = "wb-action:home", IsPrimary = false });

                string safeDomain = !string.IsNullOrEmpty(info.Domain) ? info.Domain : AppStrings.WebError.ThisSite;
                string encodedUrlQuery = WebUtility.UrlEncode(info.TargetUrl ?? "");
                model.DangerBypassUrl = "wb-action:proceed-ssl?url=" + encodedUrlQuery;
                model.DangerBypassText = string.Format(AppStrings.WebError.ProceedUnsafeFormat, safeDomain);
            }
            else
            {
                model.AdvancedDetails = null;
                model.DangerBypassUrl = null;
                model.DangerBypassText = null;
                model.ThemeVariant = "accent";
                if (info.Kind == WebErrorKind.InternetOffline)
                {
                    model.BadgeIconSvg = PhosphorErrorIcons.WifiSlash;
                }
                else if (info.Kind == WebErrorKind.ConnectionRefused)
                {
                    model.BadgeIconSvg = PhosphorErrorIcons.Plugs;
                }
                else if (info.Kind == WebErrorKind.TimedOut)
                {
                    model.BadgeIconSvg = PhosphorErrorIcons.Clock;
                }
                else
                {
                    model.BadgeIconSvg = PhosphorErrorIcons.WarningCircle;
                }

                model.Actions.Add(new ActionButton { Text = AppStrings.WebError.BtnTryAgain, Href = "wb-action:reload", IsPrimary = true });
                model.Actions.Add(new ActionButton { Text = AppStrings.WebError.BtnGoBack, Href = "wb-action:goback", IsPrimary = false });
                model.Actions.Add(new ActionButton { Text = AppStrings.WebError.BtnGoHome, Href = "wb-action:home", IsPrimary = false });
            }

            return GenerateUnifiedHtml(model);
        }

        public static string GenerateUnifiedHtml(UnifiedErrorModel model)
        {
            if (model == null) model = new UnifiedErrorModel();

            string pageTitleText = !string.IsNullOrEmpty(model.PageTitle) ? model.PageTitle : (model.Title ?? AppStrings.WebError.DefaultPageTitle);
            if (pageTitleText.EndsWith(" - wb", StringComparison.OrdinalIgnoreCase))
            {
                pageTitleText = pageTitleText.Substring(0, pageTitleText.Length - 5).TrimEnd();
            }
            else if (pageTitleText.EndsWith(" - wb Browser", StringComparison.OrdinalIgnoreCase))
            {
                pageTitleText = pageTitleText.Substring(0, pageTitleText.Length - 13).TrimEnd();
            }
            string safePageTitle = WebUtility.HtmlEncode(pageTitleText);
            string safeTitle = WebUtility.HtmlEncode(model.Title ?? AppStrings.WebError.DefaultPageTitle);
            string safeSubtitle = WebUtility.HtmlEncode(model.Subtitle ?? "");
            string safeDescription = WebUtility.HtmlEncode(model.Description ?? "");
            string safeCode = WebUtility.HtmlEncode(model.ErrorCode ?? "");
            string safeMatchedRule = WebUtility.HtmlEncode(model.MatchedRule ?? "");
            string safeFilter = WebUtility.HtmlEncode(model.FilterSource ?? "");
            string rawUrl = model.TargetUrl ?? "wb://newtab";
            string safeUrl = WebUtility.HtmlEncode(rawUrl);
            string safeRedirectUrl = WebUtility.HtmlEncode(model.RedirectUrl ?? "");
            string safeAdvDetails = WebUtility.HtmlEncode(model.AdvancedDetails ?? "");

            // Dynamic accent color from ThemeManager
            Color accent = ThemeManager.CurrentAccentColor;
            if (accent.IsEmpty || (accent.R == 0 && accent.G == 0 && accent.B == 0))
            {
                accent = Color.FromArgb(138, 180, 248);
            }
            string accentHex = string.Format("#{0:X2}{1:X2}{2:X2}", accent.R, accent.G, accent.B);
            string accentSubtle = string.Format("rgba({0}, {1}, {2}, 0.12)", accent.R, accent.G, accent.B);
            string accentGlow = string.Format("rgba({0}, {1}, {2}, 0.25)", accent.R, accent.G, accent.B);
            string accentBorder = string.Format("rgba({0}, {1}, {2}, 0.35)", accent.R, accent.G, accent.B);

            string badgeColor = accentHex;
            string badgeBg = accentSubtle;
            string badgeBorder = accentBorder;
            string primaryBtnBg = accentHex;
            Color contrastCol = UITheme.GetContrastTextColor(accent);
            string primaryBtnText = string.Format("#{0:X2}{1:X2}{2:X2}", contrastCol.R, contrastCol.G, contrastCol.B);

            if (string.Equals(model.ThemeVariant, "warning", StringComparison.OrdinalIgnoreCase))
            {
                badgeColor = "#fdd663";
                badgeBg = "rgba(253, 214, 99, 0.12)";
                badgeBorder = "rgba(253, 214, 99, 0.35)";
            }
            else if (string.Equals(model.ThemeVariant, "danger", StringComparison.OrdinalIgnoreCase))
            {
                badgeColor = "#f28b82";
                badgeBg = "rgba(242, 139, 130, 0.12)";
                badgeBorder = "rgba(242, 139, 130, 0.35)";
            }

            string iconSvg = !string.IsNullOrEmpty(model.BadgeIconSvg) ? model.BadgeIconSvg : PhosphorErrorIcons.WarningCircle;
            Image favIcon = GetFavicon(model);
            string favDataUri = GetFaviconDataUri(favIcon);

            StringBuilder sb = new StringBuilder();
            sb.Append(@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>").Append(safePageTitle).Append(@"</title>");
            if (!string.IsNullOrEmpty(favDataUri))
            {
                sb.Append(@"
    <link rel=""icon"" type=""image/png"" href=""").Append(favDataUri).Append(@""">");
            }
            sb.Append(@"
    <style>
        :root {
            /* wb Standard Design Tokens */
            --bg-color: #202124;
            --card-bg: #292a2d;
            --card-border: #3c4043;
            --card-inner-bg: #202124;
            --text-primary: #e8eaed;
            --text-secondary: #9aa0a6;
            --text-muted: #5f6368;
            --btn-hover: #383a3e;
            --btn-active: #46484d;
            --accent: ").Append(accentHex).Append(@";
            --accent-subtle: ").Append(accentSubtle).Append(@";
            --accent-glow: ").Append(accentGlow).Append(@";
            
            /* Category Accent Tokens */
            --badge-color: ").Append(badgeColor).Append(@";
            --badge-bg: ").Append(badgeBg).Append(@";
            --badge-border: ").Append(badgeBorder).Append(@";
            --primary-btn-bg: ").Append(primaryBtnBg).Append(@";
            --primary-btn-text: ").Append(primaryBtnText).Append(@";
        }");
            sb.Append(GetStaticCss());
            sb.Append(@"
    </style>
</head>
<body>
<div class=""container"">
    <div class=""header-row"">
        <div class=""header-icon"">
            ").Append(iconSvg).Append(@"
        </div>
        <div class=""title-group"">
            <h1>").Append(safeTitle).Append(@"</h1>");
            if (!string.IsNullOrEmpty(safeSubtitle))
            {
                sb.Append(@"
            <div class=""subtitle"">").Append(safeSubtitle).Append(@"</div>");
            }
            sb.Append(@"
        </div>
    </div>");

            // URL Box
            sb.Append(@"
    <div class=""url-box"">
        <div class=""url-text"" id=""destUrl"">");
            if (!string.IsNullOrEmpty(model.FormattedUrlHtml))
            {
                sb.Append(model.FormattedUrlHtml);
            }
            else
            {
                sb.Append(safeUrl);
            }
            sb.Append(@"</div>
        <button class=""copy-btn"" onclick=""copyUrl()"" id=""copyBtn"">").Append(WebUtility.HtmlEncode(AppStrings.General.BtnCopyUrl)).Append(@"</button>
    </div>");

            // Info card (description, filter source, code pill)
            bool hasDesc = !string.IsNullOrEmpty(safeDescription);
            bool hasFilter = !string.IsNullOrEmpty(safeFilter);
            bool hasRule = !string.IsNullOrEmpty(safeMatchedRule);
            bool hasCode = !string.IsNullOrEmpty(safeCode);

            if (hasDesc || hasFilter || hasRule || hasCode)
            {
                sb.Append(@"
    <div class=""info-card"">");
                if (hasDesc)
                {
                    sb.Append(@"
        <p>").Append(safeDescription).Append(@"</p>");
                }
                if (hasFilter)
                {
                    sb.Append(@"
        <div class=""meta-row"">
            <span class=""meta-label"">").Append(WebUtility.HtmlEncode(AppStrings.WebError.MetaBlockedBy)).Append(@"</span>
            <span class=""filter-pill"">").Append(safeFilter).Append(@"</span>
        </div>");
                }
                if (hasRule)
                {
                    sb.Append(@"
        <div class=""code-pill"">").Append(safeMatchedRule).Append(@"</div>");
                }
                else if (hasCode)
                {
                    sb.Append(@"
        <div class=""code-pill"">").Append(safeCode).Append(@"</div>");
                }
                sb.Append(@"
    </div>");
            }

            // Redirect box (for strict block unwrapping)
            if (!string.IsNullOrEmpty(safeRedirectUrl))
            {
                sb.Append(@"
    <div class=""redirect-box"">
        <span class=""redirect-icon"">&#10140;</span>
        <span class=""redirect-text"">").Append(WebUtility.HtmlEncode(AppStrings.WebError.RedirectNotice)).Append(@"</span>
        <a href=""").Append(safeRedirectUrl).Append(@""" class=""redirect-link"">").Append(safeRedirectUrl).Append(@"</a>
    </div>");
            }

            // Checkbox (e.g. "Don't warn me again about this site")
            if (model.ShowPermanentWhitelistCheckbox)
            {
                string cbText = !string.IsNullOrEmpty(model.WhitelistCheckboxText) ? model.WhitelistCheckboxText : AppStrings.WebError.DefaultWhitelistCheckboxText;
                sb.Append(@"
    <div class=""checkbox-row"">
        <label class=""custom-checkbox-label"">
            <input type=""checkbox"" id=""disableWarning"" onchange=""onToggleWarning(this)"">
            <span class=""checkbox-text"">").Append(cbText).Append(@"</span>
        </label>
    </div>");
            }

            // Actions
            if (model.Actions != null && model.Actions.Count > 0)
            {
                sb.Append(@"
    <div class=""actions-row"">");
                foreach (var act in model.Actions)
                {
                    string btnClass = act.IsPrimary ? "btn btn-primary" : "btn btn-secondary";
                    string idAttr = !string.IsNullOrEmpty(act.Id) ? string.Format(@" id=""{0}""", act.Id) : "";
                    string hrefAttr = !string.IsNullOrEmpty(act.Href) ? string.Format(@" href=""{0}""", act.Href) : "";
                    string onclickAttr = !string.IsNullOrEmpty(act.OnClick) ? string.Format(@" onclick=""{0}""", act.OnClick) : "";
                    string safeText = WebUtility.HtmlEncode(act.Text ?? "");

                    if (!string.IsNullOrEmpty(act.OnClick))
                    {
                        sb.AppendFormat(@"
        <button{0}{1} class=""{2}"">{3}</button>", idAttr, onclickAttr, btnClass, safeText);
                    }
                    else
                    {
                        sb.AppendFormat(@"
        <a{0}{1} class=""{2}"">{3}</a>", idAttr, hrefAttr, btnClass, safeText);
                    }
                }
                sb.Append(@"
    </div>");
            }

            // Advanced section (only show when there is meaningful information to be shown, e.g. certificate details or dangerous bypass)
            bool hasAdvDetails = !string.IsNullOrWhiteSpace(safeAdvDetails) && 
                                 !string.Equals(safeAdvDetails.Trim(), safeCode.Trim(), StringComparison.OrdinalIgnoreCase);
            bool hasBypass = !string.IsNullOrWhiteSpace(model.DangerBypassUrl);
            bool hasAdv = hasAdvDetails || hasBypass;
            if (hasAdv)
            {
                sb.Append(@"
    <div class=""advanced-section"">
        <button class=""advanced-toggle"" onclick=""toggleAdvanced()"">
            <span id=""toggleArrow"">&#9656;</span>
            <span>").Append(WebUtility.HtmlEncode(AppStrings.WebError.AdvancedToggle)).Append(@"</span>
        </button>
        <div class=""advanced-content"" id=""advContent"">");
                if (hasAdvDetails)
                {
                    sb.Append(@"
            <p>").Append(safeAdvDetails).Append(@"</p>");
                }
                if (hasBypass)
                {
                    string safeBypassText = WebUtility.HtmlEncode(!string.IsNullOrEmpty(model.DangerBypassText) ? model.DangerBypassText : AppStrings.WebError.ProceedUnsafeFallback);
                    sb.Append(@"
            <div class=""advanced-bypass"">
                <a href=""").Append(model.DangerBypassUrl).Append(@""" class=""danger-bypass-link"">").Append(safeBypassText).Append(@"</a>
            </div>");
                }
                sb.Append(@"
        </div>
    </div>");
            }

            sb.Append(@"
</div>

<script>
    function copyUrl() {
        var text = document.getElementById('destUrl').innerText;
        if (navigator.clipboard) {
            navigator.clipboard.writeText(text).then(function() {
                var btn = document.getElementById('copyBtn');
                btn.innerText = '").Append(AppStrings.WebError.BtnCopied).Append(@"';
                setTimeout(function() { btn.innerText = '").Append(AppStrings.General.BtnCopyUrl).Append(@"'; }, 2000);
            });
        }
    }
    function toggleAdvanced() {
        var el = document.getElementById('advContent');
        var arr = document.getElementById('toggleArrow');
        if (el.classList.contains('open')) {
            el.classList.remove('open');
            arr.innerHTML = '&#9656;';
        } else {
            el.classList.add('open');
            arr.innerHTML = '&#9662;';
        }
    }");

            if (!string.IsNullOrEmpty(model.CustomScript))
            {
                sb.Append("\n").Append(model.CustomScript);
            }

            sb.Append(@"
</script>
</body>
</html>");

            return sb.ToString();
        }


        private static string ExtractHost(string url)
        {
            if (string.IsNullOrEmpty(url)) return AppStrings.WebError.UnknownHost;
            string host = UrlHelper.ExtractDomain(url);
            return !string.IsNullOrEmpty(host) ? host : url;
        }

        private static string GetLockWarningSvg()
        {
            return PhosphorErrorIcons.Lock;
        }

        private static string GetOfflineSvg()
        {
            return PhosphorErrorIcons.WifiSlash;
        }

        private static string GetDefaultNetworkSvg()
        {
            return PhosphorErrorIcons.WarningCircle;
        }
    }
}
