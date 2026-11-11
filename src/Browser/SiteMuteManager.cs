using System;
using System.Collections.Generic;
using System.IO;
using wb.Core;

namespace wb.Browser
{
    /// <summary>
    /// Manages persistent domain-level audio muting across sessions.
    /// When a site is muted, all current and future tabs navigating to that domain are automatically muted.
    /// </summary>
    public class SiteMuteManager
    {
        private const string MutedSitesFileName = "muted_sites.json";
        private readonly HashSet<string> _mutedDomains;
        private readonly object _lock = new object();

        public event EventHandler<string> MuteStateChanged;

        public SiteMuteManager()
        {
            _mutedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Load();
        }

        public int MutedCount
        {
            get
            {
                lock (_lock)
                {
                    return _mutedDomains.Count;
                }
            }
        }

        public bool IsDomainMuted(string domainOrUrl)
        {
            if (string.IsNullOrEmpty(domainOrUrl)) return false;
            string domain = NormalizeDomain(domainOrUrl);
            if (string.IsNullOrEmpty(domain)) return false;

            lock (_lock)
            {
                return _mutedDomains.Contains(domain);
            }
        }

        public void SetDomainMuted(string domainOrUrl, bool muted)
        {
            if (string.IsNullOrEmpty(domainOrUrl)) return;
            string domain = NormalizeDomain(domainOrUrl);
            if (string.IsNullOrEmpty(domain)) return;

            bool changed = false;
            lock (_lock)
            {
                if (muted)
                {
                    changed = _mutedDomains.Add(domain);
                }
                else
                {
                    changed = _mutedDomains.Remove(domain);
                }
            }

            if (changed)
            {
                Save();
                if (MuteStateChanged != null)
                {
                    MuteStateChanged(this, domain);
                }
            }
        }

        public bool ToggleDomainMuted(string domainOrUrl)
        {
            if (string.IsNullOrEmpty(domainOrUrl)) return false;
            string domain = NormalizeDomain(domainOrUrl);
            if (string.IsNullOrEmpty(domain)) return false;

            bool isMuted;
            lock (_lock)
            {
                isMuted = _mutedDomains.Contains(domain);
            }

            SetDomainMuted(domain, !isMuted);
            return !isMuted;
        }

        public List<string> GetMutedDomains()
        {
            lock (_lock)
            {
                return new List<string>(_mutedDomains);
            }
        }

        public string ExtractDomain(string urlOrDomain)
        {
            return NormalizeDomain(urlOrDomain);
        }

        public bool IsSiteMuted(string domainOrUrl)
        {
            return IsDomainMuted(domainOrUrl);
        }

        public void SetSiteMuted(string domainOrUrl, bool muted)
        {
            SetDomainMuted(domainOrUrl, muted);
        }

        public bool ToggleSiteMute(string domainOrUrl)
        {
            return ToggleDomainMuted(domainOrUrl);
        }

        public void Clear()
        {
            lock (_lock)
            {
                _mutedDomains.Clear();
            }
            Save();
        }

        private void Load()
        {
            try
            {
                List<string> list = StorageHelper.LoadList<string>(MutedSitesFileName);
                lock (_lock)
                {
                    _mutedDomains.Clear();
                    if (list != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            string d = NormalizeDomain(list[i]);
                            if (!string.IsNullOrEmpty(d))
                            {
                                _mutedDomains.Add(d);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void Save()
        {
            try
            {
                List<string> list;
                lock (_lock)
                {
                    list = new List<string>(_mutedDomains);
                }
                StorageHelper.SaveList<string>(MutedSitesFileName, list);
            }
            catch
            {
            }
        }

        public static string NormalizeDomain(string urlOrDomain)
        {
            return UrlHelper.NormalizeDomain(urlOrDomain);
        }
    }
}
