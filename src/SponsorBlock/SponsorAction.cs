using System;
using wb.Core;

namespace wb.SponsorBlock
{
    /// <summary>
    /// Specifies the action to take when a sponsored segment is encountered during playback.
    /// Matches official SponsorBlock action behaviors.
    /// </summary>
    public enum SponsorAction
    {
        /// <summary>
        /// Automatically skip the segment with zero user interaction.
        /// </summary>
        AutoSkip = 0,

        /// <summary>
        /// Show an interactive on-screen prompt allowing the user to skip manually.
        /// </summary>
        ManualSkip = 1,

        /// <summary>
        /// Automatically mute audio during the segment duration.
        /// </summary>
        Mute = 2,

        /// <summary>
        /// Ignore the segment entirely.
        /// </summary>
        Disabled = 3
    }

    /// <summary>
    /// Helper methods for converting between SponsorAction enum and API/UI representations.
    /// </summary>
    public static class SponsorActionHelper
    {
        public static string ToFriendlyName(SponsorAction action)
        {
            switch (action)
            {
                case SponsorAction.AutoSkip:
                    return AppStrings.SponsorBlock.ActionAutoSkip;
                case SponsorAction.ManualSkip:
                    return AppStrings.SponsorBlock.ActionManualSkip;
                case SponsorAction.Mute:
                    return AppStrings.SponsorBlock.ActionMute;
                case SponsorAction.Disabled:
                    return AppStrings.SponsorBlock.ActionDisabled;
                default:
                    return action.ToString();
            }
        }

        public static string ToConfigString(SponsorAction action)
        {
            switch (action)
            {
                case SponsorAction.AutoSkip:
                    return "skip";
                case SponsorAction.ManualSkip:
                    return "manual";
                case SponsorAction.Mute:
                    return "mute";
                case SponsorAction.Disabled:
                    return "disabled";
                default:
                    return "skip";
            }
        }

        public static SponsorAction FromConfigString(string value)
        {
            if (string.IsNullOrEmpty(value)) return SponsorAction.AutoSkip;
            string lower = value.Trim().ToLowerInvariant();
            switch (lower)
            {
                case "skip":
                case "autoskip":
                case "auto":
                case "0":
                    return SponsorAction.AutoSkip;
                case "manual":
                case "manualskip":
                case "prompt":
                case "1":
                    return SponsorAction.ManualSkip;
                case "mute":
                case "2":
                    return SponsorAction.Mute;
                case "disabled":
                case "disable":
                case "off":
                case "3":
                    return SponsorAction.Disabled;
                default:
                    return SponsorAction.AutoSkip;
            }
        }
    }
}
