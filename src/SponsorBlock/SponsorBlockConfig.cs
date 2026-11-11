using System;
using System.Collections.Generic;

namespace wb.SponsorBlock
{
    /// <summary>
    /// User preferences and category configurations for SponsorBlock.
    /// Serialized to disk using StorageHelper.
    /// </summary>
    public class SponsorBlockConfig
    {
        public bool IsEnabled { get; set; }
        public bool ShowPreviewBar { get; set; }
        public bool ShowNotice { get; set; }
        public int NoticeDurationSeconds { get; set; }
        public bool AudioNotification { get; set; }
        public double MinDuration { get; set; }
        public Dictionary<string, string> CategoryActions { get; set; }
        public List<string> WhitelistedChannels { get; set; }
        public List<string> WhitelistedVideoIds { get; set; }

        public SponsorBlockConfig()
        {
            IsEnabled = true;
            ShowPreviewBar = true;
            ShowNotice = true;
            NoticeDurationSeconds = 4;
            AudioNotification = false;
            MinDuration = 0.0;
            CategoryActions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            WhitelistedChannels = new List<string>();
            WhitelistedVideoIds = new List<string>();

            // Set default actions for all known categories
            foreach (var cat in SponsorCategory.All)
            {
                CategoryActions[cat.Key] = SponsorActionHelper.ToConfigString(cat.DefaultAction);
            }
        }

        public SponsorAction GetAction(string category)
        {
            if (string.IsNullOrEmpty(category)) return SponsorAction.AutoSkip;
            string val;
            if (CategoryActions != null)
            {
                if (CategoryActions.TryGetValue(category, out val))
                {
                    return SponsorActionHelper.FromConfigString(val);
                }
                string norm = SponsorCategory.NormalizeKey(category);
                if (CategoryActions.TryGetValue(norm, out val))
                {
                    return SponsorActionHelper.FromConfigString(val);
                }
            }
            return SponsorCategory.GetDefaultAction(category);
        }

        public void SetAction(string category, SponsorAction action)
        {
            if (string.IsNullOrEmpty(category)) return;
            if (CategoryActions == null) CategoryActions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string norm = SponsorCategory.NormalizeKey(category);
            CategoryActions[norm] = SponsorActionHelper.ToConfigString(action);
        }

        public List<string> GetWhitelistedVideos()
        {
            if (WhitelistedVideoIds == null) return new List<string>();
            return new List<string>(WhitelistedVideoIds);
        }

        public bool AddVideoWhitelist(string videoIdOrUrl)
        {
            if (string.IsNullOrEmpty(videoIdOrUrl)) return false;
            string vid = SponsorBlockManager.ExtractVideoId(videoIdOrUrl);
            if (string.IsNullOrEmpty(vid)) vid = videoIdOrUrl.Trim();
            if (string.IsNullOrEmpty(vid)) return false;

            if (WhitelistedVideoIds == null) WhitelistedVideoIds = new List<string>();
            if (!WhitelistedVideoIds.Contains(vid))
            {
                WhitelistedVideoIds.Add(vid);
                return true;
            }
            return false;
        }

        public bool RemoveVideoWhitelist(string videoIdOrUrl)
        {
            if (string.IsNullOrEmpty(videoIdOrUrl) || WhitelistedVideoIds == null) return false;
            string vid = SponsorBlockManager.ExtractVideoId(videoIdOrUrl);
            if (string.IsNullOrEmpty(vid)) vid = videoIdOrUrl.Trim();
            return WhitelistedVideoIds.Remove(vid);
        }

        public void ClearVideoWhitelist()
        {
            if (WhitelistedVideoIds != null)
            {
                WhitelistedVideoIds.Clear();
            }
        }

        public bool IsVideoWhitelisted(string videoId)
        {
            if (string.IsNullOrEmpty(videoId) || WhitelistedVideoIds == null) return false;
            string vid = SponsorBlockManager.ExtractVideoId(videoId);
            if (string.IsNullOrEmpty(vid)) vid = videoId.Trim();
            return WhitelistedVideoIds.Contains(vid);
        }

        public bool ToggleVideoWhitelist(string videoId)
        {
            if (string.IsNullOrEmpty(videoId)) return false;
            string vid = SponsorBlockManager.ExtractVideoId(videoId);
            if (string.IsNullOrEmpty(vid)) vid = videoId.Trim();
            if (string.IsNullOrEmpty(vid)) return false;

            if (WhitelistedVideoIds == null) WhitelistedVideoIds = new List<string>();

            if (WhitelistedVideoIds.Contains(vid))
            {
                WhitelistedVideoIds.Remove(vid);
                return false;
            }
            else
            {
                WhitelistedVideoIds.Add(vid);
                return true;
            }
        }

        public bool IsChannelWhitelisted(string channelNameOrId)
        {
            if (string.IsNullOrEmpty(channelNameOrId) || WhitelistedChannels == null) return false;
            return WhitelistedChannels.Contains(channelNameOrId);
        }

        public void ResetToDefaults()
        {
            IsEnabled = true;
            ShowPreviewBar = true;
            ShowNotice = true;
            NoticeDurationSeconds = 4;
            AudioNotification = false;
            MinDuration = 0.0;
            if (CategoryActions == null) CategoryActions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CategoryActions.Clear();
            foreach (var cat in SponsorCategory.All)
            {
                CategoryActions[cat.Key] = SponsorActionHelper.ToConfigString(cat.DefaultAction);
            }
        }
    }
}
