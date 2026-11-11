using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using wb.Browser.Suggestions;
using wb.Core;
using wb.UI;

namespace wb.Browser
{
    /// <summary>
    /// Native WinForms Incognito New Tab page (wb://incognito).
    /// Uses NewTabView as the direct layout and visual template to ensure complete consistency:
    /// - Slot 1 (Logo Slot): Centered Incognito circular emblem (fedora & glasses vector)
    /// - Slot 2 (Clock Slot): "You've gone incognito" heading in identical vertical position and bounds
    /// - Slot 3 (Date Slot): Subtitle privacy reminder in identical vertical position, font and bounds
    /// - Slot 4 (Search Slot): Centered search pill with identical height, width and styling
    /// </summary>
    public class IncognitoNewTabView : UserControl
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        private readonly Action<string, bool> _openUrlCallback;
        private readonly Action _closeTabCallback;

        private TextBox _searchBox;
        private SuggestionDropdownPopup _suggestionPopup;
        private Rectangle _forwardArrowBounds;
        private bool _isForwardArrowHovered = false;
        private bool _isSearchPillHovered = false;
        private bool _isSearchActive = false;

        private const int ContentMaxWidth = 620;
        private const int SearchPillHeight = 48;

        // Cached layout positions (matching NewTabView template)
        private Rectangle _logoSlotBounds;
        private Rectangle _emblemBounds;
        private Rectangle _titleBounds;
        private Rectangle _subtitleBounds;
        private Rectangle _searchPillBounds;

        public IncognitoNewTabView(Action<string, bool> openUrlCallback, Action closeTabCallback = null)
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
                true,
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

            // Follow NewTabView template layout exactly:
            // Logo Slot (92) + gap (12) + Title/Clock (46) + Date/Subtitle (22) + gap (20) + Search (48) + gap (30) + Cards (160)
            int totalContentH = 462;
            int startY = Math.Max(24, (clientH - totalContentH) / 2);

            // 1. Slot 1: Logo Slot & Centered Incognito Emblem
            int logoW = 180;
            int logoH = 92;
            int logoX = (clientW - logoW) / 2;
            _logoSlotBounds = new Rectangle(logoX, startY, logoW, logoH);

            int emblemSize = 76;
            int emblemX = (clientW - emblemSize) / 2;
            int emblemY = startY + (logoH - emblemSize) / 2;
            _emblemBounds = new Rectangle(emblemX, emblemY, emblemSize, emblemSize);

            // 2. Slot 2 & 3: Title & Subtitle (strictly matching Clock & Date positions from NewTabView)
            int titleY = _logoSlotBounds.Bottom + 12;
            _titleBounds = new Rectangle(0, titleY, clientW, 46);
            _subtitleBounds = new Rectangle(0, titleY + 48, clientW, 22);

            // 3. Slot 4: Search Pill (matching NewTabView dimensions and coordinates)
            int pillW = Math.Min(ContentMaxWidth, Math.Max(320, clientW - 64));
            int pillX = (clientW - pillW) / 2;
            int pillY = _subtitleBounds.Bottom + 20;
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

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // 1. Draw Incognito Circular Emblem (centered in the Logo Slot)
            if (_emblemBounds.Width > 0)
            {
                using (GraphicsPath circlePath = new GraphicsPath())
                {
                    circlePath.AddEllipse(_emblemBounds);
                    using (SolidBrush emblemBg = new SolidBrush(Color.FromArgb(41, 44, 49)))
                    {
                        g.FillPath(emblemBg, circlePath);
                    }
                    using (Pen emblemPen = new Pen(Color.FromArgb(60, 64, 70), 1.5f))
                    {
                        g.DrawPath(emblemPen, circlePath);
                    }
                }

                int iconSize = 44;
                int ix = _emblemBounds.X + (_emblemBounds.Width - iconSize) / 2;
                int iy = _emblemBounds.Y + (_emblemBounds.Height - iconSize) / 2;
                MaterialIcons.DrawIcon(g, "incognito", new Rectangle(ix, iy, iconSize, iconSize), Color.FromArgb(232, 234, 237));
            }

            // 2. Title ("You've gone incognito", matching Clock Slot)
            using (Font titleFont = new Font("Segoe UI", 26f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(240, 246, 252)))
            using (StringFormat sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(AppStrings.Browser.IncognitoTitle, titleFont, titleBrush, _titleBounds, sfCenter);
            }

            // 3. Subtitle ("Now you can browse privately...", matching Date Slot)
            using (Font subtitleFont = new Font("Segoe UI", 13.5f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush subtitleBrush = new SolidBrush(Color.FromArgb(154, 160, 166)))
            using (StringFormat sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(AppStrings.Browser.IncognitoSubtitle, subtitleFont, subtitleBrush, _subtitleBounds, sfCenter);
            }

            // 4. Search Container Pill (matching Search Slot)
            if (_searchPillBounds.Width > 0)
            {
                DrawSearchPill(g);
            }
        }

        private void DrawSearchPill(Graphics g)
        {
            Rectangle r = _searchPillBounds;
            bool isFocused = _isSearchActive || (_searchBox != null && _searchBox.Focused);

            Color pillBg = isFocused
                ? Color.FromArgb(41, 42, 45)
                : (_isSearchPillHovered ? Color.FromArgb(51, 54, 59) : Color.FromArgb(41, 42, 45));

            using (GraphicsPath path = CreateRoundedRectanglePath(r, r.Height / 2))
            {
                using (SolidBrush bgBrush = new SolidBrush(pillBg))
                {
                    g.FillPath(bgBrush, path);
                }
            }

            Color borderColor;
            float borderWidth;
            if (isFocused)
            {
                borderColor = Color.FromArgb(138, 180, 248); // Crisp Google blue / incognito accent
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

            // Left Search Icon (Magnifying Glass - drawn with GDI+ pen matching NewTabView)
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
                using (StringFormat sfHint = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
                {
                    Rectangle hintRect = new Rectangle(r.Left + 48, r.Top, r.Width - 96, r.Height);
                    g.DrawString("Search the web or type a URL...", hintFont, hintBrush, hintRect, sfHint);
                }
            }

            // Right Forward Arrow Button (Circular)
            Rectangle fa = _forwardArrowBounds;
            bool hasText = _searchBox != null && !string.IsNullOrEmpty(_searchBox.Text.Trim());
            bool isActive = hasText || _isForwardArrowHovered;

            Color faBg = isActive ? Color.FromArgb(138, 180, 248) : Color.FromArgb(51, 54, 59);
            Color faArrow = isActive ? Color.FromArgb(20, 20, 20) : Color.FromArgb(154, 160, 166);

            using (SolidBrush faBrush = new SolidBrush(faBg))
            {
                g.FillEllipse(faBrush, fa);
            }

            if (_isForwardArrowHovered && isActive)
            {
                using (Pen haloPen = new Pen(Color.FromArgb(90, 138, 180, 248), 2.5f))
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
                    FocusSearchBox();
                }
            }
            else
            {
                UnfocusSearchBox();
            }

            if (_forwardArrowBounds.Contains(e.Location) && e.Button == MouseButtons.Left)
            {
                if (_searchBox != null && !string.IsNullOrEmpty(_searchBox.Text.Trim()))
                {
                    ExecuteSearchOrNavigate(_searchBox.Text);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            bool newPillHover = _searchPillBounds.Contains(e.Location) && !_forwardArrowBounds.Contains(e.Location);
            if (newPillHover != _isSearchPillHovered)
            {
                _isSearchPillHovered = newPillHover;
                UpdateSearchBoxHover();
                InvalidateSearchPill();
            }

            bool newArrowHover = _forwardArrowBounds.Contains(e.Location);
            if (newArrowHover != _isForwardArrowHovered)
            {
                _isForwardArrowHovered = newArrowHover;
                InvalidateForwardArrow();
            }

            if (_forwardArrowBounds.Contains(e.Location))
            {
                this.Cursor = Cursors.Hand;
            }
            else if (_searchPillBounds.Contains(e.Location))
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

            if (_isSearchPillHovered)
            {
                _isSearchPillHovered = false;
                UpdateSearchBoxHover();
                InvalidateSearchPill();
            }
            if (_isForwardArrowHovered)
            {
                _isForwardArrowHovered = false;
                InvalidateForwardArrow();
            }
            this.Cursor = Cursors.Default;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                UnfocusSearchBox();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
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
            }
            base.Dispose(disposing);
        }
    }
}
