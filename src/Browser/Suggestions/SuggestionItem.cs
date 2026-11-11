using System;
using wb.UI;

namespace wb.Browser.Suggestions
{
    public enum SuggestionType
    {
        Search,
        DirectUrl,
        History,
        Bookmark,
        InternalPage
    }

    public class SuggestionItem
    {
        public SuggestionType Type { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string ActionValue { get; set; }
        public string MatchedQuery { get; set; }

        public string IconName
        {
            get
            {
                switch (Type)
                {
                    case SuggestionType.Search:
                        return "magnifying-glass";
                    case SuggestionType.History:
                        return "clock-counter-clockwise";
                    case SuggestionType.Bookmark:
                        return "star";
                    case SuggestionType.InternalPage:
                        return "browser";
                    case SuggestionType.DirectUrl:
                    default:
                        return "globe";
                }
            }
        }

        public string TagName
        {
            get
            {
                switch (Type)
                {
                    case SuggestionType.Search:
                        return "Google Search";
                    case SuggestionType.History:
                        return "History";
                    case SuggestionType.Bookmark:
                        return "Bookmark";
                    case SuggestionType.InternalPage:
                        return "wb://";
                    case SuggestionType.DirectUrl:
                    default:
                        return null;
                }
            }
        }

        public bool IsUrl
        {
            get
            {
                return Type == SuggestionType.DirectUrl ||
                       Type == SuggestionType.History ||
                       Type == SuggestionType.Bookmark ||
                       Type == SuggestionType.InternalPage;
            }
        }

        public SuggestionItem()
        {
        }

        public SuggestionItem(SuggestionType type, string title, string subtitle, string actionValue, string matchedQuery = null)
        {
            Type = type;
            Title = title;
            Subtitle = subtitle;
            ActionValue = actionValue;
            MatchedQuery = matchedQuery;
        }

        public override string ToString()
        {
            return string.Format("{0}: {1} ({2})", Type, Title, ActionValue);
        }
    }
}
