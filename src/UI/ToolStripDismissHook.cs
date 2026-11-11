using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace wb.UI
{
    public static class ToolStripDismissHook
    {
        private static readonly List<ToolStripDropDown> _activeDropDowns = new List<ToolStripDropDown>();
        private static IntPtr _hookId = IntPtr.Zero;
        private static LowLevelMouseProc _proc;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCRBUTTONDOWN = 0x00A4;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public static void Register(ToolStripDropDown dropDown)
        {
            if (dropDown == null || dropDown.IsDisposed) return;

            dropDown.Opened -= DropDown_Opened;
            dropDown.Opened += DropDown_Opened;
            dropDown.Closed -= DropDown_Closed;
            dropDown.Closed += DropDown_Closed;

            HookSubMenus(dropDown);
            dropDown.ItemAdded += (s, e) =>
            {
                ToolStripDropDownItem ddi = e.Item as ToolStripDropDownItem;
                if (ddi != null && ddi.DropDown != null && !ddi.DropDown.IsDisposed)
                {
                    Register(ddi.DropDown);
                }
            };
        }

        private static void HookSubMenus(ToolStripDropDown dropDown)
        {
            if (dropDown == null || dropDown.IsDisposed) return;
            try
            {
                for (int i = 0; i < dropDown.Items.Count; i++)
                {
                    ToolStripDropDownItem ddi = dropDown.Items[i] as ToolStripDropDownItem;
                    if (ddi != null && ddi.DropDown != null && !ddi.DropDown.IsDisposed)
                    {
                        Register(ddi.DropDown);
                    }
                }
            }
            catch { }
        }

        public static bool HasActiveDropDownFocus()
        {
            Point cursor = Cursor.Position;
            lock (_activeDropDowns)
            {
                for (int i = 0; i < _activeDropDowns.Count; i++)
                {
                    ToolStripDropDown dd = _activeDropDowns[i];
                    if (dd != null && !dd.IsDisposed && dd.Visible)
                    {
                        Rectangle ddBounds = dd.Bounds;
                        ddBounds.Inflate(6, 6);
                        if (ddBounds.Contains(cursor)) return true;

                        if (dd.ContainsFocus || dd.Focused) return true;

                        for (int itemIdx = 0; itemIdx < dd.Items.Count; itemIdx++)
                        {
                            ToolStripControlHost host = dd.Items[itemIdx] as ToolStripControlHost;
                            if (host != null && host.Control != null && !host.Control.IsDisposed)
                            {
                                if (host.Control.ContainsFocus || host.Control.Focused) return true;
                                if (host.Control.IsHandleCreated)
                                {
                                    Rectangle hostRect = host.Control.RectangleToScreen(host.Control.ClientRectangle);
                                    hostRect.Inflate(6, 6);
                                    if (hostRect.Contains(cursor)) return true;
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }

        public static void CloseAll()
        {
            List<ToolStripDropDown> toClose;
            lock (_activeDropDowns)
            {
                toClose = new List<ToolStripDropDown>(_activeDropDowns);
            }

            for (int i = 0; i < toClose.Count; i++)
            {
                ToolStripDropDown dd = toClose[i];
                if (dd != null && !dd.IsDisposed && dd.Visible)
                {
                    try
                    {
                        if (!dd.IsDisposed && dd.IsHandleCreated)
                        {
                            dd.BeginInvoke(new Action(() => { try { if (!dd.IsDisposed) dd.Close(); } catch { } }));
                        }
                        else
                        {
                            dd.Close();
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void DropDown_Opened(object sender, EventArgs e)
        {
            ToolStripDropDown dd = sender as ToolStripDropDown;
            if (dd == null) return;

            HookSubMenus(dd);

            lock (_activeDropDowns)
            {
                if (!_activeDropDowns.Contains(dd))
                {
                    _activeDropDowns.Add(dd);
                }
                EnsureHookInstalled();
            }
        }

        private static void DropDown_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            ToolStripDropDown dd = sender as ToolStripDropDown;
            if (dd == null) return;

            lock (_activeDropDowns)
            {
                _activeDropDowns.Remove(dd);
                if (_activeDropDowns.Count == 0)
                {
                    EnsureHookRemoved();
                }
            }
        }

        private static void EnsureHookInstalled()
        {
            if (_hookId == IntPtr.Zero)
            {
                _proc = HookCallback;
                IntPtr hMod = LoadLibrary("user32.dll");
                _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, hMod, 0);
            }
        }

        private static void EnsureHookRemoved()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                _proc = null;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN ||
                    msg == WM_NCLBUTTONDOWN || msg == WM_NCRBUTTONDOWN)
                {
                    try
                    {
                        MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                        Point clickPt = new Point(hookStruct.pt.x, hookStruct.pt.y);
                        IntPtr clickedHwnd = WindowFromPoint(hookStruct.pt);

                        bool insideAnyMenu = false;
                        lock (_activeDropDowns)
                        {
                            for (int i = 0; i < _activeDropDowns.Count; i++)
                            {
                                ToolStripDropDown dd = _activeDropDowns[i];
                                if (dd != null && !dd.IsDisposed && dd.Visible)
                                {
                                    if (dd.IsHandleCreated && clickedHwnd != IntPtr.Zero)
                                    {
                                        if (clickedHwnd == dd.Handle || IsChild(dd.Handle, clickedHwnd))
                                        {
                                            insideAnyMenu = true;
                                            break;
                                        }
                                    }

                                    Rectangle ddScreenRect = new Rectangle(dd.Location, dd.Size);
                                    Rectangle ddClientScreen = (!dd.IsDisposed && dd.IsHandleCreated) ? dd.RectangleToScreen(dd.ClientRectangle) : ddScreenRect;
                                    Rectangle ddBounds = dd.Bounds;
                                    ddBounds.Inflate(4, 4);
                                    ddScreenRect.Inflate(4, 4);
                                    ddClientScreen.Inflate(4, 4);
                                    if (ddBounds.Contains(clickPt) || ddScreenRect.Contains(clickPt) || ddClientScreen.Contains(clickPt))
                                    {
                                        insideAnyMenu = true;
                                        break;
                                    }

                                    // Check hosted child controls (ToolStripControlHost)
                                    for (int itemIdx = 0; itemIdx < dd.Items.Count; itemIdx++)
                                    {
                                        ToolStripControlHost host = dd.Items[itemIdx] as ToolStripControlHost;
                                        if (host != null && host.Control != null && !host.Control.IsDisposed && host.Control.IsHandleCreated)
                                        {
                                            Rectangle hostRect = host.Control.RectangleToScreen(host.Control.ClientRectangle);
                                            hostRect.Inflate(4, 4);
                                            if (hostRect.Contains(clickPt))
                                            {
                                                insideAnyMenu = true;
                                                break;
                                            }
                                        }
                                    }
                                    if (insideAnyMenu) break;

                                    // Check owner item button
                                    if (dd.OwnerItem != null && !dd.OwnerItem.IsDisposed)
                                    {
                                        ToolStrip parentStrip = dd.OwnerItem.GetCurrentParent();
                                        if (parentStrip != null && !parentStrip.IsDisposed)
                                        {
                                            Rectangle ownerScreenRect = parentStrip.RectangleToScreen(dd.OwnerItem.Bounds);
                                            if (ownerScreenRect.Contains(clickPt))
                                            {
                                                insideAnyMenu = true;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (!insideAnyMenu)
                        {
                            // Click is on the web canvas or outside all active menus! Dismiss all open dropdowns.
                            CloseAll();
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }
}
