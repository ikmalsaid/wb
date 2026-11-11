using System;
using System.Collections.Generic;

namespace wb.UI
{
    /// <summary>
    /// Persistent configuration model for movable and toggleable toolbar buttons.
    /// Serialized to %LocalAppData%\wb\toolbar_config.json via StorageHelper.
    /// </summary>
    public class ToolbarConfigModel
    {
        public List<string> ButtonOrder { get; set; }
        public Dictionary<string, bool> Visibility { get; set; }

        public ToolbarConfigModel()
        {
            ButtonOrder = new List<string>
            {
                "Home",
                "Star",
                "Bookmarks",
                "History",
                "Downloads",
                "Shield",
                "Screenshot"
            };

            Visibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                { "Home", true },
                { "Star", true },
                { "Bookmarks", true },
                { "History", true },
                { "Downloads", true },
                { "Shield", true },
                { "Screenshot", true }
            };
        }
    }
}
