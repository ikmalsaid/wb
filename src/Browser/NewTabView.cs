using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using wb.Browser.Suggestions;
using wb.Core;
using wb.UI;

namespace wb.Browser
{
    /// <summary>
    /// Native WinForms internal New Tab page (wb://newtab) implementing Option 1 design:
    /// - Dynamic accent-adapted wb brand logo (via WbAppIcon)
    /// - Real-time digital clock and localized date
    /// - Centered search pill with interactive forward arrow submit button
    /// - 2x4 square customizable shortcuts grid with Add/Edit/Remove support
    /// </summary>
    public class NewTabView : UserControl
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        private readonly Action<string, bool> _openUrlCallback;
        private readonly Action _closeTabCallback;

        private TextBox _searchBox;
        private SuggestionDropdownPopup _suggestionPopup;
        private Timer _clockTimer;
        private ContextMenuStrip _shortcutContextMenu;
        private ContextMenuStrip _silhouetteContextMenu;
        private ShortcutItem _selectedContextMenuShortcut;

        private Rectangle _forwardArrowBounds;
        private bool _isForwardArrowHovered = false;
        private bool _isSearchPillHovered = false;
        private int _hoveredShortcutIndex = -1;
        private bool _isDeleteHovered = false;
        private bool _isSearchActive = false;

        private const int ContentMaxWidth = 620;
        private const int SearchPillHeight = 48;
        private const int BadgeDiameter = 52;
        private const int GridColumns = 4;
        private const int ColumnWidth = 110;
        private const int RowHeight = 94;

        // Cached layout positions
        private Rectangle _logoBounds;
        private Rectangle _clockBounds;
        private Rectangle _dateBounds;
        private Rectangle _searchPillBounds;
        private Point _gridOrigin;
        private Rectangle _silhouetteBounds;
        private readonly List<Rectangle> _shortcutBadgeBounds = new List<Rectangle>();
        private readonly List<Rectangle> _shortcutDeleteBounds = new List<Rectangle>();
        private readonly HashSet<string> _fetchingFaviconUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public NewTabView(Action<string, bool> openUrlCallback, Action closeTabCallback = null)
        {
            _openUrlCallback = openUrlCallback;
            _closeTabCallback = closeTabCallback;

            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);
            this.DoubleBuffered = true;
            this.TabStop = true;

            InitializeView();
        }

        private void InitializeView()
        {
            this.BackColor = Color.FromArgb(32, 33, 36);
            this.Margin = Padding.Empty;
            this.Padding = Padding.Empty;

            // Search Box (always visible; native cue banner provides consistent active/inactive alignment)
            _searchBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(41, 42, 45),
                ForeColor = Color.FromArgb(240, 246, 252),
                Font = new Font("Segoe UI", 11f, FontStyle.Regular),
                Visible = true,
                TabStop = false
            };
            _suggestionPopup = new SuggestionDropdownPopup();
            _suggestionPopup.Attach(
                _searchBox,
                () => _searchPillBounds,
                this,
                false,
                (target, newTab) =>
                {
                    if (_openUrlCallback != null)
                    {
                        _openUrlCallback(target, newTab);
                    }
                });
            _searchBox.KeyDown += OnSearchBoxKeyDown;
            _searchBox.TextChanged += (s, e) => { InvalidateForwardArrow(); };
            _searchBox.GotFocus += (s, e) =>
            {
                _isSearchActive = true;
                _searchBox.BackColor = Color.FromArgb(41, 42, 45);
                InvalidateSearchPill();
            };
            _searchBox.LostFocus += (s, e) =>
            {
                _isSearchActive = false;
                _searchBox.BackColor = _isSearchPillHovered ? Color.FromArgb(51, 54, 59) : Color.FromArgb(41, 42, 45);
                InvalidateSearchPill();
            };
            _searchBox.MouseEnter += (s, e) =>
            {
                if (!_isSearchPillHovered)
                {
                    _isSearchPillHovered = true;
                    UpdateSearchBoxHover();
                    InvalidateSearchPill();
                }
            };
            _searchBox.MouseLeave += (s, e) =>
            {
                Point clientPos = this.PointToClient(Cursor.Position);
                bool stillInPill = _searchPillBounds.Contains(clientPos) && !_forwardArrowBounds.Contains(clientPos);
                if (_isSearchPillHovered != stillInPill)
                {
                    _isSearchPillHovered = stillInPill;
                    UpdateSearchBoxHover();
                    InvalidateSearchPill();
                }
            };
            this.Controls.Add(_searchBox);
            UITheme.SetCueBanner(_searchBox, "Search the web or type a URL...");

            // Context Menu for shortcuts
            _shortcutContextMenu = new ContextMenuStrip();
            ToolStripMenuItem openTabItem = new ToolStripMenuItem(AppStrings.NewTab.ShortcutMenuOpenInNewTab, null, (s, e) =>
            {
                if (_selectedContextMenuShortcut != null && _openUrlCallback != null)
                {
                    _openUrlCallback(_selectedContextMenuShortcut.Url, true);
                }
            });
            ToolStripMenuItem editItem = new ToolStripMenuItem(AppStrings.NewTab.ShortcutMenuEdit, null, (s, e) =>
            {
                if (_selectedContextMenuShortcut != null)
                {
                    EditShortcut(_selectedContextMenuShortcut);
                }
            });
            ToolStripMenuItem removeItem = new ToolStripMenuItem(AppStrings.NewTab.ShortcutMenuRemove, null, (s, e) =>
            {
                if (_selectedContextMenuShortcut != null)
                {
                    AppServices.Shortcuts.Remove(_selectedContextMenuShortcut.Id);
                }
            });
            _shortcutContextMenu.Items.Add(openTabItem);
            _shortcutContextMenu.Items.Add(new ToolStripSeparator());
            _shortcutContextMenu.Items.Add(editItem);
            _shortcutContextMenu.Items.Add(removeItem);

            // Real-time Clock Timer
            _clockTimer = new Timer();
            _clockTimer.Interval = 1000;
            _clockTimer.Tick += (s, e) =>
            {
                if (_clockBounds.Width > 0)
                {
                    Invalidate(_clockBounds);
                    Invalidate(_dateBounds);
                }
            };
            _clockTimer.Start();

            // Subscriptions
            ThemeManager.AccentColorChanged += OnAccentColorChanged;
            AppServices.Shortcuts.ShortcutsChanged += OnShortcutsChanged;
            SilhouetteManager.SilhouetteChanged += OnSilhouetteChanged;

            PreloadShortcutFavicons();
        }

        private void PreloadShortcutFavicons()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    List<ShortcutItem> list = AppServices.Shortcuts.GetAll();
                    if (list == null || list.Count == 0) return;

                    bool anyLoaded = false;
                    foreach (var item in list)
                    {
                        if (item == null || string.IsNullOrEmpty(item.Url)) continue;
                        Image img = AppServices.Favicons.GetFavicon(item.Url);
                        if (img != null)
                        {
                            anyLoaded = true;
                        }
                    }

                    if (anyLoaded && !this.IsDisposed && this.IsHandleCreated)
                    {
                        try
                        {
                            if (this.InvokeRequired)
                            {
                                this.BeginInvoke(new Action(() => this.Invalidate()));
                            }
                            else
                            {
                                this.Invalidate();
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            });
        }

        private void TriggerShortcutFaviconFetch(string url)
        {
            if (string.IsNullOrEmpty(url) || this.IsDisposed || !this.IsHandleCreated) return;
            lock (_fetchingFaviconUrls)
            {
                if (_fetchingFaviconUrls.Contains(url)) return;
                _fetchingFaviconUrls.Add(url);
            }

            AppServices.Favicons.FetchFaviconAsync(url, null, img =>
            {
                if (img != null && !this.IsDisposed && this.IsHandleCreated)
                {
                    try
                    {
                        if (this.InvokeRequired)
                        {
                            this.BeginInvoke(new Action(() => this.Invalidate()));
                        }
                        else
                        {
                            this.Invalidate();
                        }
                    }
                    catch { }
                }
            });
        }

        private void OnAccentColorChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => Invalidate()));
                return;
            }
            Invalidate();
        }

        private void OnSilhouetteChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => Invalidate()));
                return;
            }
            Invalidate();
        }

        private void OnShortcutsChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            PreloadShortcutFavicons();
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => Invalidate()));
                return;
            }
            Invalidate();
        }

        public void FocusSearchBox()
        {
            _isSearchActive = true;
            if (_searchBox != null && !_searchBox.IsDisposed)
            {
                _searchBox.BackColor = Color.FromArgb(41, 42, 45);
                _searchBox.Focus();
                _searchBox.SelectAll();
            }
            InvalidateSearchPill();
        }

        public void UnfocusSearchBox()
        {
            _isSearchActive = false;
            if (_suggestionPopup != null)
            {
                _suggestionPopup.HidePopup();
            }
            if (_searchBox != null && !_searchBox.IsDisposed)
            {
                this.ActiveControl = null;
                ContainerControl container = this.Parent as ContainerControl ?? this.FindForm() as ContainerControl;
                if (container != null)
                {
                    container.ActiveControl = this;
                }
                this.ActiveControl = null;
            }
            UpdateSearchBoxHover();
            InvalidateSearchPill();
        }

        public void ResetSearchFocus()
        {
            UnfocusSearchBox();
        }

        private void UpdateSearchBoxHover()
        {
            if (_searchBox != null && !_searchBox.IsDisposed)
            {
                bool isFocused = _isSearchActive || _searchBox.Focused;
                if (!isFocused)
                {
                    _searchBox.BackColor = _isSearchPillHovered ? Color.FromArgb(51, 54, 59) : Color.FromArgb(41, 42, 45);
                }
                else
                {
                    _searchBox.BackColor = Color.FromArgb(41, 42, 45);
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ComputeLayout();
            if (_suggestionPopup != null)
            {
                _suggestionPopup.UpdatePosition();
            }
        }

        private void ComputeLayout()
        {
            int clientW = this.ClientSize.Width;
            int clientH = this.ClientSize.Height;
            if (clientW <= 0 || clientH <= 0) return;

            // Total vertical height of content block:
            // Logo (90) + gap (14) + Clock (48) + Date (20) + gap (22) + Search (48) + gap (32) + Shortcuts (188) = ~462
            int totalContentH = 462;
            int startY = Math.Max(24, (clientH - totalContentH) / 2);

            // 1. Logo
            int logoW = 180;
            int logoH = 92;
            int logoX = (clientW - logoW) / 2;
            _logoBounds = new Rectangle(logoX, startY, logoW, logoH);

            // 2. Clock & Date
            int clockY = _logoBounds.Bottom + 12;
            _clockBounds = new Rectangle(0, clockY, clientW, 46);
            _dateBounds = new Rectangle(0, clockY + 48, clientW, 22);

            // 3. Search Pill
            int pillW = Math.Min(ContentMaxWidth, Math.Max(320, clientW - 64));
            int pillX = (clientW - pillW) / 2;
            int pillY = _dateBounds.Bottom + 20;
            _searchPillBounds = new Rectangle(pillX, pillY, pillW, SearchPillHeight);

            // Search Box inside pill
            int sbLeft = pillX + 46;
            int sbRight = pillX + pillW - 46;
            int sbW = sbRight - sbLeft;
            int sbH = (_searchBox != null && _searchBox.PreferredHeight > 0) ? _searchBox.PreferredHeight : 22;
            int sbY = pillY + (SearchPillHeight - sbH) / 2;
            if (_searchBox != null)
            {
                _searchBox.SetBounds(sbLeft, sbY, sbW, sbH);
            }

            // Forward Arrow Button
            int faSize = 32;
            int faX = pillX + pillW - faSize - 8;
            int faY = pillY + (SearchPillHeight - faSize) / 2;
            _forwardArrowBounds = new Rectangle(faX, faY, faSize, faSize);

            // 4. Shortcuts Grid
            int gridW = GridColumns * ColumnWidth;
            int gridStartX = (clientW - gridW) / 2;
            int gridStartY = _searchPillBounds.Bottom + 30;
            _gridOrigin = new Point(gridStartX, gridStartY);

            _shortcutBadgeBounds.Clear();
            _shortcutDeleteBounds.Clear();

            for (int i = 0; i < 8; i++)
            {
                int row = i / GridColumns;
                int col = i % GridColumns;
                int cx = gridStartX + col * ColumnWidth + ColumnWidth / 2;
                int cy = gridStartY + row * RowHeight + BadgeDiameter / 2;

                Rectangle badgeRect = new Rectangle(cx - BadgeDiameter / 2, cy - BadgeDiameter / 2, BadgeDiameter, BadgeDiameter);
                _shortcutBadgeBounds.Add(badgeRect);

                Rectangle delRect = new Rectangle(badgeRect.Right - 10, badgeRect.Top - 6, 16, 16);
                _shortcutDeleteBounds.Add(delRect);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color accent = ThemeManager.CurrentAccentColor;

            // 0. Draw Anime 2B Silhouette Background (visible subtle companion watermark)
            DrawBackgroundSilhouette(g, accent);

            // 1. Draw wb Logo (Option 1: Kinetic harmonic gradient via WbAppIcon)
            if (_logoBounds.Width > 0 && _logoBounds.Height > 0)
            {
                using (Bitmap logo = WbAppIcon.GetAppBitmap(_logoBounds.Width, _logoBounds.Height, accent))
                {
                    if (logo != null)
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(logo, _logoBounds);
                    }
                }
            }

            // 2. Draw Digital Clock & Date
            DateTime now = DateTime.Now;
            string timeStr = now.ToString("h:mm tt");
            string dateStr = now.ToString("dddd, MMMM d, yyyy");

            using (Font clockFont = new Font("Segoe UI Light", 34f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush clockBrush = new SolidBrush(Color.FromArgb(240, 246, 252)))
            using (StringFormat sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(timeStr, clockFont, clockBrush, _clockBounds, sfCenter);
            }

            using (Font dateFont = new Font("Segoe UI", 13.5f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush dateBrush = new SolidBrush(Color.FromArgb(154, 160, 166)))
            using (StringFormat sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(dateStr, dateFont, dateBrush, _dateBounds, sfCenter);
            }

            // 3. Draw Search Container Pill
            if (_searchPillBounds.Width > 0)
            {
                DrawSearchPill(g, accent);
            }

            // 4. Draw 2x4 Shortcuts Grid
            DrawShortcutsGrid(g, accent);
        }

        private void DrawSearchPill(Graphics g, Color accent)
        {
            Rectangle r = _searchPillBounds;
            bool isFocused = _isSearchActive || (_searchBox != null && _searchBox.Focused);

            Color pillBg = isFocused
                ? Color.FromArgb(41, 42, 45)
                : (_isSearchPillHovered ? Color.FromArgb(51, 54, 59) : Color.FromArgb(41, 42, 45));

            using (GraphicsPath path = CreateRoundedRectanglePath(r, r.Height / 2))
            {
                // Background fill
                using (SolidBrush bgBrush = new SolidBrush(pillBg))
                {
                    g.FillPath(bgBrush, path);
                }
            }

            // Inset stroke path by half the pen width so all 4 borders (top, bottom, left, right)
            // render with identical stroke thickness and stay 100% inside the bounds and clipping rectangle.
            Color borderColor;
            float borderWidth;
            if (isFocused)
            {
                borderColor = accent;
                borderWidth = 1.8f;
            }
            else if (_isSearchPillHovered)
            {
                borderColor = Color.FromArgb(95, 99, 104);
                borderWidth = 1.2f;
            }
            else
            {
                borderColor = Color.FromArgb(60, 64, 67);
                borderWidth = 1f;
            }

            float inset = borderWidth / 2f;
            RectangleF strokeRect = new RectangleF(r.X + inset, r.Y + inset, r.Width - borderWidth, r.Height - borderWidth);
            float strokeRadius = strokeRect.Height / 2f;

            using (GraphicsPath strokePath = CreateRoundedRectanglePathF(strokeRect, strokeRadius))
            using (Pen borderPen = new Pen(borderColor, borderWidth))
            {
                g.DrawPath(borderPen, strokePath);
            }

            // Left Search Icon (Magnifying Glass)
            int iconX = r.Left + 18;
            int iconY = r.Top + (r.Height - 16) / 2;
            Color iconColor = _isSearchPillHovered || isFocused ? Color.FromArgb(200, 205, 210) : Color.FromArgb(154, 160, 166);
            using (Pen iconPen = new Pen(iconColor, 1.8f))
            {
                g.DrawEllipse(iconPen, iconX, iconY, 11, 11);
                g.DrawLine(iconPen, iconX + 9, iconY + 9, iconX + 15, iconY + 15);
            }

            // Fallback placeholder rendering ONLY if _searchBox is null or invisible
            if ((_searchBox == null || !_searchBox.Visible) && _searchPillBounds.Width > 0)
            {
                using (Font hintFont = new Font("Segoe UI", 11f, FontStyle.Regular))
                using (SolidBrush hintBrush = new SolidBrush(Color.FromArgb(154, 160, 166)))
                {
                    int sbLeft = r.X + 46;
                    int sbRight = r.X + r.Width - 46;
                    int sbW = sbRight - sbLeft;
                    int sbH = 22;
                    int sbY = r.Y + (SearchPillHeight - sbH) / 2;
                    Rectangle hintRect = new Rectangle(sbLeft, sbY, sbW, sbH);
                    using (StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Center })
                    {
                        g.DrawString(AppStrings.NewTab.SearchHintText, hintFont, hintBrush, hintRect, sf);
                    }
                }
            }

            // Right Forward Arrow Button (Circular)
            Rectangle fa = _forwardArrowBounds;
            bool hasText = _searchBox != null && !string.IsNullOrEmpty(_searchBox.Text.Trim());
            bool isActive = hasText || _isForwardArrowHovered;

            Color faBg = isActive ? accent : Color.FromArgb(51, 54, 59);
            Color faArrow = isActive ? Color.FromArgb(20, 20, 20) : Color.FromArgb(154, 160, 166);

            using (SolidBrush faBrush = new SolidBrush(faBg))
            {
                g.FillEllipse(faBrush, fa);
            }

            if (_isForwardArrowHovered && isActive)
            {
                using (Pen haloPen = new Pen(Color.FromArgb(90, accent.R, accent.G, accent.B), 2.5f))
                {
                    Rectangle haloRect = new Rectangle(fa.X - 1, fa.Y - 1, fa.Width + 2, fa.Height + 2);
                    g.DrawEllipse(haloPen, haloRect);
                }
            }

            // Arrow symbol (→)
            int acx = fa.X + fa.Width / 2;
            int acy = fa.Y + fa.Height / 2;
            using (Pen arrowPen = new Pen(faArrow, 2f))
            {
                arrowPen.StartCap = LineCap.Round;
                arrowPen.EndCap = LineCap.Round;
                g.DrawLine(arrowPen, acx - 6, acy, acx + 6, acy);
                g.DrawLine(arrowPen, acx + 1, acy - 5, acx + 6, acy);
                g.DrawLine(arrowPen, acx + 1, acy + 5, acx + 6, acy);
            }
        }

        private void DrawShortcutsGrid(Graphics g, Color accent)
        {
            List<ShortcutItem> list = AppServices.Shortcuts.GetAll();
            using (Font titleFont = new Font("Segoe UI", 9f, FontStyle.Regular))
            using (Font badgeFont = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (StringFormat sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int i = 0; i < 8; i++)
                {
                    if (i >= _shortcutBadgeBounds.Count) break;
                    Rectangle badgeRect = _shortcutBadgeBounds[i];
                    bool isHovered = (_hoveredShortcutIndex == i);

                    if (i < list.Count)
                    {
                        // Site Shortcut Badge
                        ShortcutItem item = list[i];
                        Color badgeColor = item.ColorRgb != 0 ? Color.FromArgb(item.ColorRgb) : accent;

                        // Background Square (subtle 8px rounded corners for sleek modern finish)
                        using (GraphicsPath bPath = CreateRoundedRectanglePath(badgeRect, 8))
                        {
                            using (SolidBrush bBrush = new SolidBrush(Color.FromArgb(41, 42, 45)))
                            {
                                g.FillPath(bBrush, bPath);
                            }

                            // Outline: Accent on hover
                            Color borderCol = isHovered ? accent : Color.FromArgb(60, 64, 67);
                            float borderW = isHovered ? 2f : 1f;
                            using (Pen bPen = new Pen(borderCol, borderW))
                            {
                                g.DrawPath(bPen, bPath);
                            }
                        }

                        // Site Favicon (centered 28x28 inside 52x52 badge) or initials fallback
                        Image fav = AppServices.Favicons.GetFavicon(item.Url);
                        if (fav != null)
                        {
                            int favSize = 28;
                            int fx = badgeRect.X + (badgeRect.Width - favSize) / 2;
                            int fy = badgeRect.Y + (badgeRect.Height - favSize) / 2;
                            Rectangle favRect = new Rectangle(fx, fy, favSize, favSize);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(fav, favRect);
                        }
                        else
                        {
                            // Initials badge text fallback
                            string badgeText = !string.IsNullOrEmpty(item.BadgeText) ? item.BadgeText : "?";
                            using (SolidBrush textBrush = new SolidBrush(badgeColor))
                            {
                                g.DrawString(badgeText, badgeFont, textBrush, badgeRect, sfCenter);
                            }

                            // Trigger background fetch if not already requested
                            TriggerShortcutFaviconFetch(item.Url);
                        }

                        // Title underneath
                        Rectangle titleRect = new Rectangle(badgeRect.Left - 22, badgeRect.Bottom + 6, badgeRect.Width + 44, 20);
                        Color titleCol = isHovered ? Color.FromArgb(255, 255, 255) : Color.FromArgb(232, 234, 237);
                        using (SolidBrush tBrush = new SolidBrush(titleCol))
                        using (StringFormat sfTitle = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                        {
                            g.DrawString(item.Title, titleFont, tBrush, titleRect, sfTitle);
                        }

                        // Delete button (✕) when hovered
                        if (isHovered && i < _shortcutDeleteBounds.Count)
                        {
                            Rectangle delRect = _shortcutDeleteBounds[i];
                            Color delBg = _isDeleteHovered ? Color.FromArgb(217, 48, 37) : Color.FromArgb(51, 54, 59);
                            using (SolidBrush delBrush = new SolidBrush(delBg))
                            {
                                g.FillEllipse(delBrush, delRect);
                            }
                            using (Pen xPen = new Pen(Color.White, 1.4f))
                            {
                                int dcx = delRect.X + delRect.Width / 2;
                                int dcy = delRect.Y + delRect.Height / 2;
                                g.DrawLine(xPen, dcx - 3, dcy - 3, dcx + 3, dcy + 3);
                                g.DrawLine(xPen, dcx + 3, dcy - 3, dcx - 3, dcy + 3);
                            }
                        }
                    }
                    else
                    {
                        // "Add Shortcut" Slot (Dashed square badge with '+' sign for every empty slot up to 8)
                        Color addColor = isHovered ? accent : Color.FromArgb(154, 160, 166);
                        using (GraphicsPath bPath = CreateRoundedRectanglePath(badgeRect, 8))
                        {
                            using (SolidBrush bBrush = new SolidBrush(Color.FromArgb(41, 42, 45)))
                            {
                                g.FillPath(bBrush, bPath);
                            }

                            using (Pen dashPen = new Pen(addColor, 1.8f))
                            {
                                dashPen.DashStyle = DashStyle.Dash;
                                g.DrawPath(dashPen, bPath);
                            }
                        }

                        // Plus sign (+)
                        int pcx = badgeRect.X + badgeRect.Width / 2;
                        int pcy = badgeRect.Y + badgeRect.Height / 2;
                        using (Pen plusPen = new Pen(addColor, 2f))
                        {
                            plusPen.StartCap = LineCap.Round;
                            plusPen.EndCap = LineCap.Round;
                            g.DrawLine(plusPen, pcx - 7, pcy, pcx + 7, pcy);
                            g.DrawLine(plusPen, pcx, pcy - 7, pcx, pcy + 7);
                        }

                        // "Add Shortcut" text underneath
                        Rectangle titleRect = new Rectangle(badgeRect.Left - 20, badgeRect.Bottom + 6, badgeRect.Width + 40, 20);
                        using (SolidBrush tBrush = new SolidBrush(addColor))
                        {
                            g.DrawString(AppStrings.NewTab.AddShortcutLabel, titleFont, tBrush, titleRect, sfCenter);
                        }
                    }
                }
            }
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            return UITheme.CreateRoundedPath(rect, radius);
        }

        private static GraphicsPath CreateRoundedRectanglePathF(RectangleF rect, float radius)
        {
            return UITheme.CreateRoundedPath(rect, radius);
        }

        private void InvalidateSearchPill()
        {
            if (_searchPillBounds.Width > 0 && _searchPillBounds.Height > 0)
            {
                Rectangle inv = _searchPillBounds;
                inv.Inflate(4, 4);
                Invalidate(inv);
            }
        }

        private void InvalidateForwardArrow()
        {
            if (_forwardArrowBounds.Width > 0)
            {
                Rectangle r = _forwardArrowBounds;
                r.Inflate(4, 4);
                Invalidate(r);
            }
        }

        private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled || e.SuppressKeyPress) return;

            if (_suggestionPopup != null && _suggestionPopup.HandleKeyDown(e))
            {
                return;
            }

            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (_suggestionPopup != null)
                {
                    _suggestionPopup.SuppressSuggestions();
                }
                ExecuteSearchOrNavigate(_searchBox.Text);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                if (!string.IsNullOrEmpty(_searchBox.Text))
                {
                    _searchBox.Text = string.Empty;
                }
                UnfocusSearchBox();
            }
        }

        private void ExecuteSearchOrNavigate(string input)
        {
            if (string.IsNullOrEmpty(input)) return;
            string targetUrl = UrlHelper.ResolveNavigableUrl(input);
            if (string.IsNullOrEmpty(targetUrl)) return;

            if (_openUrlCallback != null)
            {
                _openUrlCallback(targetUrl, false);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (_searchPillBounds.Contains(e.Location))
            {
                if (e.Button == MouseButtons.Left && !_forwardArrowBounds.Contains(e.Location))
                {
                    if (_searchBox != null)
                    {
                        _isSearchActive = true;
                        _searchBox.Visible = true;
                        _searchBox.BackColor = Color.FromArgb(41, 42, 45);
                        _searchBox.Focus();
                        _searchBox.SelectAll();
                        InvalidateSearchPill();
                    }
                }
            }
            else
            {
                // Click outside search pill: deactivate search box (return to inactive resting state)
                UnfocusSearchBox();
            }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (!char.IsControl(e.KeyChar) && _searchBox != null)
            {
                _isSearchActive = true;
                _searchBox.Focus();
                _searchBox.Text += e.KeyChar.ToString();
                _searchBox.SelectionStart = _searchBox.Text.Length;
                _searchBox.SelectionLength = 0;
                e.Handled = true;
                InvalidateSearchPill();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                UnfocusSearchBox();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // Search pill hover test
            bool prevPillHover = _isSearchPillHovered;
            _isSearchPillHovered = _searchPillBounds.Contains(e.Location) && !_forwardArrowBounds.Contains(e.Location);
            if (prevPillHover != _isSearchPillHovered)
            {
                UpdateSearchBoxHover();
                InvalidateSearchPill();
            }

            // Forward Arrow hover test
            bool prevFaHover = _isForwardArrowHovered;
            _isForwardArrowHovered = _forwardArrowBounds.Contains(e.Location);
            if (prevFaHover != _isForwardArrowHovered)
            {
                InvalidateForwardArrow();
            }

            // Shortcuts hover test
            int prevHovered = _hoveredShortcutIndex;
            bool prevDelHover = _isDeleteHovered;

            _hoveredShortcutIndex = -1;
            _isDeleteHovered = false;

            List<ShortcutItem> list = AppServices.Shortcuts.GetAll();
            for (int i = 0; i < 8; i++)
            {
                if (i >= _shortcutBadgeBounds.Count) break;
                Rectangle bRect = _shortcutBadgeBounds[i];
                Rectangle totalHitRect = new Rectangle(bRect.Left - 16, bRect.Top - 4, bRect.Width + 32, bRect.Height + 34);

                if (totalHitRect.Contains(e.Location))
                {
                    _hoveredShortcutIndex = i;
                    if (i < list.Count && i < _shortcutDeleteBounds.Count && _shortcutDeleteBounds[i].Contains(e.Location))
                    {
                        _isDeleteHovered = true;
                    }
                    break;
                }
            }

            if (prevHovered != _hoveredShortcutIndex || prevDelHover != _isDeleteHovered)
            {
                Invalidate();
            }

            // Cursor update
            if (_isForwardArrowHovered || _hoveredShortcutIndex >= 0)
            {
                this.Cursor = Cursors.Hand;
            }
            else if (_isSearchPillHovered)
            {
                this.Cursor = Cursors.IBeam;
            }
            else
            {
                this.Cursor = Cursors.Default;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            bool needInvalidate = false;
            if (_isForwardArrowHovered || _hoveredShortcutIndex >= 0)
            {
                _isForwardArrowHovered = false;
                _hoveredShortcutIndex = -1;
                _isDeleteHovered = false;
                needInvalidate = true;
            }
            if (_isSearchPillHovered)
            {
                _isSearchPillHovered = false;
                UpdateSearchBoxHover();
                needInvalidate = true;
            }
            this.Cursor = Cursors.Default;
            if (needInvalidate)
            {
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            // 1. Forward Arrow click
            if (_forwardArrowBounds.Contains(e.Location) && e.Button == MouseButtons.Left)
            {
                ExecuteSearchOrNavigate(_searchBox.Text);
                return;
            }

            // 2. Shortcuts click
            List<ShortcutItem> list = AppServices.Shortcuts.GetAll();
            for (int i = 0; i < 8; i++)
            {
                if (i >= _shortcutBadgeBounds.Count) break;
                Rectangle bRect = _shortcutBadgeBounds[i];
                Rectangle totalHitRect = new Rectangle(bRect.Left - 16, bRect.Top - 4, bRect.Width + 32, bRect.Height + 34);

                if (totalHitRect.Contains(e.Location))
                {
                    if (i < list.Count)
                    {
                        ShortcutItem item = list[i];

                        // Delete button click
                        if (i < _shortcutDeleteBounds.Count && _shortcutDeleteBounds[i].Contains(e.Location) && e.Button == MouseButtons.Left)
                        {
                            AppServices.Shortcuts.Remove(item.Id);
                            return;
                        }

                        // Right-click context menu
                        if (e.Button == MouseButtons.Right)
                        {
                            _selectedContextMenuShortcut = item;
                            _shortcutContextMenu.Show(this, e.Location);
                            return;
                        }

                        // Normal navigation click (clicking shortcut link from internal page opens in new tab)
                        bool openNewTab = true;
                        if (_openUrlCallback != null)
                        {
                            _openUrlCallback(item.Url, openNewTab);
                        }
                        if (_closeTabCallback != null)
                        {
                            _closeTabCallback();
                        }
                        return;
                    }
                    else
                    {
                        // "Add Shortcut" slot click (any of the '+' slots up to 8)
                        if (e.Button == MouseButtons.Left)
                        {
                            PromptAddShortcut();
                        }
                        return;
                    }
                }
            }

            // Right-click handling:
            if (e.Button == MouseButtons.Right)
            {
                bool isBlank = SilhouetteManager.IsHidden || SilhouetteManager.GetActiveSilhouetteBitmap() == null;

                if (isBlank)
                {
                    // Blank bg: right-clicking anywhere on empty canvas (outside search pill) opens "Show background"
                    if (!_searchPillBounds.Contains(e.Location))
                    {
                        ShowSilhouetteContextMenu(e.Location);
                        return;
                    }
                }
                else
                {
                    // Not blank: only clickable on the image itself, not around the area like blank bg
                    if (IsClickOnSilhouetteImage(e.Location))
                    {
                        ShowSilhouetteContextMenu(e.Location);
                        return;
                    }
                }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                if (e.Delta > 0)
                {
                    SilhouetteManager.AdjustZoom(5);
                }
                else if (e.Delta < 0)
                {
                    SilhouetteManager.AdjustZoom(-5);
                }
            }
        }

        private void PromptAddShortcut()
        {
            using (AddShortcutDialog dlg = new AddShortcutDialog())
            {
                if (dlg.ShowDialog(this.FindForm()) == DialogResult.OK)
                {
                    AppServices.Shortcuts.Add(dlg.ShortcutTitle, dlg.ShortcutUrl);
                }
            }
        }

        private void EditShortcut(ShortcutItem item)
        {
            using (AddShortcutDialog dlg = new AddShortcutDialog(item.Title, item.Url))
            {
                if (dlg.ShowDialog(this.FindForm()) == DialogResult.OK)
                {
                    AppServices.Shortcuts.Update(item.Id, dlg.ShortcutTitle, dlg.ShortcutUrl);
                }
            }
        }

        private bool IsClickOnSilhouetteImage(Point loc)
        {
            if (_silhouetteBounds.Width <= 0 || _silhouetteBounds.Height <= 0) return false;
            return _silhouetteBounds.Contains(loc);
        }

        private void ShowSilhouetteContextMenu(Point location)
        {
            if (_silhouetteContextMenu != null)
            {
                _silhouetteContextMenu.Dispose();
                _silhouetteContextMenu = null;
            }

            _silhouetteContextMenu = new ContextMenuStrip();
            UITheme.ApplyContextMenu(_silhouetteContextMenu);

            bool isBlank = SilhouetteManager.IsHidden || SilhouetteManager.GetActiveSilhouetteBitmap() == null;

            if (isBlank)
            {
                // Only show the menu item "Show background" if the bg is blank
                ToolStripMenuItem showItem = new ToolStripMenuItem(AppStrings.NewTab.BgMenuShowBackground);
                showItem.Click += (s, e) =>
                {
                    SilhouetteManager.SetHidden(false);
                };
                _silhouetteContextMenu.Items.Add(showItem);
                _silhouetteContextMenu.Show(this, location);
                return;
            }

            // If not blank, then show other menus including hide
            ToolStripMenuItem hideItem = new ToolStripMenuItem(AppStrings.NewTab.BgMenuHide);
            hideItem.Click += (s, e) =>
            {
                SilhouetteManager.SetHidden(true);
            };
            _silhouetteContextMenu.Items.Add(hideItem);
            _silhouetteContextMenu.Items.Add(new ToolStripSeparator());

            // 1. Randomize
            ToolStripMenuItem randItem = new ToolStripMenuItem(AppStrings.NewTab.BgMenuRandomize);
            randItem.Checked = SilhouetteManager.IsRandomMode;
            randItem.Click += (s, e) =>
            {
                SilhouetteManager.SetRandomMode(!SilhouetteManager.IsRandomMode);
            };
            _silhouetteContextMenu.Items.Add(randItem);

            // 2. Toggle Color Style
            bool isWallpaper = SilhouetteManager.IsActiveWallpaper;
            bool isWhite = string.Equals(SilhouetteManager.ColorStyle, "White", StringComparison.OrdinalIgnoreCase);
            string colorLabel = AppStrings.NewTab.BgMenuToggleColorPrefix + (isWallpaper ? (isWhite ? AppStrings.NewTab.BgMenuWhiteGrayscale : AppStrings.NewTab.BgMenuFullColor) : (isWhite ? AppStrings.NewTab.BgMenuPureWhite : AppStrings.NewTab.BgMenuAccent));
            ToolStripMenuItem colorItem = new ToolStripMenuItem(colorLabel);
            colorItem.Click += (s, e) =>
            {
                SilhouetteManager.ToggleColorStyle();
            };
            _silhouetteContextMenu.Items.Add(colorItem);

            // 3. Fit Mode: Fill Space (Wallpaper) vs Corner (Silhouette)
            bool isBundled = SilhouetteManager.IsBundledSilhouette(SilhouetteManager.ActiveFilePath);
            ToolStripMenuItem fitItem = new ToolStripMenuItem(isWallpaper ? AppStrings.NewTab.BgMenuFitFill : AppStrings.NewTab.BgMenuFitCorner);
            fitItem.Checked = isWallpaper;
            if (isBundled)
            {
                fitItem.Enabled = false;
            }
            fitItem.Click += (s, e) =>
            {
                SilhouetteManager.ToggleDisplayMode();
            };
            _silhouetteContextMenu.Items.Add(fitItem);

            // 4. Zoom (XX%) Submenu
            string zoomLabel = string.Format(AppStrings.NewTab.BgMenuZoomFormat, SilhouetteManager.ZoomPercent);
            ToolStripMenuItem zoomParent = new ToolStripMenuItem(zoomLabel);
            int[] zoomLevels = new int[] { 125, 100, 85, 70, 50 };
            for (int i = 0; i < zoomLevels.Length; i++)
            {
                int level = zoomLevels[i];
                string zText = (level == 85) ? AppStrings.NewTab.BgMenuZoomDefault : (level.ToString() + "%");
                ToolStripMenuItem zItem = new ToolStripMenuItem(zText);
                zItem.Checked = (SilhouetteManager.ZoomPercent == level);
                zItem.Click += (s, e) =>
                {
                    SilhouetteManager.SetZoomPercent(level);
                };
                zoomParent.DropDownItems.Add(zItem);
            }
            _silhouetteContextMenu.Items.Add(zoomParent);

            // 5. Position Submenu (Left, Center, Right)
            ToolStripMenuItem posParent = new ToolStripMenuItem(AppStrings.NewTab.BgMenuPosition);
            string[] posOptions = new string[] { AppStrings.NewTab.BgMenuPosLeft, AppStrings.NewTab.BgMenuPosCenter, AppStrings.NewTab.BgMenuPosRight };
            for (int i = 0; i < posOptions.Length; i++)
            {
                string pos = posOptions[i];
                string pText = (pos == AppStrings.NewTab.BgMenuPosRight) ? AppStrings.NewTab.BgMenuPosRightDefault : pos;
                ToolStripMenuItem pItem = new ToolStripMenuItem(pText);
                pItem.Checked = string.Equals(SilhouetteManager.Alignment, pos, StringComparison.OrdinalIgnoreCase);
                pItem.Click += (s, e) =>
                {
                    SilhouetteManager.SetAlignment(pos);
                };
                posParent.DropDownItems.Add(pItem);
            }
            _silhouetteContextMenu.Items.Add(posParent);

            // 6. Mirror (Horizontal Flip)
            ToolStripMenuItem mirrorItem = new ToolStripMenuItem(AppStrings.NewTab.BgMenuMirror);
            mirrorItem.Checked = SilhouetteManager.IsMirrored;
            mirrorItem.Click += (s, e) =>
            {
                SilhouetteManager.ToggleMirror();
            };
            _silhouetteContextMenu.Items.Add(mirrorItem);

            // 7. Flip (Vertical Flip)
            ToolStripMenuItem flipItem = new ToolStripMenuItem(AppStrings.NewTab.BgMenuFlip);
            flipItem.Checked = SilhouetteManager.IsFlipped;
            flipItem.Click += (s, e) =>
            {
                SilhouetteManager.ToggleFlip();
            };
            _silhouetteContextMenu.Items.Add(flipItem);

            _silhouetteContextMenu.Items.Add(new ToolStripSeparator());

            // 8. Flat list of all detected silhouettes in assets/silhouettes and custom/
            IList<string> files = SilhouetteManager.GetAvailableFiles();
            for (int i = 0; i < files.Count; i++)
            {
                string filePath = files[i];
                string name = Path.GetFileNameWithoutExtension(filePath);
                ToolStripMenuItem fItem = new ToolStripMenuItem(name);
                fItem.Checked = string.Equals(SilhouetteManager.ActiveFilePath, filePath, StringComparison.OrdinalIgnoreCase);
                fItem.Click += (s, e) =>
                {
                    SilhouetteManager.SelectSilhouette(filePath);
                };
                _silhouetteContextMenu.Items.Add(fItem);
            }

            _silhouetteContextMenu.Items.Add(new ToolStripSeparator());

            // 9. Rescan files
            ToolStripMenuItem rescanItem = new ToolStripMenuItem(AppStrings.NewTab.BgMenuRescan);
            rescanItem.Click += (s, e) =>
            {
                SilhouetteManager.Rescan();
            };
            _silhouetteContextMenu.Items.Add(rescanItem);

            // 10. Browse custom image...
            ToolStripMenuItem browseItem = new ToolStripMenuItem(AppStrings.NewTab.BgMenuBrowse);
            browseItem.Click += (s, e) =>
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Title = AppStrings.NewTab.SelectCustomBgTitle;
                    dlg.Filter = AppStrings.NewTab.BgImageFilter;
                    if (dlg.ShowDialog(this.FindForm()) == DialogResult.OK)
                    {
                        SilhouetteManager.ImportCustomImage(dlg.FileName);
                    }
                }
            };
            _silhouetteContextMenu.Items.Add(browseItem);

            // 11. Open folder
            ToolStripMenuItem openFolderItem = new ToolStripMenuItem(AppStrings.NewTab.BgMenuOpenFolder);
            openFolderItem.Click += (s, e) =>
            {
                try
                {
                    string dir = SilhouetteManager.CustomDirectory;
                    if (!Directory.Exists(dir))
                    {
                        dir = SilhouetteManager.SilhouettesDirectory;
                    }
                    if (Directory.Exists(dir))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", dir);
                    }
                }
                catch { }
            };
            _silhouetteContextMenu.Items.Add(openFolderItem);

            _silhouetteContextMenu.Show(this, location);
        }

        private void DrawBackgroundSilhouette(Graphics g, Color accent)
        {
            if (SilhouetteManager.IsHidden)
            {
                _silhouetteBounds = Rectangle.Empty;
                return;
            }

            Bitmap silhouette = SilhouetteManager.GetActiveSilhouetteBitmap();
            if (silhouette == null)
            {
                _silhouetteBounds = Rectangle.Empty;
                return;
            }

            int clientW = this.ClientSize.Width;
            int clientH = this.ClientSize.Height;
            if (clientW <= 0 || clientH <= 0) return;

            if (SilhouetteManager.IsActiveWallpaper)
            {
                // Wallpaper Cover Mode: fills the entire space preserving aspect ratio
                float scale = Math.Max((float)clientW / silhouette.Width, (float)clientH / silhouette.Height);
                int targetW = (int)Math.Ceiling(silhouette.Width * scale);
                int targetH = (int)Math.Ceiling(silhouette.Height * scale);
                int posX = (clientW - targetW) / 2;
                int posY = (clientH - targetH) / 2;

                Rectangle destRect = new Rectangle(posX, posY, targetW, targetH);
                _silhouetteBounds = new Rectangle(0, 0, clientW, clientH);

                bool isWhiteMode = string.Equals(SilhouetteManager.ColorStyle, "White", StringComparison.OrdinalIgnoreCase);

                if (isWhiteMode)
                {
                    // Grayscale / pure silver-white monochrome at 35% alpha
                    float[][] matrixItems = new float[][]
                    {
                        new float[] { 0.299f * 0.35f, 0.299f * 0.35f, 0.299f * 0.35f, 0, 0 },
                        new float[] { 0.587f * 0.35f, 0.587f * 0.35f, 0.587f * 0.35f, 0, 0 },
                        new float[] { 0.114f * 0.35f, 0.114f * 0.35f, 0.114f * 0.35f, 0, 0 },
                        new float[] { 0, 0, 0, 0.35f, 0 },
                        new float[] { 0, 0, 0, 0, 1 }
                    };
                    using (ImageAttributes imageAttributes = new ImageAttributes())
                    {
                        imageAttributes.SetColorMatrix(new ColorMatrix(matrixItems), ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                        InterpolationMode prevMode = g.InterpolationMode;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(silhouette, destRect, 0, 0, silhouette.Width, silhouette.Height, GraphicsUnit.Pixel, imageAttributes);
                        g.InterpolationMode = prevMode;
                    }
                }
                else
                {
                    // Full rich color with 55% opacity over the dark background
                    float alphaScale = 0.55f;
                    float[][] matrixItems = new float[][]
                    {
                        new float[] { 1f, 0, 0, 0, 0 },
                        new float[] { 0, 1f, 0, 0, 0 },
                        new float[] { 0, 0, 1f, 0, 0 },
                        new float[] { 0, 0, 0, alphaScale, 0 },
                        new float[] { 0, 0, 0, 0, 1 }
                    };
                    using (ImageAttributes imageAttributes = new ImageAttributes())
                    {
                        imageAttributes.SetColorMatrix(new ColorMatrix(matrixItems), ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                        InterpolationMode prevMode = g.InterpolationMode;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(silhouette, destRect, 0, 0, silhouette.Width, silhouette.Height, GraphicsUnit.Pixel, imageAttributes);
                        g.InterpolationMode = prevMode;
                    }

                    // Gentle subtle dark gradient overlay for optimal readability of search pill and shortcuts
                    using (LinearGradientBrush scrim = new LinearGradientBrush(
                        new Point(0, 0),
                        new Point(0, clientH),
                        Color.FromArgb(40, 15, 17, 21),
                        Color.FromArgb(140, 15, 17, 21)))
                    {
                        g.FillRectangle(scrim, 0, 0, clientW, clientH);
                    }
                }
                return;
            }

            // Target Height based on SilhouetteManager.ZoomPercent
            float zoomRatio = SilhouetteManager.ZoomPercent / 100.0f;
            int targetHNormal = Math.Max(100, (int)(clientH * zoomRatio));
            int targetWNormal = (int)(targetHNormal * ((float)silhouette.Width / silhouette.Height));

            // Position based on alignment (Left, Center, Right)
            int posXNormal;
            string align = SilhouetteManager.Alignment;
            if (string.Equals(align, "Left", StringComparison.OrdinalIgnoreCase))
            {
                posXNormal = 0;
            }
            else if (string.Equals(align, "Center", StringComparison.OrdinalIgnoreCase))
            {
                posXNormal = (clientW - targetWNormal) / 2;
            }
            else
            {
                // Default: Right
                posXNormal = clientW - targetWNormal;
            }
            int posYNormal = clientH - targetHNormal;

            Rectangle destRectNormal = new Rectangle(posXNormal, posYNormal, targetWNormal, targetHNormal);
            _silhouetteBounds = destRectNormal;

            // ColorStyle: "White" (pure silver-white at 30% alpha) vs "Accent" (dynamic accent hologram at 36% alpha)
            bool isWhite = string.Equals(SilhouetteManager.ColorStyle, "White", StringComparison.OrdinalIgnoreCase);
            float alphaScaleNormal = isWhite ? 0.30f : 0.36f;
            float r = isWhite ? alphaScaleNormal : (accent.R / 255f) * alphaScaleNormal;
            float gVal = isWhite ? alphaScaleNormal : (accent.G / 255f) * alphaScaleNormal;
            float b = isWhite ? alphaScaleNormal : (accent.B / 255f) * alphaScaleNormal;

            float[][] matrixItemsNormal = new float[][]
            {
                new float[] { r, 0, 0, 0, 0 },
                new float[] { 0, gVal, 0, 0, 0 },
                new float[] { 0, 0, b, 0, 0 },
                new float[] { 0, 0, 0, alphaScaleNormal, 0 },
                new float[] { 0, 0, 0, 0, 1 }
            };

            ColorMatrix colorMatrix = new ColorMatrix(matrixItemsNormal);
            using (ImageAttributes imageAttributes = new ImageAttributes())
            {
                imageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                InterpolationMode prevMode = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(silhouette, destRectNormal, 0, 0, silhouette.Width, silhouette.Height, GraphicsUnit.Pixel, imageAttributes);
                g.InterpolationMode = prevMode;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_suggestionPopup != null)
                {
                    _suggestionPopup.Dispose();
                    _suggestionPopup = null;
                }
                if (_clockTimer != null)
                {
                    _clockTimer.Stop();
                    _clockTimer.Dispose();
                    _clockTimer = null;
                }
                ThemeManager.AccentColorChanged -= OnAccentColorChanged;
                AppServices.Shortcuts.ShortcutsChanged -= OnShortcutsChanged;
                SilhouetteManager.SilhouetteChanged -= OnSilhouetteChanged;

                if (_shortcutContextMenu != null)
                {
                    _shortcutContextMenu.Dispose();
                    _shortcutContextMenu = null;
                }
                if (_silhouetteContextMenu != null)
                {
                    _silhouetteContextMenu.Dispose();
                    _silhouetteContextMenu = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
