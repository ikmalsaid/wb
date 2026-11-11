using System;
using System.Collections.Generic;
using System.Drawing;
using wb.Core;

namespace wb.SponsorBlock
{
    /// <summary>
    /// Metadata definition for a SponsorBlock category.
    /// </summary>
    public class SponsorCategoryInfo
    {
        public string Key { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public string ColorHex { get; private set; }
        public Color Color { get; private set; }
        public SponsorAction DefaultAction { get; private set; }

        public SponsorCategoryInfo(string key, string name, string description, string colorHex, SponsorAction defaultAction)
        {
            Key = key;
            Name = name;
            Description = description;
            ColorHex = colorHex;
            DefaultAction = defaultAction;
            Color = ParseHexColor(colorHex);
        }

        private static Color ParseHexColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.FromArgb(0, 212, 0);
            try
            {
                string clean = hex.Trim().TrimStart('#');
                if (clean.Length == 6)
                {
                    int r = Convert.ToInt32(clean.Substring(0, 2), 16);
                    int g = Convert.ToInt32(clean.Substring(2, 2), 16);
                    int b = Convert.ToInt32(clean.Substring(4, 2), 16);
                    return Color.FromArgb(r, g, b);
                }
            }
            catch
            {
            }
            return Color.FromArgb(0, 212, 0);
        }
    }

    /// <summary>
    /// Supported SponsorBlock categories with official colors, names and default actions.
    /// References official SponsorBlock specification.
    /// </summary>
    public static class SponsorCategory
    {
        public const string Sponsor = "sponsor";
        public const string Intro = "intro";
        public const string Outro = "outro";
        public const string Interaction = "interaction";
        public const string SelfPromo = "selfpromo";
        public const string Preview = "preview";
        public const string Filler = "filler";
        public const string MusicOfftopic = "music_offtopic";
        public const string PoiHighlight = "poi_highlight";

        private static readonly Dictionary<string, SponsorCategoryInfo> CategoriesMap =
            new Dictionary<string, SponsorCategoryInfo>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<SponsorCategoryInfo> CategoriesList = new List<SponsorCategoryInfo>();

        static SponsorCategory()
        {
            Register(new SponsorCategoryInfo(
                Sponsor,
                AppStrings.SponsorBlock.CategorySponsorName,
                AppStrings.SponsorBlock.CategorySponsorDesc,
                "#00d400",
                SponsorAction.AutoSkip));

            Register(new SponsorCategoryInfo(
                Intro,
                AppStrings.SponsorBlock.CategoryIntroName,
                AppStrings.SponsorBlock.CategoryIntroDesc,
                "#00ffff",
                SponsorAction.ManualSkip));

            Register(new SponsorCategoryInfo(
                Outro,
                AppStrings.SponsorBlock.CategoryOutroName,
                AppStrings.SponsorBlock.CategoryOutroDesc,
                "#0202ed",
                SponsorAction.ManualSkip));

            Register(new SponsorCategoryInfo(
                Interaction,
                AppStrings.SponsorBlock.CategoryInteractionName,
                AppStrings.SponsorBlock.CategoryInteractionDesc,
                "#cc00ff",
                SponsorAction.AutoSkip));

            Register(new SponsorCategoryInfo(
                SelfPromo,
                AppStrings.SponsorBlock.CategorySelfPromoName,
                AppStrings.SponsorBlock.CategorySelfPromoDesc,
                "#ffff00",
                SponsorAction.AutoSkip));

            Register(new SponsorCategoryInfo(
                Preview,
                AppStrings.SponsorBlock.CategoryPreviewName,
                AppStrings.SponsorBlock.CategoryPreviewDesc,
                "#008fd6",
                SponsorAction.AutoSkip));

            Register(new SponsorCategoryInfo(
                Filler,
                AppStrings.SponsorBlock.CategoryFillerName,
                AppStrings.SponsorBlock.CategoryFillerDesc,
                "#7300ff",
                SponsorAction.ManualSkip));

            Register(new SponsorCategoryInfo(
                MusicOfftopic,
                AppStrings.SponsorBlock.CategoryMusicOfftopicName,
                AppStrings.SponsorBlock.CategoryMusicOfftopicDesc,
                "#ff9900",
                SponsorAction.AutoSkip));

            Register(new SponsorCategoryInfo(
                PoiHighlight,
                AppStrings.SponsorBlock.CategoryPoiHighlightName,
                AppStrings.SponsorBlock.CategoryPoiHighlightDesc,
                "#ff1684",
                SponsorAction.Disabled));
        }

        private static void Register(SponsorCategoryInfo info)
        {
            CategoriesMap[info.Key] = info;
            CategoriesList.Add(info);
        }

        public static IReadOnlyList<SponsorCategoryInfo> All
        {
            get { return CategoriesList; }
        }

        public static string NormalizeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return Sponsor;
            string clean = key.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "");
            switch (clean)
            {
                case "intermission":
                case "intro":
                    return Intro;
                case "endcards":
                case "outro":
                    return Outro;
                case "selfpromo":
                case "selfpromotion":
                    return SelfPromo;
                case "musicofftopic":
                case "nonmusic":
                    return MusicOfftopic;
                case "poihighlight":
                case "highlight":
                case "pointofinterest":
                    return PoiHighlight;
                case "interaction":
                case "subscribe":
                    return Interaction;
                case "preview":
                case "recap":
                    return Preview;
                case "filler":
                case "tangent":
                    return Filler;
                case "sponsor":
                    return Sponsor;
                default:
                    return key.Trim().ToLowerInvariant();
            }
        }

        public static SponsorCategoryInfo Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            SponsorCategoryInfo info;
            if (CategoriesMap.TryGetValue(key, out info)) return info;
            string norm = NormalizeKey(key);
            if (norm != key && CategoriesMap.TryGetValue(norm, out info)) return info;
            return null;
        }

        public static string GetName(string key)
        {
            SponsorCategoryInfo info = Get(key);
            return info != null ? info.Name : (key ?? AppStrings.SponsorBlock.CategorySponsorName);
        }

        public static string GetColorHex(string key)
        {
            SponsorCategoryInfo info = Get(key);
            return info != null ? info.ColorHex : "#00d400";
        }

        public static Color GetColor(string key)
        {
            SponsorCategoryInfo info = Get(key);
            return info != null ? info.Color : Color.FromArgb(0, 212, 0);
        }

        public static SponsorAction GetDefaultAction(string key)
        {
            SponsorCategoryInfo info = Get(key);
            return info != null ? info.DefaultAction : SponsorAction.AutoSkip;
        }
    }
}
