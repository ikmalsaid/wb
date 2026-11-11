using System;
using System.Drawing;
using System.Windows.Forms;
using wb.Browser;
using wb.UI;

namespace wb.Screenshot
{
    public static class ScreenshotMenuHelper
    {
        public static int CurrentDelaySeconds = 0;

        public static ContextMenuStrip CreateScreenshotMenu(MainBrowserForm form)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            UITheme.ApplyContextMenu(menu);

            // 1. Capture Modes
            ToolStripMenuItem visibleItem = new ToolStripMenuItem("Capture Visible Part", null, (s, e) =>
            {
                ScreenshotController.TakeScreenshot(form, ScreenshotMode.VisiblePart, CurrentDelaySeconds);
            });
            visibleItem.ShortcutKeyDisplayString = "Ctrl+Shift+S";
            UITheme.ApplyMenuItem(visibleItem);

            ToolStripMenuItem fullPageItem = new ToolStripMenuItem("Capture Full Page", null, (s, e) =>
            {
                ScreenshotController.TakeScreenshot(form, ScreenshotMode.FullPage, CurrentDelaySeconds);
            });
            fullPageItem.ShortcutKeyDisplayString = "Ctrl+Shift+F";
            UITheme.ApplyMenuItem(fullPageItem);

            ToolStripMenuItem selectedAreaItem = new ToolStripMenuItem("Capture Selected Area", null, (s, e) =>
            {
                ScreenshotController.TakeScreenshot(form, ScreenshotMode.SelectedArea, CurrentDelaySeconds);
            });
            selectedAreaItem.ShortcutKeyDisplayString = "Ctrl+Shift+A";
            UITheme.ApplyMenuItem(selectedAreaItem);

            menu.Items.Add(visibleItem);
            menu.Items.Add(fullPageItem);
            menu.Items.Add(selectedAreaItem);
            menu.Items.Add(new ToolStripSeparator());

            // 2. Delay Submenu
            ToolStripMenuItem delayMenu = new ToolStripMenuItem("Delay");
            UITheme.ApplyMenuItem(delayMenu);

            int[] delays = new int[] { 0, 3, 5, 10 };
            string[] delayLabels = new string[] { "None (Instant)", "3 seconds", "5 seconds", "10 seconds" };

            for (int i = 0; i < delays.Length; i++)
            {
                int delayVal = delays[i];
                ToolStripMenuItem dItem = new ToolStripMenuItem(delayLabels[i], null, (s, e) =>
                {
                    CurrentDelaySeconds = delayVal;
                });
                dItem.Checked = (CurrentDelaySeconds == delayVal);
                UITheme.ApplyMenuItem(dItem);
                delayMenu.DropDownItems.Add(dItem);
            }

            menu.Items.Add(delayMenu);
            return menu;
        }

        public static ToolStripMenuItem CreateScreenshotSubMenu(MainBrowserForm form)
        {
            ToolStripMenuItem subMenu = new ToolStripMenuItem("Screenshot");
            UITheme.ApplyMenuItem(subMenu);

            ToolStripMenuItem visibleItem = new ToolStripMenuItem("Capture Visible Part", null, (s, e) =>
            {
                ScreenshotController.TakeScreenshot(form, ScreenshotMode.VisiblePart, CurrentDelaySeconds);
            });
            visibleItem.ShortcutKeyDisplayString = "Ctrl+Shift+S";
            UITheme.ApplyMenuItem(visibleItem);

            ToolStripMenuItem fullPageItem = new ToolStripMenuItem("Capture Full Page", null, (s, e) =>
            {
                ScreenshotController.TakeScreenshot(form, ScreenshotMode.FullPage, CurrentDelaySeconds);
            });
            fullPageItem.ShortcutKeyDisplayString = "Ctrl+Shift+F";
            UITheme.ApplyMenuItem(fullPageItem);

            ToolStripMenuItem selectedAreaItem = new ToolStripMenuItem("Capture Selected Area", null, (s, e) =>
            {
                ScreenshotController.TakeScreenshot(form, ScreenshotMode.SelectedArea, CurrentDelaySeconds);
            });
            selectedAreaItem.ShortcutKeyDisplayString = "Ctrl+Shift+A";
            UITheme.ApplyMenuItem(selectedAreaItem);

            subMenu.DropDownItems.Add(visibleItem);
            subMenu.DropDownItems.Add(fullPageItem);
            subMenu.DropDownItems.Add(selectedAreaItem);
            subMenu.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem delayMenu = new ToolStripMenuItem("Delay");
            UITheme.ApplyMenuItem(delayMenu);

            int[] delays = new int[] { 0, 3, 5, 10 };
            string[] delayLabels = new string[] { "None (Instant)", "3 seconds", "5 seconds", "10 seconds" };

            for (int i = 0; i < delays.Length; i++)
            {
                int delayVal = delays[i];
                ToolStripMenuItem dItem = new ToolStripMenuItem(delayLabels[i], null, (s, e) =>
                {
                    CurrentDelaySeconds = delayVal;
                });
                dItem.Checked = (CurrentDelaySeconds == delayVal);
                UITheme.ApplyMenuItem(dItem);
                delayMenu.DropDownItems.Add(dItem);
            }

            subMenu.DropDownItems.Add(delayMenu);
            return subMenu;
        }
    }
}
