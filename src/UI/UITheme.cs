using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace wb.UI
{
    public static class UITheme
    {
        // Colors (Dark Material Design)
        public static readonly Color FormBackground = MaterialColors.ToolbarBackground;
        public static readonly Color ToolbarBackground = MaterialColors.NavBarBackground;
        public static readonly Color PanelBackground = MaterialColors.NavBarBackground;
        public static readonly Color CardBackground = MaterialColors.NavBarBackground;
        public static readonly Color BorderColor = MaterialColors.Separator;
        public static readonly Color ControlBorder = MaterialColors.Separator;
        public static readonly Color SplitterColor = MaterialColors.Separator;
        public static readonly Color WindowBorder = Color.FromArgb(80, 84, 90);          // #50545A (Crisp outer window outline)
        public static readonly Color TextPrimary = MaterialColors.TextPrimary;
        public static readonly Color TextSecondary = MaterialColors.TextSecondary;
        public static readonly Color TextMuted = MaterialColors.TextDisabled;
        public static Color AccentBlue
        {
            get { return MaterialColors.AccentBlue; }
        }

        public static Color AccentColor { get { return BrandTheme.AccentColor; } }
        public static Color AccentTextColor { get { return BrandTheme.AccentTextColor; } }
        public static Color AccentHoverColor { get { return BrandTheme.AccentHoverColor; } }
        public static Color AccentPressedColor { get { return BrandTheme.AccentPressedColor; } }
        public static Color AccentGradientStart { get { return BrandTheme.AccentGradientStart; } }
        public static Color AccentGradientEnd { get { return BrandTheme.AccentGradientEnd; } }

        // Status Colors
        public static readonly Color StatusSuccess = MaterialColors.StatusSuccess;
        public static readonly Color StatusDanger = MaterialColors.StatusDanger;
        public static readonly Color StatusWarning = MaterialColors.StatusWarning;
        public static readonly Color StatusInfo = Color.SteelBlue;
        public static Color StatusActive
        {
            get { return MaterialColors.AccentBlue; }
        }
        public static readonly Color StatusNeutral = MaterialColors.TextSecondary;

        // Badge Colors (uBlock #666666 style)
        public static readonly Color BadgeBackground = MaterialColors.BadgeBackground;     // #666666 (uBlock / Toolbar badge)
        public static readonly Color BadgeText = MaterialColors.BadgeText;                 // White text

        // Fonts
        public static readonly Font FontDefault = new Font("Segoe UI", 9F, FontStyle.Regular);
        public static readonly Font FontDefaultBold = new Font("Segoe UI", 9F, FontStyle.Bold);
        public static readonly Font FontHeader = new Font("Segoe UI", 11F, FontStyle.Bold);
        public static readonly Font FontSmall = new Font("Segoe UI", 8.25F, FontStyle.Regular);
        public static readonly Font FontMono = new Font("Consolas", 9F, FontStyle.Regular);

        static UITheme()
        {
            InitializeApplicationTheme();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static Color GetStatusColor(string status)
        {
            if (string.IsNullOrEmpty(status)) return StatusNeutral;
            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)) return StatusSuccess;
            if (string.Equals(status, "Missing (Deleted)", StringComparison.OrdinalIgnoreCase)) return StatusDanger;
            if (string.Equals(status, "Paused", StringComparison.OrdinalIgnoreCase)) return StatusWarning;
            if (string.Equals(status, "Queued", StringComparison.OrdinalIgnoreCase)) return StatusInfo;
            if (string.Equals(status, "InProgress", StringComparison.OrdinalIgnoreCase)) return StatusActive;
            if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)) return StatusDanger;
            if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase)) return StatusNeutral;
            return StatusNeutral;
        }

        public static void EnableDoubleBuffering(Control control)
        {
            if (control == null) return;
            try
            {
                System.Reflection.PropertyInfo prop = typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (prop != null)
                {
                    prop.SetValue(control, true, null);
                }
            }
            catch
            {
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true)]
        public static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        public static extern int SetPreferredAppMode(int appMode);

        [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true)]
        public static extern bool AllowDarkModeForApp(bool allow);

        [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
        public static extern void FlushMenuThemes();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;
        private const int LVM_GETHEADER = 0x101F;

        public static void InitializeApplicationTheme()
        {
            try
            {
                SetPreferredAppMode(2); // 2 = ForceDark
            }
            catch
            {
                try
                {
                    AllowDarkModeForApp(true);
                }
                catch { }
            }

            try
            {
                FlushMenuThemes();
            }
            catch { }
        }

        public static void EnableImmersiveDarkMode(IntPtr hWnd, Color? captionColor = null, Color? textColor = null)
        {
            if (hWnd == IntPtr.Zero) return;
            try
            {
                try { AllowDarkModeForWindow(hWnd, true); } catch { }

                int useDarkMode = 1;
                // Windows 10 (2004+) & Windows 11 attribute 20
                DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
                // Windows 10 (1809 - 1909) attribute 19
                DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));

                Color cap = captionColor ?? FormBackground;
                int colorRef = (cap.R) | (cap.G << 8) | (cap.B << 16);
                DwmSetWindowAttribute(hWnd, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));

                Color txt = textColor ?? TextPrimary;
                int txtRef = (txt.R) | (txt.G << 8) | (txt.B << 16);
                DwmSetWindowAttribute(hWnd, DWMWA_TEXT_COLOR, ref txtRef, sizeof(int));

                try { SetWindowTheme(hWnd, "DarkMode_Explorer", null); } catch { }

                // Force DWM to recalculate non-client frame and repaint immersive dark title bar
                SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
            }
            catch
            {
            }
        }

        public static void ApplyForm(Form form)
        {
            if (form == null) return;
            form.Font = FontDefault;
            form.BackColor = FormBackground;
            form.ForeColor = TextPrimary;
            EnableDoubleBuffering(form);
            WbAppIcon.UpdateFormIcon(form);

            form.HandleCreated -= OnFormHandleCreated;
            form.HandleCreated += OnFormHandleCreated;

            form.Load -= OnFormLoad;
            form.Load += OnFormLoad;

            form.Shown -= OnFormShown;
            form.Shown += OnFormShown;

            form.VisibleChanged -= OnFormVisibleChanged;
            form.VisibleChanged += OnFormVisibleChanged;

            if (form.IsHandleCreated)
            {
                EnableImmersiveDarkMode(form.Handle);
            }

            ApplyHierarchy(form);

            form.ControlAdded -= OnFormControlAdded;
            form.ControlAdded += OnFormControlAdded;
        }

        private static void OnFormHandleCreated(object sender, EventArgs e)
        {
            Form f = sender as Form;
            if (f != null && f.IsHandleCreated)
            {
                EnableImmersiveDarkMode(f.Handle);
                WbAppIcon.UpdateFormIcon(f);
            }
        }

        private static void OnFormShown(object sender, EventArgs e)
        {
            Form f = sender as Form;
            if (f != null && f.IsHandleCreated)
            {
                EnableImmersiveDarkMode(f.Handle);
                WbAppIcon.UpdateFormIcon(f);
            }
        }

        private static void OnFormVisibleChanged(object sender, EventArgs e)
        {
            Form f = sender as Form;
            if (f != null && f.Visible && f.IsHandleCreated)
            {
                EnableImmersiveDarkMode(f.Handle);
                WbAppIcon.UpdateFormIcon(f);
            }
        }

        private static void OnFormControlAdded(object sender, ControlEventArgs e)
        {
            if (e.Control != null)
            {
                ApplyHierarchy(e.Control);
            }
        }

        private static void OnFormLoad(object sender, EventArgs e)
        {
            Form f = sender as Form;
            if (f != null && f.IsHandleCreated)
            {
                EnableImmersiveDarkMode(f.Handle);
                WbAppIcon.UpdateFormIcon(f);
            }
            Control c = sender as Control;
            if (c != null)
            {
                ApplyHierarchy(c);
            }
        }

        public static void ApplyForm(Form form, string title, Size size, Size minSize)
        {
            if (form == null) return;
            ApplyForm(form);
            form.Text = title;
            form.Size = size;
            form.MinimumSize = minSize;
            form.StartPosition = FormStartPosition.CenterParent;
        }

        public static void ApplyModalDialog(Form form, string title, Size size, Size minSize = default(Size))
        {
            if (form == null) return;
            ApplyForm(form);
            form.Text = title;
            form.ClientSize = size;
            if (minSize != default(Size))
            {
                form.MinimumSize = minSize;
            }
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.CenterParent;
        }

        public static void ApplyTopToolbar(TableLayoutPanel layout)
        {
            if (layout == null) return;
            layout.Dock = DockStyle.Top;
            layout.Height = 48;
            layout.RowCount = 1;
            layout.ColumnCount = 2;
            layout.ColumnStyles.Clear();
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.Padding = new Padding(6);
            layout.BackColor = ToolbarBackground;
            layout.ForeColor = TextPrimary;
        }

        public static void ApplyTopToolbar(Panel panel)
        {
            if (panel == null) return;
            panel.BackColor = ToolbarBackground;
            panel.ForeColor = TextPrimary;
        }

        public static void ApplyBottomBar(TableLayoutPanel layout)
        {
            if (layout == null) return;
            layout.Dock = DockStyle.Bottom;
            layout.Height = 46;
            layout.RowCount = 1;
            layout.ColumnCount = 2;
            layout.ColumnStyles.Clear();
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            layout.Padding = new Padding(6);
            layout.BackColor = ToolbarBackground;
            layout.ForeColor = TextPrimary;
        }

        public static void ApplyBottomBar(Panel panel)
        {
            if (panel == null) return;
            panel.BackColor = ToolbarBackground;
            panel.ForeColor = TextPrimary;
        }

        public static void ApplyCardPanel(Panel panel)
        {
            if (panel == null) return;
            panel.BackColor = CardBackground;
            panel.ForeColor = TextPrimary;
            panel.Padding = new Padding(16, 12, 16, 12);
        }

        // Input & Button Colors
        public static readonly Color InputBackground = Color.FromArgb(41, 42, 45);
        public static readonly Color InputBackgroundDark = MaterialColors.AddressBarBackground;
        public static readonly Color InputBorder = MaterialColors.Separator;
        public static readonly Color InputText = MaterialColors.TextPrimary;

        public static readonly Color ButtonBackground = Color.FromArgb(48, 50, 54);
        public static readonly Color ButtonHover = MaterialColors.ButtonHover;
        public static readonly Color ButtonPressed = MaterialColors.ButtonPressed;
        public static readonly Color ButtonBorder = MaterialColors.Separator;
        public static readonly Color ButtonDisabledBackground = Color.FromArgb(36, 37, 40);
        public static readonly Color ButtonDisabledText = Color.FromArgb(145, 148, 153);
        public static readonly Color ButtonDisabledBorder = Color.FromArgb(48, 50, 54);

        public static void ApplyButton(Button button, bool isPrimary = false)
        {
            if (button == null) return;
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.Font = isPrimary ? FontDefaultBold : FontDefault;
            EnableDoubleBuffering(button);

            Action updateStyle = () =>
            {
                if (button.IsDisposed) return;

                if (isPrimary)
                {
                    button.FlatAppearance.BorderSize = 0;
                    if (button.Enabled)
                    {
                        Color accent = ThemeManager.CurrentAccentColor;
                        button.BackColor = accent;
                        button.ForeColor = GetContrastTextColor(accent);
                        button.FlatAppearance.MouseOverBackColor = BrandTheme.GetHoverColor(accent);
                        button.FlatAppearance.MouseDownBackColor = BrandTheme.GetPressedColor(accent);
                    }
                    else
                    {
                        button.BackColor = ButtonDisabledBackground;
                        button.ForeColor = ButtonDisabledText;
                        button.FlatAppearance.MouseOverBackColor = ButtonDisabledBackground;
                        button.FlatAppearance.MouseDownBackColor = ButtonDisabledBackground;
                    }
                }
                else
                {
                    button.FlatAppearance.BorderSize = 1;
                    if (button.Enabled)
                    {
                        button.BackColor = ButtonBackground;
                        button.ForeColor = TextPrimary;
                        button.FlatAppearance.BorderColor = ButtonBorder;
                        button.FlatAppearance.MouseOverBackColor = ButtonHover;
                        button.FlatAppearance.MouseDownBackColor = ButtonPressed;
                    }
                    else
                    {
                        button.BackColor = ButtonDisabledBackground;
                        button.ForeColor = ButtonDisabledText;
                        button.FlatAppearance.BorderColor = ButtonDisabledBorder;
                        button.FlatAppearance.MouseOverBackColor = ButtonDisabledBackground;
                        button.FlatAppearance.MouseDownBackColor = ButtonDisabledBackground;
                    }
                }
                button.Invalidate();
            };

            updateStyle();

            button.EnabledChanged += (s, e) => updateStyle();

            // Override disabled state rendering to eliminate Win32 dark embossing / black text
            button.Paint += (s, e) =>
            {
                if (!button.Enabled)
                {
                    using (SolidBrush bgBrush = new SolidBrush(ButtonDisabledBackground))
                    {
                        e.Graphics.FillRectangle(bgBrush, button.ClientRectangle);
                    }

                    if (!isPrimary)
                    {
                        using (Pen borderPen = new Pen(ButtonDisabledBorder, 1))
                        {
                            e.Graphics.DrawRectangle(borderPen, 0, 0, button.Width - 1, button.Height - 1);
                        }
                    }

                    if (button.Image != null)
                    {
                        int imgX = (button.Width - button.Image.Width) / 2;
                        int imgY = (button.Height - button.Image.Height) / 2;
                        ControlPaint.DrawImageDisabled(e.Graphics, button.Image, imgX, imgY, ButtonDisabledBackground);
                    }

                    if (!string.IsNullOrEmpty(button.Text))
                    {
                        TextFormatFlags flags = GetTextFormatFlags(button.TextAlign);
                        if (!button.UseMnemonic)
                        {
                            flags |= TextFormatFlags.NoPrefix;
                        }
                        TextRenderer.DrawText(e.Graphics, button.Text, button.Font, button.ClientRectangle, ButtonDisabledText, flags);
                    }
                }
            };

            if (isPrimary)
            {
                EventHandler onAccentChanged = (s, e) =>
                {
                    if (button != null && !button.IsDisposed && button.Enabled)
                    {
                        updateStyle();
                    }
                };
                ThemeManager.AccentColorChanged += onAccentChanged;
                button.Disposed += (s, e) => { ThemeManager.AccentColorChanged -= onAccentChanged; };
            }
        }

        public static void ApplyTextBox(TextBox textBox, string placeholder = null, Color? backColor = null)
        {
            if (textBox == null) return;
            textBox.Font = FontDefault;
            textBox.ForeColor = InputText;
            textBox.BorderStyle = BorderStyle.FixedSingle;

            Action updateBg = () =>
            {
                if (textBox.IsDisposed) return;

                if (backColor.HasValue)
                {
                    textBox.BackColor = backColor.Value;
                    return;
                }

                if (!textBox.Enabled)
                {
                    textBox.BackColor = ButtonDisabledBackground;
                    textBox.ForeColor = ButtonDisabledText;
                    return;
                }

                textBox.ForeColor = InputText;

                // If placed in a Toolbar or Panel with ToolbarBackground, recess with AddressBarBackground
                Control p = textBox.Parent;
                while (p != null)
                {
                    if (p.BackColor == ToolbarBackground || p.BackColor == MaterialColors.NavBarBackground)
                    {
                        textBox.BackColor = InputBackgroundDark;
                        return;
                    }
                    if (p is Form) break;
                    p = p.Parent;
                }
                textBox.BackColor = InputBackground;
            };

            updateBg();
            textBox.ParentChanged += (s, e) => updateBg();
            textBox.EnabledChanged += (s, e) => updateBg();

            if (!string.IsNullOrEmpty(placeholder))
            {
                SetCueBanner(textBox, placeholder);
            }
        }

        public static Color CalculateContrastingTextColor(Color bg)
        {
            return BrandTheme.CalculateContrastingTextColor(bg);
        }

        public static Color GetContrastTextColor(Color bg)
        {
            return CalculateContrastingTextColor(bg);
        }

        public static Color AdjustBrightness(Color color, float factor)
        {
            float r = color.R;
            float g = color.G;
            float b = color.B;

            if (factor < 0)
            {
                factor = 1 + factor;
                r *= factor;
                g *= factor;
                b *= factor;
            }
            else
            {
                r = (255 - r) * factor + r;
                g = (255 - g) * factor + g;
                b = (255 - b) * factor + b;
            }

            return Color.FromArgb(color.A, (int)Math.Min(255, Math.Max(0, r)), (int)Math.Min(255, Math.Max(0, g)), (int)Math.Min(255, Math.Max(0, b)));
        }

        public static TextFormatFlags GetTextFormatFlags(ContentAlignment align)
        {
            TextFormatFlags flags = TextFormatFlags.WordEllipsis | TextFormatFlags.SingleLine;
            switch (align)
            {
                case ContentAlignment.TopLeft:
                    return flags | TextFormatFlags.Top | TextFormatFlags.Left;
                case ContentAlignment.TopCenter:
                    return flags | TextFormatFlags.Top | TextFormatFlags.HorizontalCenter;
                case ContentAlignment.TopRight:
                    return flags | TextFormatFlags.Top | TextFormatFlags.Right;
                case ContentAlignment.MiddleLeft:
                    return flags | TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
                case ContentAlignment.MiddleCenter:
                    return flags | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;
                case ContentAlignment.MiddleRight:
                    return flags | TextFormatFlags.VerticalCenter | TextFormatFlags.Right;
                case ContentAlignment.BottomLeft:
                    return flags | TextFormatFlags.Bottom | TextFormatFlags.Left;
                case ContentAlignment.BottomCenter:
                    return flags | TextFormatFlags.Bottom | TextFormatFlags.HorizontalCenter;
                case ContentAlignment.BottomRight:
                    return flags | TextFormatFlags.Bottom | TextFormatFlags.Right;
                default:
                    return flags | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;
            }
        }

        public static void ApplyLabel(Label label, bool isSecondary = false)
        {
            if (label == null) return;
            label.Font = isSecondary ? FontSmall : FontDefault;
            label.ForeColor = isSecondary ? TextSecondary : TextPrimary;
        }

        public static void ApplyFlowPanel(FlowLayoutPanel panel)
        {
            if (panel == null) return;
            panel.BackColor = Color.Transparent;
            panel.ForeColor = TextPrimary;
        }

        public static void ApplyComboBox(ComboBox comboBox, Action<DrawItemEventArgs, bool, bool> customItemPainter = null)
        {
            if (comboBox == null) return;
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.Font = FontDefault;
            comboBox.FlatStyle = FlatStyle.Flat;
            comboBox.BackColor = InputBackground;
            comboBox.ForeColor = InputText;
            comboBox.DrawMode = DrawMode.OwnerDrawFixed;

            Action applyTheme = () =>
            {
                if (comboBox.IsHandleCreated)
                {
                    try { AllowDarkModeForWindow(comboBox.Handle, true); } catch { }
                    SetWindowTheme(comboBox.Handle, "DarkMode_CFD", null);
                }
            };

            applyTheme();
            comboBox.HandleCreated += (s, e) => applyTheme();

            comboBox.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                bool isEdit = (e.State & DrawItemState.ComboBoxEdit) == DrawItemState.ComboBoxEdit;

                if (customItemPainter != null)
                {
                    customItemPainter(e, isSelected, isEdit);
                    return;
                }

                Color bg;
                Color fg;

                if (isSelected && !isEdit)
                {
                    bg = ThemeManager.CurrentAccentColor;
                    fg = GetContrastTextColor(bg);
                }
                else
                {
                    bg = InputBackground;
                    fg = InputText;
                }

                using (SolidBrush b = new SolidBrush(bg))
                {
                    e.Graphics.FillRectangle(b, e.Bounds);
                }

                string text = comboBox.Items[e.Index] != null ? comboBox.Items[e.Index].ToString() : string.Empty;
                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
                Rectangle textRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 8), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, text, comboBox.Font, textRect, fg, flags);
            };
        }

        public static void ApplyListView(ListView listView, bool enableMultiselect = true)
        {
            if (listView == null) return;
            EnableDoubleBuffering(listView);
            listView.View = View.Details;
            listView.FullRowSelect = true;
            listView.GridLines = false;
            listView.MultiSelect = enableMultiselect;
            listView.HideSelection = false;
            listView.Font = FontDefault;
            listView.BackColor = FormBackground;
            listView.ForeColor = TextPrimary;

            Action applyTheme = () =>
            {
                if (listView.IsHandleCreated)
                {
                    try { AllowDarkModeForWindow(listView.Handle, true); } catch { }
                    SetWindowTheme(listView.Handle, "DarkMode_Explorer", null);
                    DarkListViewHeaderSubclass.Attach(listView);
                }
            };

            applyTheme();
            listView.HandleCreated += (s, e) => applyTheme();
            listView.ColumnWidthChanged += (s, e) =>
            {
                if (listView.IsHandleCreated)
                {
                    IntPtr hHeader = SendMessage(listView.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                    DarkListViewHeaderSubclass.InvalidateHeader(hHeader);
                }
            };
            listView.Resize += (s, e) =>
            {
                if (listView.IsHandleCreated)
                {
                    IntPtr hHeader = SendMessage(listView.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                    DarkListViewHeaderSubclass.InvalidateHeader(hHeader);
                }
            };

            listView.OwnerDraw = true;
            listView.DrawColumnHeader += (s, e) =>
            {
                Rectangle r = e.Bounds;
                using (SolidBrush b = new SolidBrush(MaterialColors.NavBarBackground))
                {
                    e.Graphics.FillRectangle(b, r);
                }
                using (Pen p = new Pen(MaterialColors.Separator))
                {
                    e.Graphics.DrawLine(p, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
                    e.Graphics.DrawLine(p, r.Right - 1, r.Top + 3, r.Right - 1, r.Bottom - 4);
                }
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.NoPrefix;
                if (e.Header.TextAlign == HorizontalAlignment.Center) flags |= TextFormatFlags.HorizontalCenter;
                else if (e.Header.TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
                else flags |= TextFormatFlags.Left;

                Rectangle textRect = new Rectangle(r.X + 8, r.Y, Math.Max(0, r.Width - 16), r.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, FontDefaultBold, textRect, MaterialColors.TextPrimary, flags);
            };
            listView.DrawItem += (s, e) => { e.DrawDefault = true; };
            listView.DrawSubItem += (s, e) => { e.DrawDefault = true; };
        }

        public static void ApplyModernListView(
            ListView listView,
            int rowHeight = 38,
            bool enableMultiselect = true,
            Action<DrawListViewSubItemEventArgs, bool, bool> customSubItemPainter = null)
        {
            if (listView == null) return;
            EnableDoubleBuffering(listView);
            listView.View = View.Details;
            listView.FullRowSelect = true;
            listView.GridLines = false;
            listView.MultiSelect = enableMultiselect;
            listView.HideSelection = false;
            listView.Font = FontDefault;
            listView.BackColor = FormBackground;
            listView.ForeColor = TextPrimary;

            // Enforce modern row height via ImageList
            ImageList rowHeightList = new ImageList();
            rowHeightList.ImageSize = new Size(1, Math.Max(16, rowHeight));
            listView.SmallImageList = rowHeightList;

            Action applyTheme = () =>
            {
                if (listView.IsHandleCreated)
                {
                    try { AllowDarkModeForWindow(listView.Handle, true); } catch { }
                    SetWindowTheme(listView.Handle, "DarkMode_Explorer", null);
                    DarkListViewHeaderSubclass.Attach(listView);
                }
            };

            applyTheme();
            listView.HandleCreated += (s, e) => applyTheme();
            listView.ColumnWidthChanged += (s, e) =>
            {
                if (listView.IsHandleCreated)
                {
                    IntPtr hHeader = SendMessage(listView.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                    DarkListViewHeaderSubclass.InvalidateHeader(hHeader);
                }
            };
            listView.Resize += (s, e) =>
            {
                if (listView.IsHandleCreated)
                {
                    IntPtr hHeader = SendMessage(listView.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                    DarkListViewHeaderSubclass.InvalidateHeader(hHeader);
                }
            };

            int hoveredIndex = -1;
            listView.MouseMove += (s, e) =>
            {
                ListViewItem item = listView.GetItemAt(e.X, e.Y);
                int newIndex = item != null ? item.Index : -1;
                if (newIndex != hoveredIndex)
                {
                    int old = hoveredIndex;
                    hoveredIndex = newIndex;
                    if (old >= 0 && old < listView.Items.Count) listView.Invalidate(listView.Items[old].Bounds);
                    if (hoveredIndex >= 0 && hoveredIndex < listView.Items.Count) listView.Invalidate(listView.Items[hoveredIndex].Bounds);
                }
            };
            listView.MouseLeave += (s, e) =>
            {
                if (hoveredIndex >= 0)
                {
                    int old = hoveredIndex;
                    hoveredIndex = -1;
                    if (old >= 0 && old < listView.Items.Count) listView.Invalidate(listView.Items[old].Bounds);
                }
            };

            listView.OwnerDraw = true;
            listView.DrawColumnHeader += (s, e) =>
            {
                Rectangle r = e.Bounds;
                using (SolidBrush b = new SolidBrush(MaterialColors.NavBarBackground))
                {
                    e.Graphics.FillRectangle(b, r);
                }
                using (Pen p = new Pen(MaterialColors.Separator))
                {
                    e.Graphics.DrawLine(p, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
                    e.Graphics.DrawLine(p, r.Right - 1, r.Top + 6, r.Right - 1, r.Bottom - 7);
                }
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.NoPrefix;
                if (e.Header.TextAlign == HorizontalAlignment.Center) flags |= TextFormatFlags.HorizontalCenter;
                else if (e.Header.TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
                else flags |= TextFormatFlags.Left;

                Rectangle textRect = new Rectangle(r.X + 12, r.Y, Math.Max(0, r.Width - 24), r.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, FontDefaultBold, textRect, MaterialColors.TextPrimary, flags);
            };

            listView.DrawItem += (s, e) =>
            {
                // SubItem drawing handles painting full rows
            };

            listView.DrawSubItem += (s, e) =>
            {
                if (e.Item == null) return;
                bool isSelected = e.Item.Selected;
                bool isHovered = (e.ItemIndex == hoveredIndex);

                Color bg = (e.ItemIndex % 2 == 0) ? Color.FromArgb(32, 33, 36) : Color.FromArgb(36, 37, 40);
                if (isHovered) bg = Color.FromArgb(48, 50, 54);
                if (isSelected) bg = Color.FromArgb(28, 45, 70);

                Rectangle bounds = e.Bounds;
                using (SolidBrush bgBrush = new SolidBrush(bg))
                {
                    e.Graphics.FillRectangle(bgBrush, bounds);
                }

                if (isSelected && e.ColumnIndex == 0)
                {
                    using (SolidBrush accB = new SolidBrush(ThemeManager.CurrentAccentColor))
                    {
                        e.Graphics.FillRectangle(accB, bounds.X, bounds.Y, 3, bounds.Height);
                    }
                }

                using (Pen sepP = new Pen(Color.FromArgb(45, 47, 50), 1))
                {
                    e.Graphics.DrawLine(sepP, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
                }

                if (customSubItemPainter != null)
                {
                    customSubItemPainter(e, isSelected, isHovered);
                    return;
                }

                string text = e.SubItem != null ? e.SubItem.Text : string.Empty;
                if (!string.IsNullOrEmpty(text))
                {
                    Font font = (e.ColumnIndex == 0) ? FontDefaultBold : FontDefault;
                    Color fg = (e.ColumnIndex == 0) ? MaterialColors.TextPrimary : MaterialColors.TextSecondary;

                    TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.NoPrefix;
                    if (e.Header.TextAlign == HorizontalAlignment.Center) flags |= TextFormatFlags.HorizontalCenter;
                    else if (e.Header.TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
                    else flags |= TextFormatFlags.Left;

                    int pad = (e.ColumnIndex == 0) ? 14 : 10;
                    Rectangle textRect = new Rectangle(bounds.X + pad, bounds.Y, Math.Max(0, bounds.Width - pad - 6), bounds.Height);
                    TextRenderer.DrawText(e.Graphics, text, font, textRect, fg, bg, flags);
                }
            };
        }

        public static void ApplyModernTreeView(TreeView treeView, int itemHeight = 28)
        {
            if (treeView == null) return;
            EnableDoubleBuffering(treeView);
            treeView.BorderStyle = BorderStyle.None;
            treeView.ItemHeight = Math.Max(22, itemHeight);
            treeView.Font = FontDefault;
            treeView.BackColor = FormBackground;
            treeView.ForeColor = TextPrimary;
            treeView.LineColor = BorderColor;
            treeView.HotTracking = true;
            treeView.ShowLines = false;
            treeView.ShowPlusMinus = true;
            treeView.ShowRootLines = true;
            treeView.FullRowSelect = true;
            treeView.HideSelection = false;
            treeView.Indent = 18;

            Action applyTheme = () =>
            {
                if (treeView.IsHandleCreated)
                {
                    try { AllowDarkModeForWindow(treeView.Handle, true); } catch { }
                    SetWindowTheme(treeView.Handle, "DarkMode_Explorer", null);
                }
            };

            applyTheme();
            treeView.HandleCreated += (s, e) => applyTheme();
        }

        public static void ApplyCheckBox(CheckBox checkBox)
        {
            if (checkBox == null) return;
            checkBox.Font = FontDefault;
            checkBox.ForeColor = TextPrimary;
            checkBox.BackColor = Color.Transparent;

            Action applyTheme = () =>
            {
                if (checkBox.IsHandleCreated)
                {
                    try { AllowDarkModeForWindow(checkBox.Handle, true); } catch { }
                    SetWindowTheme(checkBox.Handle, "DarkMode_Explorer", null);
                }
            };

            applyTheme();
            checkBox.HandleCreated += (s, e) => applyTheme();
        }

        public static void ApplyRadioButton(RadioButton radioButton)
        {
            if (radioButton == null) return;
            radioButton.Font = FontDefault;
            radioButton.ForeColor = TextPrimary;
            radioButton.BackColor = Color.Transparent;

            Action applyTheme = () =>
            {
                if (radioButton.IsHandleCreated)
                {
                    try { AllowDarkModeForWindow(radioButton.Handle, true); } catch { }
                    SetWindowTheme(radioButton.Handle, "DarkMode_Explorer", null);
                }
            };

            applyTheme();
            radioButton.HandleCreated += (s, e) => applyTheme();
        }

        public static void ApplyControlTheme(Control c)
        {
            if (c == null || c.IsDisposed) return;

            string typeName = c.GetType().Name;
            if (typeName == "WbTopBar" || typeName == "NewTabView" || typeName == "IncognitoNewTabView" ||
                typeName == "ScreenshotView" || typeName == "ChromeTabControl" || typeName == "TabLoadingOverlay" ||
                typeName == "FullScreenExitOverlay" || typeName == "WbBottomBar")
            {
                return;
            }

            ListView lv = c as ListView;
            if (lv != null)
            {
                ApplyListView(lv);
                return;
            }

            ComboBox cb = c as ComboBox;
            if (cb != null)
            {
                ApplyComboBox(cb);
                return;
            }

            TextBox tb = c as TextBox;
            if (tb != null)
            {
                if (tb.BorderStyle == BorderStyle.Fixed3D || tb.BackColor == SystemColors.Window || tb.BackColor == Color.White)
                {
                    ApplyTextBox(tb);
                }
                return;
            }

            Button btn = c as Button;
            if (btn != null)
            {
                if (btn.FlatStyle == FlatStyle.Standard || btn.BackColor == SystemColors.Control)
                {
                    ApplyButton(btn, isPrimary: false);
                }
                return;
            }

            CheckBox chk = c as CheckBox;
            if (chk != null)
            {
                ApplyCheckBox(chk);
                return;
            }

            RadioButton rb = c as RadioButton;
            if (rb != null)
            {
                ApplyRadioButton(rb);
                return;
            }

            Label lbl = c as Label;
            if (lbl != null)
            {
                if (lbl.ForeColor == SystemColors.ControlText || lbl.ForeColor == Color.Black)
                {
                    lbl.ForeColor = TextPrimary;
                }
                return;
            }

            TableLayoutPanel tlp = c as TableLayoutPanel;
            if (tlp != null)
            {
                tlp.ForeColor = TextPrimary;
                return;
            }

            FlowLayoutPanel flp = c as FlowLayoutPanel;
            if (flp != null)
            {
                flp.ForeColor = TextPrimary;
                return;
            }

            Panel p = c as Panel;
            if (p != null)
            {
                if (p.BackColor == SystemColors.Control)
                {
                    p.BackColor = FormBackground;
                }
                p.ForeColor = TextPrimary;
                return;
            }

            if (c.ContextMenuStrip != null)
            {
                ApplyContextMenu(c.ContextMenuStrip);
            }
        }

        public static void ApplyHierarchy(Control root)
        {
            if (root == null || root.IsDisposed) return;
            ApplyControlTheme(root);

            if (root.HasChildren)
            {
                foreach (Control child in root.Controls)
                {
                    ApplyHierarchy(child);
                }
            }
        }

        public static void ApplySplitter(Splitter splitter)
        {
            if (splitter == null) return;
            splitter.Dock = DockStyle.Bottom;
            splitter.Height = 6;
            splitter.BackColor = SplitterColor;
            splitter.MinExtra = 120;
            splitter.MinSize = 150;
        }

        // Universal Dropdown scroll-on-hover, mouse wheel & dark theme rendering support
        private static readonly MethodInfo _scrollInternalMethod = typeof(ToolStripDropDownMenu).GetMethod(
            "ScrollInternal", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(bool) }, null);
        private static readonly MethodInfo _setBoundsMethod = typeof(ToolStripItem).GetMethod(
            "SetBounds", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(Rectangle) }, null);
        private static readonly PropertyInfo _upScrollButtonProp = typeof(ToolStripDropDownMenu).GetProperty(
            "UpScrollButton", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo _downScrollButtonProp = typeof(ToolStripDropDownMenu).GetProperty(
            "DownScrollButton", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly List<ToolStripDropDown> _activeDropDowns = new List<ToolStripDropDown>();
        private static DropDownWheelMessageFilter _wheelMessageFilter;
        private static System.Windows.Forms.Timer _dropDownHoverScrollTimer;
        private static int _hoverScrollTicks = 0;

        public static void ApplyContextMenu(ContextMenuStrip menu)
        {
            ApplyContextMenu((ToolStripDropDown)menu);
        }

        public static void ApplyContextMenu(ToolStripDropDown menu)
        {
            if (menu == null) return;
            if (!(menu.Renderer is MaterialToolStripRenderer))
            {
                menu.Renderer = new MaterialToolStripRenderer();
            }
            menu.Font = FontDefault;
            menu.BackColor = ToolbarBackground;
            menu.ForeColor = TextPrimary;
            ToolStripDismissHook.Register(menu);
            RegisterDropDown(menu);
        }

        public static void ConfigureDropDownConstraints(ToolStripDropDown menu, Point screenPt, float dpiScale = 0f)
        {
            if (menu == null) return;
            if (dpiScale <= 0f) dpiScale = DpiHelper.GetDpiScale(menu);

            Screen scr = Screen.FromPoint(screenPt);
            Rectangle work = (scr != null) ? scr.WorkingArea : Screen.PrimaryScreen.WorkingArea;

            int spaceBelow = work.Bottom - screenPt.Y - 16;
            int spaceAbove = screenPt.Y - work.Top - 16;
            int maxSpace = Math.Max(240, Math.Max(spaceBelow, spaceAbove));

            int itemH = 0;
            if (menu.Items.Count > 0)
            {
                for (int i = 0; i < menu.Items.Count; i++)
                {
                    ToolStripItem it = menu.Items[i];
                    if (it != null && !(it is ToolStripSeparator))
                    {
                        if (it.Height > 0)
                        {
                            itemH = it.Height;
                            break;
                        }
                        Size pref = it.GetPreferredSize(Size.Empty);
                        if (pref.Height > 0)
                        {
                            itemH = pref.Height;
                            break;
                        }
                    }
                }
            }
            if (itemH <= 0)
            {
                itemH = Math.Max(22, (int)Math.Round(22 * dpiScale));
            }

            int scrollBtnH = Math.Max(24, (int)Math.Round(24 * dpiScale));
            int overhead = 2 * scrollBtnH;

            int maxTargetH = Math.Max(220, Math.Min((int)Math.Round(720 * dpiScale), maxSpace));
            int availableForItems = Math.Max(itemH * 3, maxTargetH - overhead);
            int itemCount = availableForItems / itemH;
            int fittedH = (itemCount * itemH) + overhead;

            int maxW = Math.Max(260, (int)Math.Round(380 * dpiScale));
            menu.MaximumSize = new Size(maxW, fittedH);

            HookDropDownScrollButtons(menu);
        }

        public static void ScrollDropDown(ToolStripDropDown dd, bool up, int count = 1)
        {
            if (dd == null || dd.IsDisposed || !dd.Visible) return;
            try
            {
                if (_scrollInternalMethod != null)
                {
                    ToolStripItem upBtn = _upScrollButtonProp != null ? (ToolStripItem)_upScrollButtonProp.GetValue(dd, null) : null;
                    ToolStripItem downBtn = _downScrollButtonProp != null ? (ToolStripItem)_downScrollButtonProp.GetValue(dd, null) : null;

                    for (int i = 0; i < count; i++)
                    {
                        if (up)
                        {
                            if (upBtn != null && (!upBtn.Visible || !upBtn.Enabled)) break;
                            if (dd.Items.Count > 0)
                            {
                                int minTop = int.MaxValue;
                                for (int k = 0; k < dd.Items.Count; k++)
                                {
                                    ToolStripItem it = dd.Items[k];
                                    if (it != null && it != upBtn && it != downBtn && it.Available)
                                    {
                                        if (it.Bounds.Top < minTop) minTop = it.Bounds.Top;
                                    }
                                }
                                int topLimit = (upBtn != null && upBtn.Visible) ? upBtn.Bounds.Bottom : 0;
                                if (minTop >= topLimit)
                                {
                                    if (upBtn != null) upBtn.Enabled = false;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            if (downBtn != null && (!downBtn.Visible || !downBtn.Enabled)) break;
                            if (dd.Items.Count > 0)
                            {
                                int maxBottom = 0;
                                for (int k = 0; k < dd.Items.Count; k++)
                                {
                                    ToolStripItem it = dd.Items[k];
                                    if (it != null && it != upBtn && it != downBtn && it.Available)
                                    {
                                        if (it.Bounds.Bottom > maxBottom) maxBottom = it.Bounds.Bottom;
                                    }
                                }
                                int bottomLimit = (downBtn != null && downBtn.Visible) ? downBtn.Bounds.Top : dd.ClientSize.Height;
                                if (maxBottom <= bottomLimit)
                                {
                                    if (downBtn != null) downBtn.Enabled = false;
                                    break;
                                }
                            }
                        }

                        _scrollInternalMethod.Invoke(dd, new object[] { up });
                        EnforceScrollButtonBounds(dd);
                    }

                    dd.Invalidate();
                    if (upBtn != null)
                    {
                        upBtn.Invalidate();
                        ToolStripControlHost host = upBtn as ToolStripControlHost;
                        if (host != null && host.Control != null) host.Control.Invalidate();
                    }
                    if (downBtn != null)
                    {
                        downBtn.Invalidate();
                        ToolStripControlHost host = downBtn as ToolStripControlHost;
                        if (host != null && host.Control != null) host.Control.Invalidate();
                    }
                }
            }
            catch { }
        }

        public static void RegisterDropDown(ToolStripDropDown dd)
        {
            if (dd == null) return;

            HookDropDownScrollButtons(dd);

            dd.Opening -= DropDown_Opening;
            dd.Opening += DropDown_Opening;

            dd.Opened -= DropDown_Opened;
            dd.Opened += DropDown_Opened;

            dd.Closed -= DropDown_Closed;
            dd.Closed += DropDown_Closed;

            dd.Layout -= DropDown_Layout;
            dd.Layout += DropDown_Layout;

            HookSubItems(dd);
            dd.ItemAdded -= DropDown_ItemAdded;
            dd.ItemAdded += DropDown_ItemAdded;
        }

        private static void DropDown_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ToolStripDropDown dd = sender as ToolStripDropDown;
            if (dd != null)
            {
                if (dd.MaximumSize.Height == 0)
                {
                    ConfigureDropDownConstraints(dd, Cursor.Position);
                }
                else
                {
                    HookDropDownScrollButtons(dd);
                    dd.PerformLayout();
                    EnforceScrollButtonBounds(dd);
                }
            }
        }

        private static void DropDown_Opened(object sender, EventArgs e)
        {
            ToolStripDropDown dd = sender as ToolStripDropDown;
            if (dd == null || dd.IsDisposed) return;

            if (!_activeDropDowns.Contains(dd))
            {
                _activeDropDowns.Add(dd);
            }
            EnsureWheelMessageFilter();
            HookDropDownScrollButtons(dd);
            EnforceScrollButtonBounds(dd);
            StartHoverScrollTimer();
        }

        private static void DropDown_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            ToolStripDropDown dd = sender as ToolStripDropDown;
            if (dd != null)
            {
                _activeDropDowns.Remove(dd);
            }
            if (_activeDropDowns.Count == 0)
            {
                StopHoverScrollTimer();
            }
        }

        private static void DropDown_Layout(object sender, LayoutEventArgs e)
        {
            ToolStripDropDown dd = sender as ToolStripDropDown;
            if (dd != null)
            {
                HookDropDownScrollButtons(dd);
                EnforceScrollButtonBounds(dd);
            }
        }

        private static void DropDown_ItemAdded(object sender, ToolStripItemEventArgs e)
        {
            ToolStripDropDownItem item = e.Item as ToolStripDropDownItem;
            if (item != null)
            {
                HookDropDownItem(item);
            }
        }

        private static void HookSubItems(ToolStripDropDown dd)
        {
            if (dd == null) return;
            for (int i = 0; i < dd.Items.Count; i++)
            {
                ToolStripDropDownItem dropDownItem = dd.Items[i] as ToolStripDropDownItem;
                if (dropDownItem != null)
                {
                    HookDropDownItem(dropDownItem);
                }
            }
        }

        private static void HookDropDownItem(ToolStripDropDownItem item)
        {
            if (item == null) return;
            item.DropDownOpening -= DropDownItem_DropDownOpening;
            item.DropDownOpening += DropDownItem_DropDownOpening;
        }

        private static void DropDownItem_DropDownOpening(object sender, EventArgs e)
        {
            ToolStripDropDownItem item = sender as ToolStripDropDownItem;
            if (item != null && item.DropDown != null)
            {
                ApplyContextMenu(item.DropDown);
            }
        }

        private static void HookDropDownScrollButtons(ToolStripDropDown dd)
        {
            if (dd == null || dd.IsDisposed) return;
            try
            {
                ToolStripItem up = _upScrollButtonProp != null ? (ToolStripItem)_upScrollButtonProp.GetValue(dd, null) : null;
                ToolStripItem down = _downScrollButtonProp != null ? (ToolStripItem)_downScrollButtonProp.GetValue(dd, null) : null;

                HookScrollButtonItem(up, true);
                HookScrollButtonItem(down, false);
            }
            catch { }
        }

        private static void EnforceScrollButtonBounds(ToolStripDropDown dd)
        {
            if (dd == null || dd.IsDisposed) return;
            try
            {
                ToolStripItem up = _upScrollButtonProp != null ? (ToolStripItem)_upScrollButtonProp.GetValue(dd, null) : null;
                ToolStripItem down = _downScrollButtonProp != null ? (ToolStripItem)_downScrollButtonProp.GetValue(dd, null) : null;

                float dpi = DpiHelper.GetDpiScale(dd);
                int targetH = Math.Max(24, (int)Math.Round(24 * dpi));

                if (up != null && up.Visible && _setBoundsMethod != null)
                {
                    if (up.Bounds.X != 1 || up.Bounds.Y != 0 || up.Bounds.Width != dd.Width - 2 || up.Bounds.Height != targetH)
                    {
                        _setBoundsMethod.Invoke(up, new object[] { new Rectangle(1, 0, dd.Width - 2, targetH) });
                    }
                }
                if (down != null && down.Visible && _setBoundsMethod != null)
                {
                    int downY = dd.Height - targetH;
                    if (down.Bounds.X != 1 || down.Bounds.Y != downY || down.Bounds.Width != dd.Width - 2 || down.Bounds.Height != targetH)
                    {
                        _setBoundsMethod.Invoke(down, new object[] { new Rectangle(1, downY, dd.Width - 2, targetH) });
                    }
                }

                int minTop = int.MaxValue;
                int maxBottom = 0;
                for (int i = 0; i < dd.Items.Count; i++)
                {
                    ToolStripItem it = dd.Items[i];
                    if (it != null && it != up && it != down && it.Available)
                    {
                        if (it.Bounds.Top < minTop) minTop = it.Bounds.Top;
                        if (it.Bounds.Bottom > maxBottom) maxBottom = it.Bounds.Bottom;
                    }
                }

                if (up != null && up.Visible)
                {
                    int upBottom = up.Bounds.Bottom;
                    if (minTop >= upBottom)
                    {
                        up.Enabled = false;
                    }
                }

                if (down != null && down.Visible)
                {
                    int downTop = down.Bounds.Top;
                    if (maxBottom <= downTop)
                    {
                        down.Enabled = false;
                    }
                }
            }
            catch { }
        }

        private static void HookScrollButtonItem(ToolStripItem item, bool isUp)
        {
            if (item == null) return;

            float dpi = (item.Owner != null) ? DpiHelper.GetDpiScale(item.Owner) : 1.0f;
            int targetH = Math.Max(24, (int)Math.Round(24 * dpi));
            int dummyH = Math.Max(16, targetH - 4);

            ToolStripControlHost host = item as ToolStripControlHost;
            if (host != null && host.Control != null)
            {
                Control c = host.Control;
                Label lbl = c as Label;
                if (lbl != null && (lbl.Image == null || lbl.Image.Height != dummyH))
                {
                    lbl.Image = new Bitmap(1, dummyH);
                }
                c.Height = targetH;
                host.Height = targetH;
            }
            item.Height = targetH;

            item.Paint -= isUp ? (PaintEventHandler)ScrollButtonItem_PaintUp : ScrollButtonItem_PaintDown;
            item.Paint += isUp ? (PaintEventHandler)ScrollButtonItem_PaintUp : ScrollButtonItem_PaintDown;

            item.MouseEnter -= ScrollButtonItem_MouseEnter;
            item.MouseEnter += ScrollButtonItem_MouseEnter;

            item.MouseLeave -= ScrollButtonItem_MouseLeave;
            item.MouseLeave += ScrollButtonItem_MouseLeave;

            item.EnabledChanged -= ScrollButtonItem_EnabledChanged;
            item.EnabledChanged += ScrollButtonItem_EnabledChanged;

            if (host != null && host.Control != null)
            {
                Control c = host.Control;
                if (!object.Equals(c.Tag, "scroll_btn_configured"))
                {
                    c.Tag = "scroll_btn_configured";
                    c.BackColor = MaterialColors.NavBarBackground;
                    c.ForeColor = MaterialColors.TextPrimary;
                    EnableDoubleBuffering(c);

                    c.Paint += isUp ? (PaintEventHandler)ScrollButtonControl_PaintUp : ScrollButtonControl_PaintDown;
                    c.MouseEnter += ScrollButtonControl_MouseEnter;
                    c.MouseLeave += ScrollButtonControl_MouseLeave;
                    c.MouseMove += ScrollButtonControl_MouseMove;
                    c.EnabledChanged += ScrollButtonControl_EnabledChanged;
                }
            }
        }

        private static void ScrollButtonItem_MouseEnter(object sender, EventArgs e)
        {
            ToolStripItem item = sender as ToolStripItem;
            if (item != null && item.Owner != null) item.Invalidate();
        }

        private static void ScrollButtonItem_MouseLeave(object sender, EventArgs e)
        {
            ToolStripItem item = sender as ToolStripItem;
            if (item != null && item.Owner != null) item.Invalidate();
        }

        private static void ScrollButtonItem_EnabledChanged(object sender, EventArgs e)
        {
            ToolStripItem item = sender as ToolStripItem;
            if (item != null && item.Owner != null) item.Invalidate();
        }

        private static void ScrollButtonControl_MouseEnter(object sender, EventArgs e)
        {
            Control c = sender as Control;
            if (c != null && !c.IsDisposed) c.Invalidate();
        }

        private static void ScrollButtonControl_MouseLeave(object sender, EventArgs e)
        {
            Control c = sender as Control;
            if (c != null && !c.IsDisposed) c.Invalidate();
        }

        private static void ScrollButtonControl_MouseMove(object sender, MouseEventArgs e)
        {
            Control c = sender as Control;
            if (c != null && !c.IsDisposed) c.Invalidate();
        }

        private static void ScrollButtonControl_EnabledChanged(object sender, EventArgs e)
        {
            Control c = sender as Control;
            if (c != null && !c.IsDisposed) c.Invalidate();
        }

        private static void ScrollButtonItem_PaintUp(object sender, PaintEventArgs pe)
        {
            ToolStripItem item = sender as ToolStripItem;
            if (item == null) return;
            PaintScrollButton(pe.Graphics, new Rectangle(Point.Empty, item.Size), null, item, true);
        }

        private static void ScrollButtonItem_PaintDown(object sender, PaintEventArgs pe)
        {
            ToolStripItem item = sender as ToolStripItem;
            if (item == null) return;
            PaintScrollButton(pe.Graphics, new Rectangle(Point.Empty, item.Size), null, item, false);
        }

        private static void ScrollButtonControl_PaintUp(object sender, PaintEventArgs pe)
        {
            Control c = sender as Control;
            if (c == null) return;
            PaintScrollButton(pe.Graphics, c.ClientRectangle, c, null, true);
        }

        private static void ScrollButtonControl_PaintDown(object sender, PaintEventArgs pe)
        {
            Control c = sender as Control;
            if (c == null) return;
            PaintScrollButton(pe.Graphics, c.ClientRectangle, c, null, false);
        }

        private static void PaintScrollButton(Graphics g, Rectangle bounds, Control c, ToolStripItem item, bool isUp)
        {
            if (g == null || bounds.Width <= 0 || bounds.Height <= 0) return;

            Point cur = Cursor.Position;
            bool hovered = false;
            if (c != null && c.IsHandleCreated && !c.IsDisposed)
            {
                hovered = c.ClientRectangle.Contains(c.PointToClient(cur));
            }
            else if (item != null && item.Owner != null && item.Owner.IsHandleCreated)
            {
                hovered = item.Selected || item.Owner.RectangleToScreen(item.Bounds).Contains(cur);
            }

            bool enabled = (c != null ? c.Enabled : true) && (item != null ? item.Enabled : true);

            Color bg = (hovered && enabled) ? MaterialColors.ButtonHover : MaterialColors.NavBarBackground;
            using (SolidBrush b = new SolidBrush(bg))
            {
                g.FillRectangle(b, bounds);
            }

            // Restore the dropdown menu's top outline (on UpScrollButton) and bottom outline (on DownScrollButton)
            using (Pen borderPen = new Pen(MaterialColors.Separator, 1))
            {
                if (isUp)
                {
                    g.DrawLine(borderPen, bounds.X, bounds.Y, bounds.Right - 1, bounds.Y);
                }
                else
                {
                    g.DrawLine(borderPen, bounds.X, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
                }
            }

            // Arrow middle-centered in bounds
            int midX = bounds.X + bounds.Width / 2;
            int midY = bounds.Y + bounds.Height / 2;
            float dpi = c != null ? DpiHelper.GetDpiScale(c) : 1.0f;
            int arrowH = Math.Max(4, (int)Math.Round(4.0f * dpi));
            int arrowW = Math.Max(5, (int)Math.Round(6.0f * dpi));

            Point[] triangle;
            if (isUp)
            {
                triangle = new Point[]
                {
                    new Point(midX, midY - arrowH),
                    new Point(midX - arrowW, midY + arrowH),
                    new Point(midX + arrowW, midY + arrowH)
                };
            }
            else
            {
                triangle = new Point[]
                {
                    new Point(midX, midY + arrowH),
                    new Point(midX - arrowW, midY - arrowH),
                    new Point(midX + arrowW, midY - arrowH)
                };
            }

            Color arrowColor;
            if (!enabled)
            {
                arrowColor = MaterialColors.TextDisabled;
            }
            else if (hovered)
            {
                arrowColor = ThemeManager.CurrentAccentColor;
            }
            else
            {
                arrowColor = MaterialColors.TextPrimary;
            }

            SmoothingMode prevMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush ab = new SolidBrush(arrowColor))
            {
                g.FillPolygon(ab, triangle);
            }
            g.SmoothingMode = prevMode;
        }

        private static void StartHoverScrollTimer()
        {
            if (_dropDownHoverScrollTimer == null)
            {
                _dropDownHoverScrollTimer = new System.Windows.Forms.Timer();
                _dropDownHoverScrollTimer.Interval = 35;
                _dropDownHoverScrollTimer.Tick += HoverScrollTimer_Tick;
            }
            if (!_dropDownHoverScrollTimer.Enabled)
            {
                _hoverScrollTicks = 0;
                _dropDownHoverScrollTimer.Start();
            }
        }

        private static void StopHoverScrollTimer()
        {
            if (_dropDownHoverScrollTimer != null && _dropDownHoverScrollTimer.Enabled)
            {
                _dropDownHoverScrollTimer.Stop();
            }
            _hoverScrollTicks = 0;
        }

        private static void HoverScrollTimer_Tick(object sender, EventArgs e)
        {
            if (_activeDropDowns == null || _activeDropDowns.Count == 0)
            {
                StopHoverScrollTimer();
                return;
            }

            Point cur = Cursor.Position;
            ToolStripDropDown targetDd = null;
            bool targetUp = false;
            bool foundButton = false;

            for (int i = _activeDropDowns.Count - 1; i >= 0; i--)
            {
                ToolStripDropDown dd = _activeDropDowns[i];
                if (dd == null || dd.IsDisposed || !dd.Visible) continue;

                try
                {
                    ToolStripItem up = _upScrollButtonProp != null ? (ToolStripItem)_upScrollButtonProp.GetValue(dd, null) : null;
                    ToolStripItem down = _downScrollButtonProp != null ? (ToolStripItem)_downScrollButtonProp.GetValue(dd, null) : null;

                    if (up != null && up.Visible && up.Enabled && dd.RectangleToScreen(up.Bounds).Contains(cur))
                    {
                        targetDd = dd;
                        targetUp = true;
                        foundButton = true;
                        break;
                    }
                    if (down != null && down.Visible && down.Enabled && dd.RectangleToScreen(down.Bounds).Contains(cur))
                    {
                        targetDd = dd;
                        targetUp = false;
                        foundButton = true;
                        break;
                    }
                }
                catch { }
            }

            if (foundButton && targetDd != null)
            {
                _hoverScrollTicks++;
                if (_hoverScrollTicks == 1 || _hoverScrollTicks >= 3)
                {
                    ScrollDropDown(targetDd, targetUp, 1);
                }
            }
            else
            {
                _hoverScrollTicks = 0;
            }
        }

        private static void EnsureWheelMessageFilter()
        {
            if (_wheelMessageFilter == null)
            {
                _wheelMessageFilter = new DropDownWheelMessageFilter();
                Application.AddMessageFilter(_wheelMessageFilter);
            }
        }

        private class DropDownWheelMessageFilter : IMessageFilter
        {
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg == 0x020A) // WM_MOUSEWHEEL
                {
                    return HandleGlobalDropDownMouseWheel(ref m);
                }
                return false;
            }
        }

        private static bool HandleGlobalDropDownMouseWheel(ref Message m)
        {
            if (_activeDropDowns == null || _activeDropDowns.Count == 0) return false;

            Point pt;
            int x = (short)(m.LParam.ToInt32() & 0xFFFF);
            int y = (short)((m.LParam.ToInt32() >> 16) & 0xFFFF);
            if (x != 0 || y != 0)
            {
                pt = new Point(x, y);
            }
            else
            {
                pt = Cursor.Position;
            }

            for (int i = _activeDropDowns.Count - 1; i >= 0; i--)
            {
                ToolStripDropDown dd = _activeDropDowns[i];
                if (dd != null && !dd.IsDisposed && dd.Visible && dd.Bounds.Contains(pt))
                {
                    short delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                    if (delta != 0)
                    {
                        bool up = (delta > 0);
                        int notches = Math.Max(1, Math.Abs(delta) / 120);
                        ScrollDropDown(dd, up, notches * 3);
                        return true;
                    }
                }
            }
            return false;
        }

        public static string FormatShortcutDisplayString(Keys shortcut)
        {
            if (shortcut == Keys.None) return null;

            Keys keyCode = shortcut & Keys.KeyCode;
            Keys modifiers = shortcut & Keys.Modifiers;

            string keyPart;
            switch (keyCode)
            {
                case Keys.Oemplus:
                case Keys.Add:
                    keyPart = "+";
                    break;
                case Keys.OemMinus:
                case Keys.Subtract:
                    keyPart = "-";
                    break;
                case Keys.OemPeriod:
                    keyPart = ".";
                    break;
                case Keys.Oemcomma:
                    keyPart = ",";
                    break;
                case Keys.OemQuestion:
                    keyPart = "?";
                    break;
                case Keys.Oemtilde:
                    keyPart = "~";
                    break;
                case Keys.OemOpenBrackets:
                    keyPart = "[";
                    break;
                case Keys.OemCloseBrackets:
                    keyPart = "]";
                    break;
                case Keys.OemPipe:
                    keyPart = "\\";
                    break;
                case Keys.Oem1:
                    keyPart = ";";
                    break;
                case Keys.Oem7:
                    keyPart = "'";
                    break;
                case Keys.Escape:
                    keyPart = "Esc";
                    break;
                case Keys.Delete:
                    keyPart = "Del";
                    break;
                case Keys.Back:
                    keyPart = "Backspace";
                    break;
                case Keys.D0:
                case Keys.D1:
                case Keys.D2:
                case Keys.D3:
                case Keys.D4:
                case Keys.D5:
                case Keys.D6:
                case Keys.D7:
                case Keys.D8:
                case Keys.D9:
                    keyPart = ((int)keyCode - (int)Keys.D0).ToString();
                    break;
                default:
                    keyPart = keyCode.ToString();
                    break;
            }

            string modPart = "";
            if ((modifiers & Keys.Control) == Keys.Control) modPart += "Ctrl+";
            if ((modifiers & Keys.Shift) == Keys.Shift) modPart += "Shift+";
            if ((modifiers & Keys.Alt) == Keys.Alt) modPart += "Alt+";

            return modPart + keyPart;
        }

        public static void ApplyMenuItem(ToolStripMenuItem item)
        {
            if (item == null) return;
            item.Font = FontDefault;
            item.ForeColor = TextPrimary;
            if (item.ShortcutKeys != Keys.None && string.IsNullOrEmpty(item.ShortcutKeyDisplayString))
            {
                string displayStr = FormatShortcutDisplayString(item.ShortcutKeys);
                if (!string.IsNullOrEmpty(displayStr))
                {
                    item.ShortcutKeyDisplayString = displayStr;
                }
            }
            if (item.HasDropDownItems && item.DropDown != null)
            {
                item.DropDown.Renderer = new MaterialToolStripRenderer();
                item.DropDown.Font = FontDefault;
                item.DropDown.BackColor = ToolbarBackground;
                item.DropDown.ForeColor = TextPrimary;
                ToolStripDismissHook.Register(item.DropDown);
            }
        }


        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, [MarshalAs(UnmanagedType.LPWStr)] string lParam);

        private const int EM_SETCUEBANNER = 0x1501;

        public static void SetCueBanner(TextBox textBox, string placeholder)
        {
            if (textBox == null) return;

            Action apply = () =>
            {
                if (textBox.IsHandleCreated)
                {
                    SendMessage(textBox.Handle, EM_SETCUEBANNER, 1, placeholder);
                }
            };

            textBox.HandleCreated += (s, e) => apply();
            if (textBox.IsHandleCreated)
            {
                apply();
            }
        }

        public static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0) return path;

            int d = radius * 2;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;

            if (d <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            return CreateRoundedPath(rect, radius);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            return CreateRoundedPath(rect, radius);
        }

        public static GraphicsPath CreateRoundedPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0f || rect.Height <= 0f) return path;

            float d = radius * 2f;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;

            if (d <= 0f)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static GraphicsPath CreateRoundedRectanglePathF(RectangleF rect, float radius)
        {
            return CreateRoundedPath(rect, radius);
        }

        public static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle rect, int radius)
        {
            if (g == null || brush == null || rect.Width <= 0 || rect.Height <= 0) return;
            using (GraphicsPath path = CreateRoundedPath(rect, radius))
            {
                SmoothingMode prev = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(brush, path);
                g.SmoothingMode = prev;
            }
        }

        public static void FillRoundedRectangle(Graphics g, Brush brush, RectangleF rect, float radius)
        {
            if (g == null || brush == null || rect.Width <= 0f || rect.Height <= 0f) return;
            using (GraphicsPath path = CreateRoundedPath(rect, radius))
            {
                SmoothingMode prev = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(brush, path);
                g.SmoothingMode = prev;
            }
        }

        public static void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle rect, int radius)
        {
            if (g == null || pen == null || rect.Width <= 0 || rect.Height <= 0) return;
            using (GraphicsPath path = CreateRoundedPath(rect, radius))
            {
                SmoothingMode prev = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawPath(pen, path);
                g.SmoothingMode = prev;
            }
        }

        public static void DrawRoundedRectangle(Graphics g, Pen pen, RectangleF rect, float radius)
        {
            if (g == null || pen == null || rect.Width <= 0f || rect.Height <= 0f) return;
            using (GraphicsPath path = CreateRoundedPath(rect, radius))
            {
                SmoothingMode prev = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawPath(pen, path);
                g.SmoothingMode = prev;
            }
        }

        public static void DrawCloseGlyph(Graphics g, Rectangle bounds, Color color, float penWidth = 1.4f)
        {
            if (g == null || bounds.Width <= 0 || bounds.Height <= 0) return;

            SmoothingMode prevMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int pad = Math.Max(2, Math.Min(bounds.Width, bounds.Height) / 4);
            int left = bounds.Left + pad;
            int top = bounds.Top + pad;
            int right = bounds.Right - pad;
            int bottom = bounds.Bottom - pad;

            using (Pen pen = new Pen(color, penWidth))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, left, top, right, bottom);
                g.DrawLine(pen, right, top, left, bottom);
            }

            g.SmoothingMode = prevMode;
        }
    }

    internal class DarkListViewHeaderSubclass : NativeWindow
    {
        private static readonly Dictionary<IntPtr, DarkListViewHeaderSubclass> _activeSubclasses =
            new Dictionary<IntPtr, DarkListViewHeaderSubclass>();

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_PAINT = 0x000F;
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_WINDOWPOSCHANGED = 0x0047;
        private const int WM_DESTROY = 0x0002;
        private const int LVM_GETHEADER = 0x101F;

        private readonly ListView _listView;

        public static void Attach(ListView listView)
        {
            if (listView == null || !listView.IsHandleCreated) return;
            try
            {
                IntPtr hHeader = SendMessage(listView.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                if (hHeader != IntPtr.Zero)
                {
                    try { UITheme.AllowDarkModeForWindow(hHeader, true); } catch { }
                    UITheme.SetWindowTheme(hHeader, "DarkMode_Explorer", null);

                    lock (_activeSubclasses)
                    {
                        DarkListViewHeaderSubclass existing;
                        if (_activeSubclasses.TryGetValue(hHeader, out existing))
                        {
                            if (existing.Handle == hHeader) return;
                            existing.ReleaseHandle();
                            _activeSubclasses.Remove(hHeader);
                        }
                        _activeSubclasses[hHeader] = new DarkListViewHeaderSubclass(listView, hHeader);
                    }
                }
            }
            catch { }
        }

        public static void InvalidateHeader(IntPtr hHeader)
        {
            if (hHeader != IntPtr.Zero)
            {
                try
                {
                    InvalidateRect(hHeader, IntPtr.Zero, true);
                }
                catch { }
            }
        }

        public DarkListViewHeaderSubclass(ListView listView, IntPtr headerHandle)
        {
            _listView = listView;
            AssignHandle(headerHandle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ERASEBKGND)
            {
                if (m.WParam != IntPtr.Zero)
                {
                    RECT rc;
                    if (GetClientRect(Handle, out rc))
                    {
                        using (Graphics g = Graphics.FromHdc(m.WParam))
                        {
                            using (SolidBrush b = new SolidBrush(MaterialColors.NavBarBackground))
                            {
                                g.FillRectangle(b, 0, 0, rc.Right, rc.Bottom);
                            }
                        }
                    }
                    m.Result = (IntPtr)1;
                    return;
                }
            }

            base.WndProc(ref m);

            if (m.Msg == WM_PAINT || m.Msg == WM_WINDOWPOSCHANGED)
            {
                PaintTrailingArea();
            }
            else if (m.Msg == WM_DESTROY)
            {
                lock (_activeSubclasses)
                {
                    _activeSubclasses.Remove(Handle);
                }
                ReleaseHandle();
            }
        }

        public void PaintTrailingArea()
        {
            if (Handle == IntPtr.Zero || _listView == null || _listView.IsDisposed) return;

            RECT rc;
            if (!GetClientRect(Handle, out rc)) return;

            int totalWidth = 0;
            try
            {
                foreach (ColumnHeader col in _listView.Columns)
                {
                    totalWidth += col.Width;
                }
            }
            catch
            {
                return;
            }

            if (totalWidth < rc.Right)
            {
                try
                {
                    using (Graphics g = Graphics.FromHwnd(Handle))
                    {
                        Rectangle fillRect = new Rectangle(totalWidth, 0, rc.Right - totalWidth, rc.Bottom);
                        using (SolidBrush b = new SolidBrush(MaterialColors.NavBarBackground))
                        {
                            g.FillRectangle(b, fillRect);
                        }
                        using (Pen p = new Pen(MaterialColors.Separator))
                        {
                            g.DrawLine(p, totalWidth, rc.Bottom - 1, rc.Right, rc.Bottom - 1);
                        }
                    }
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// Fluent and Material Dark theme design tokens for controls, surfaces, borders, and typography.
    /// </summary>
    public static class UIThemeTokens
    {
        public static readonly Color FormBackground = Color.FromArgb(32, 33, 36);        // #202124 (Header & Canvas)
        public static readonly Color CardBackground = Color.FromArgb(41, 42, 45);        // #292A2D (Card / Surface)
        public static readonly Color CardHover = Color.FromArgb(48, 50, 54);             // #303236
        public static readonly Color CardPressed = Color.FromArgb(56, 58, 62);           // #383A3E
        public static readonly Color BorderColor = Color.FromArgb(60, 64, 67);           // #3C4043 (Separators / Outline)
        public static readonly Color BorderHover = Color.FromArgb(90, 95, 100);
        public static readonly Color WindowBorder = Color.FromArgb(80, 84, 90);          // #50545A (Crisp outer window outline)
        public static readonly Color TextPrimary = Color.FromArgb(232, 234, 237);        // #E8EAED (Light text)
        public static readonly Color TextSecondary = Color.FromArgb(154, 160, 166);     // #9AA0A6 (Muted text)
        public static readonly Color TextDisabled = Color.FromArgb(95, 99, 104);         // #5F6368
        public static readonly Color InputBackground = Color.FromArgb(28, 29, 31);
        public static readonly Color StatusSuccess = Color.FromArgb(129, 201, 149);     // #81C995 (Green)

        public static readonly Font FontTitle = new Font("Segoe UI", 18f, FontStyle.Bold);
        public static readonly Font Header = new Font("Segoe UI", 11f, FontStyle.Bold);
        public static readonly Font FontHeader = new Font("Segoe UI", 11f, FontStyle.Bold);
        public static readonly Font FontDefault = new Font("Segoe UI", 9f, FontStyle.Regular);
        public static readonly Font FontDefaultBold = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        public static readonly Font FontSmall = new Font("Segoe UI", 8.25f, FontStyle.Regular);
    }

    /// <summary>
    /// Dynamically derives fluid two-tone speed gradients matching an accent color or dynamic random generation.
    /// </summary>
    public static class BrandTheme
    {
        public static Color AccentColor { get; set; }
        public static Color AccentTextColor { get; private set; }
        public static Color AccentHoverColor { get; private set; }
        public static Color AccentPressedColor { get; private set; }
        public static Color AccentGradientStart { get; private set; }
        public static Color AccentGradientEnd { get; private set; }

        static BrandTheme()
        {
            try
            {
                Color initColor = ThemeManager.CurrentAccentColor;
                if (!initColor.IsEmpty && initColor.A > 0)
                {
                    GenerateRandomAccent(initColor);
                }
                else
                {
                    GenerateRandomAccent();
                }
                ThemeManager.AccentColorChanged += (s, e) =>
                {
                    GenerateRandomAccent(ThemeManager.CurrentAccentColor);
                };
            }
            catch
            {
                GenerateRandomAccent();
            }
        }

        public static void GenerateRandomAccent(Color? explicitColor = null)
        {
            if (explicitColor.HasValue && !explicitColor.Value.IsEmpty && explicitColor.Value.A > 0)
            {
                AccentColor = explicitColor.Value;
            }
            else
            {
                Random rand = new Random(Guid.NewGuid().GetHashCode());
                double hue = rand.NextDouble() * 360.0;
                // Pastel range: moderate saturation (50% - 70%) and high lightness (72% - 82%) to avoid dark tones
                double saturation = 0.50 + (rand.NextDouble() * 0.20);
                double lightness = 0.72 + (rand.NextDouble() * 0.10);
                AccentColor = HslToRgb(hue, saturation, lightness);
            }

            // Derive harmonic kinetic speed gradient
            float h, s, l;
            RgbToHsl(AccentColor, out h, out s, out l);

            float hStart = (h - 22f + 360f) % 360f;
            float lStart = Math.Min(0.90f, l * 1.08f);
            float sStart = Math.Min(0.85f, s * 1.02f);

            float hEnd = (h + 22f) % 360f;
            float lEnd = Math.Max(0.55f, l * 0.90f);
            float sEnd = Math.Min(0.85f, s * 1.05f);

            AccentGradientStart = HslToRgb(hStart, sStart, lStart);
            AccentGradientEnd = HslToRgb(hEnd, sEnd, lEnd);

            float lHover = Math.Min(0.90f, l * 1.08f);
            float lPressed = Math.Max(0.55f, l * 0.88f);
            AccentHoverColor = HslToRgb(h, s, lHover);
            AccentPressedColor = HslToRgb(h, s, lPressed);

            // Compute high-contrast readable text color
            AccentTextColor = CalculateContrastingTextColor(AccentColor);
        }

        public static Color CalculateContrastingTextColor(Color bg)
        {
            if (bg.IsEmpty || bg.A == 0) return Color.White;

            // Standard relative luminance (ITU-R BT.709 / WCAG 2.1)
            double r = bg.R / 255.0;
            double g = bg.G / 255.0;
            double b = bg.B / 255.0;

            double rLin = (r <= 0.03928) ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
            double gLin = (g <= 0.03928) ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
            double bLin = (b <= 0.03928) ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

            double luminance = 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;

            // If background is light (> 0.20 luminance, mathematical crossover where contrast with charcoal exceeds white),
            // use crisp dark charcoal (#101114) for maximum readability. If dark/medium, use pure White (#FFFFFF).
            return luminance > 0.20 ? Color.FromArgb(16, 17, 20) : Color.White;
        }

        public static Color GetHoverColor(Color color)
        {
            float h, s, l;
            RgbToHsl(color, out h, out s, out l);
            float lHover = Math.Min(0.90f, l * 1.08f);
            return HslToRgb(h, s, lHover);
        }

        public static Color GetPressedColor(Color color)
        {
            float h, s, l;
            RgbToHsl(color, out h, out s, out l);
            float lPressed = Math.Max(0.55f, l * 0.88f);
            return HslToRgb(h, s, lPressed);
        }

        public static void RgbToHsl(Color rgb, out float h, out float s, out float l)
        {
            float r = rgb.R / 255f;
            float g = rgb.G / 255f;
            float b = rgb.B / 255f;

            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));

            l = (max + min) / 2f;

            if (max == min)
            {
                h = 0f;
                s = 0f;
            }
            else
            {
                float delta = max - min;
                s = (l <= 0.5f) ? (delta / (max + min)) : (delta / (2f - max - min));

                if (r == max)
                    h = ((g - b) / delta + (g < b ? 6f : 0f)) * 60f;
                else if (g == max)
                    h = ((b - r) / delta + 2f) * 60f;
                else
                    h = ((r - g) / delta + 4f) * 60f;
            }
        }

        public static Color HslToRgb(double h, double s, double l)
        {
            double r, g, b;
            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1.0 + s) : l + s - (l * s);
                double p = 2.0 * l - q;
                double hk = h / 360.0;
                r = HueToRgb(p, q, hk + 1.0 / 3.0);
                g = HueToRgb(p, q, hk);
                b = HueToRgb(p, q, hk - 1.0 / 3.0);
            }
            return Color.FromArgb(
                (int)Math.Max(0, Math.Min(255, Math.Round(r * 255))),
                (int)Math.Max(0, Math.Min(255, Math.Round(g * 255))),
                (int)Math.Max(0, Math.Min(255, Math.Round(b * 255)))
            );
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1.0;
            if (t > 1) t -= 1.0;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }
    }

    /// <summary>
    /// Backward-compatibility alias for BrandTheme.
    /// </summary>
    public static class WbBrandTheme
    {
        public static Color AccentColor { get { return BrandTheme.AccentColor; } }
        public static Color AccentTextColor { get { return BrandTheme.AccentTextColor; } }
        public static Color AccentHoverColor { get { return BrandTheme.AccentHoverColor; } }
        public static Color AccentPressedColor { get { return BrandTheme.AccentPressedColor; } }
        public static Color AccentGradientStart { get { return BrandTheme.AccentGradientStart; } }
        public static Color AccentGradientEnd { get { return BrandTheme.AccentGradientEnd; } }
        public static void GenerateRandomAccent(Color? explicitColor = null) { BrandTheme.GenerateRandomAccent(explicitColor); }
        public static Color CalculateContrastingTextColor(Color bg) { return BrandTheme.CalculateContrastingTextColor(bg); }
        public static Color GetHoverColor(Color color) { return BrandTheme.GetHoverColor(color); }
        public static Color GetPressedColor(Color color) { return BrandTheme.GetPressedColor(color); }
        public static void RgbToHsl(Color rgb, out float h, out float s, out float l) { BrandTheme.RgbToHsl(rgb, out h, out s, out l); }
        public static Color HslToRgb(double h, double s, double l) { return BrandTheme.HslToRgb(h, s, l); }
    }
}
