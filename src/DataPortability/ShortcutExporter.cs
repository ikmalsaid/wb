using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using wb.Browser;

namespace wb.DataPortability
{
    public static class ShortcutExporter
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static void ExportToFile(ShortcutManager manager, string filePath)
        {
            if (manager == null) throw new ArgumentNullException("manager");
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException("filePath");

            string json = ExportToJson(manager.GetAll());
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }

        public static string ExportToJson(List<ShortcutItem> items)
        {
            if (items == null) items = new List<ShortcutItem>();
            return Serializer.Serialize(items);
        }
    }
}

