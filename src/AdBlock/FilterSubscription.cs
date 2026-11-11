using System;
using System.Collections.Generic;

namespace wb.AdBlock
{
    /// <summary>
    /// Represents an updateable filter list subscription (e.g. EasyList, Peter Lowe, uBlock filters).
    /// Supports primary content URLs and multiple CDN fallback mirrors like uBlock Origin.
    /// </summary>
    public class FilterSubscription
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public List<string> FallbackUrls { get; set; }
        public string Group { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsCustom { get; set; }
        public string LocalFileName { get; set; }
        public DateTime LastUpdatedUtc { get; set; }
        public int RuleCount { get; set; }
        public string LastStatus { get; set; }
        public string ETag { get; set; }
        public string LastModified { get; set; }

        public FilterSubscription()
        {
            Id = string.Empty;
            Name = string.Empty;
            Url = string.Empty;
            FallbackUrls = new List<string>();
            Group = "General";
            IsEnabled = true;
            IsCustom = false;
            LocalFileName = string.Empty;
            LastUpdatedUtc = DateTime.MinValue;
            RuleCount = 0;
            LastStatus = "Ready";
            ETag = string.Empty;
            LastModified = string.Empty;
        }

        public FilterSubscription(string id, string name, string url, string group, string localFileName, bool isCustom, string[] fallbackUrls = null)
        {
            Id = id;
            Name = name;
            Url = url;
            FallbackUrls = fallbackUrls != null ? new List<string>(fallbackUrls) : new List<string>();
            Group = group;
            IsEnabled = true;
            IsCustom = isCustom;
            LocalFileName = localFileName;
            LastUpdatedUtc = DateTime.MinValue;
            RuleCount = 0;
            LastStatus = "Ready";
            ETag = string.Empty;
            LastModified = string.Empty;
        }

        public List<string> GetCandidateUrls()
        {
            List<string> list = new List<string>();
            if (!string.IsNullOrEmpty(Url))
            {
                list.Add(Url);
            }
            if (FallbackUrls != null)
            {
                for (int i = 0; i < FallbackUrls.Count; i++)
                {
                    string f = FallbackUrls[i];
                    if (!string.IsNullOrEmpty(f) && !list.Contains(f))
                    {
                        list.Add(f);
                    }
                }
            }
            return list;
        }
    }
}
