using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.Browser.Suggestions
{
    public class SuggestionDropdownPopup : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_HIDE = 0;
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_HIDEWINDOW = 0x0080;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;

        private readonly SuggestionService _service = new SuggestionService();
        private List<SuggestionItem> _items = new List<SuggestionItem>();
        private int _selectedIndex = -1;
        private int _hoveredIndex = -1;

        private TextBox _attachedBox;
        private Func<Rectangle> _anchorBoundsProvider;
        private Control _anchorParent;
        private Form _hookedParentForm;
        private bool _isIncognito;
        private Action<string, bool> _onExecute;

        private System.Windows.Forms.Timer _debounceTimer;
        private CancellationTokenSource _cts;
        private string _userTypedText = string.Empty;
        private bool _isSuppressingTextChange = false;
        private bool _isNavigatingOrExecuted = false;

        private readonly Font _titleFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        private readonly Font _titleBoldFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        private readonly Font _subFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        private readonly Font _tagFont = new Font("Segoe UI", 8.0f, FontStyle.Regular);

        private const int BaseItemHeight = 36;
        private const int MaxVisibleItems = 7;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE (never steal keyboard focus)
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW (prevent showing in taskbar/Alt-Tab)
                cp.Style |= unchecked((int)0x80000000); // WS_POPUP
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        public bool IsPopupVisible
        {
            get { return this.Visible && _items.Count > 0; }
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
        }

        public bool IsIncognito
        {
            get { return _isIncognito; }
            set { _isIncognito = value; }
        }

        public SuggestionDropdownPopup()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.TopLevel = true;
            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(32, 33, 36);
            this.Cursor = Cursors.Default;
            this.TabStop = false;

            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            _debounceTimer = new System.Windows.Forms.Timer();
            _debounceTimer.Interval = 120;
            _debounceTimer.Tick += OnDebounceTick;
        }

        public void SuppressSuggestions()
        {
            _isNavigatingOrExecuted = true;
            _isSuppressingTextChange = true;
            _debounceTimer.Stop();
            if (_cts != null)
            {
                try { _cts.Cancel(); } catch { }
            }
            HidePopup();
        }

        public void ResumeSuggestions()
        {
            _isSuppressingTextChange = false;
        }

        public void Attach(TextBox textBox, Func<Rectangle> anchorBoundsProvider, Control anchorParent, bool isIncognito, Action<string, bool> onExecute)
        {
            Detach();

            _attachedBox = textBox;
            _anchorBoundsProvider = anchorBoundsProvider;
            _anchorParent = anchorParent;
            _isIncognito = isIncognito;
            _onExecute = onExecute;
            _isNavigatingOrExecuted = false;

            if (_attachedBox != null)
            {
                _attachedBox.TextChanged += OnAttachedTextChanged;
                _attachedBox.KeyDown += OnAttachedKeyDown;
                _attachedBox.GotFocus += OnAttachedGotFocus;
                _attachedBox.LostFocus += OnAttachedLostFocus;
            }

            if (_anchorParent != null)
            {
                _anchorParent.ParentChanged += OnAnchorParentOrFormChanged;
                HookParentForm();
            }
        }

        public void Detach()
        {
            HidePopup();

            if (_attachedBox != null)
            {
                _attachedBox.TextChanged -= OnAttachedTextChanged;
                _attachedBox.KeyDown -= OnAttachedKeyDown;
                _attachedBox.GotFocus -= OnAttachedGotFocus;
                _attachedBox.LostFocus -= OnAttachedLostFocus;
                _attachedBox = null;
            }

            if (_anchorParent != null)
            {
                _anchorParent.ParentChanged -= OnAnchorParentOrFormChanged;
            }

            UnhookParentForm();

            _anchorParent = null;
            _anchorBoundsProvider = null;
            _onExecute = null;
        }

        private void OnAttachedGotFocus(object sender, EventArgs e)
        {
            _isNavigatingOrExecuted = false;
        }

        private void OnAnchorParentOrFormChanged(object sender, EventArgs e)
        {
            HookParentForm();
        }

        private void HookParentForm()
        {
            Form parentForm = _anchorParent != null ? _anchorParent.FindForm() : null;
            if (parentForm != null && parentForm != _hookedParentForm)
            {
                UnhookParentForm();
                _hookedParentForm = parentForm;
                _hookedParentForm.LocationChanged += OnParentWindowBoundsChanged;
                _hookedParentForm.SizeChanged += OnParentWindowBoundsChanged;
                _hookedParentForm.Deactivate += OnParentWindowDeactivate;
            }
        }

        private void UnhookParentForm()
        {
            if (_hookedParentForm != null)
            {
                _hookedParentForm.LocationChanged -= OnParentWindowBoundsChanged;
                _hookedParentForm.SizeChanged -= OnParentWindowBoundsChanged;
                _hookedParentForm.Deactivate -= OnParentWindowDeactivate;
                _hookedParentForm = null;
            }
        }

        public void UpdatePosition()
        {
            if (this.Visible && !this.IsDisposed)
            {
                UpdatePositionAndGeometry();
            }
        }

        private void OnParentWindowBoundsChanged(object sender, EventArgs e)
        {
            if (this.Visible)
            {
                UpdatePositionAndGeometry();
            }
        }

        private void OnParentWindowDeactivate(object sender, EventArgs e)
        {
            HidePopup();
        }

        private void OnAttachedLostFocus(object sender, EventArgs e)
        {
            // Delay close slightly so that mouse click on popup can process
            System.Windows.Forms.Timer delayClose = new System.Windows.Forms.Timer();
            delayClose.Interval = 160;
            delayClose.Tick += (s, ev) =>
            {
                delayClose.Stop();
                delayClose.Dispose();
                if (_attachedBox != null && !_attachedBox.Focused && !this.Bounds.Contains(Cursor.Position))
                {
                    HidePopup();
                }
            };
            delayClose.Start();
        }

        private void OnAttachedTextChanged(object sender, EventArgs e)
        {
            if (_isSuppressingTextChange || _isNavigatingOrExecuted) return;
            if (_attachedBox == null || !_attachedBox.Focused)
            {
                HidePopup();
                return;
            }

            string text = _attachedBox.Text;
            _userTypedText = text;

            if (string.IsNullOrWhiteSpace(text))
            {
                HidePopup();
                return;
            }

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private async void OnDebounceTick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();

            if (_isNavigatingOrExecuted || _attachedBox == null || !_attachedBox.Focused || string.IsNullOrWhiteSpace(_attachedBox.Text))
            {
                HidePopup();
                return;
            }

            string query = _attachedBox.Text.Trim();

            if (_cts != null)
            {
                try { _cts.Cancel(); _cts.Dispose(); } catch { }
            }
            _cts = new CancellationTokenSource();

            try
            {
                var list = await _service.GetSuggestionsAsync(query, _isIncognito, MaxVisibleItems, _cts.Token);
                if (_isNavigatingOrExecuted || _attachedBox == null || !_attachedBox.Focused || string.IsNullOrWhiteSpace(_attachedBox.Text))
                {
                    HidePopup();
                    return;
                }

                // If user changed query while fetching, discard
                if (!string.Equals(_attachedBox.Text.Trim(), query, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (list != null && list.Count > 0)
                {
                    _items = list;
                    _selectedIndex = -1;
                    _hoveredIndex = -1;
                    ShowPopup();
                }
                else
                {
                    HidePopup();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                HidePopup();
            }
        }

        private void OnAttachedKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Escape)
            {
                _isNavigatingOrExecuted = false;
            }
            HandleKeyDown(e);
        }

        public bool HandleKeyDown(KeyEventArgs e)
        {
            if (e == null) return false;

            if (!this.Visible || _items.Count == 0)
            {
                if (e.KeyCode == Keys.Down)
                {
                    // Trigger suggestions on down arrow even if popup was hidden
                    _debounceTimer.Stop();
                    OnDebounceTick(_debounceTimer, EventArgs.Empty);
                }
                return false;
            }

            if (e.KeyCode == Keys.Down)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                MoveSelectionDown();
                return true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                MoveSelectionUp();
                return true;
            }
            else if (e.KeyCode == Keys.Tab)
            {
                if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    ApplySuggestionText(_items[_selectedIndex]);
                    return true;
                }
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                RestoreUserText();
                HidePopup();
                return true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    ExecuteSuggestion(_items[_selectedIndex]);
                    return true;
                }
                else
                {
                    _isNavigatingOrExecuted = true;
                    HidePopup();
                }
            }
            return false;
        }

        private void MoveSelectionDown()
        {
            if (_items.Count == 0) return;

            _selectedIndex++;
            if (_selectedIndex >= _items.Count)
            {
                _selectedIndex = -1;
                RestoreUserText();
            }
            else
            {
                ApplySuggestionText(_items[_selectedIndex]);
            }
            this.Invalidate();
        }

        private void MoveSelectionUp()
        {
            if (_items.Count == 0) return;

            _selectedIndex--;
            if (_selectedIndex < -1)
            {
                _selectedIndex = _items.Count - 1;
                ApplySuggestionText(_items[_selectedIndex]);
            }
            else if (_selectedIndex == -1)
            {
                RestoreUserText();
            }
            else
            {
                ApplySuggestionText(_items[_selectedIndex]);
            }
            this.Invalidate();
        }

        private void ApplySuggestionText(SuggestionItem item)
        {
            if (_attachedBox == null || item == null) return;

            _isSuppressingTextChange = true;
            try
            {
                _attachedBox.Text = item.Title;
                _attachedBox.SelectionStart = _attachedBox.Text.Length;
                _attachedBox.SelectionLength = 0;
            }
            finally
            {
                _isSuppressingTextChange = false;
            }
        }

        private void RestoreUserText()
        {
            if (_attachedBox == null) return;

            _isSuppressingTextChange = true;
            try
            {
                _attachedBox.Text = _userTypedText ?? string.Empty;
                _attachedBox.SelectionStart = _attachedBox.Text.Length;
                _attachedBox.SelectionLength = 0;
            }
            finally
            {
                _isSuppressingTextChange = false;
            }
        }

        public void ExecuteSuggestion(SuggestionItem item)
        {
            if (item == null) return;

            string target = item.ActionValue;
            _isNavigatingOrExecuted = true;
            HidePopup();

            if (_attachedBox != null)
            {
                _isSuppressingTextChange = true;
                try
                {
                    _attachedBox.Text = item.Title;
                    _attachedBox.SelectionStart = _attachedBox.Text.Length;
                    _attachedBox.SelectionLength = 0;
                }
                finally
                {
                    _isSuppressingTextChange = false;
                }
            }

            if (_onExecute != null && !string.IsNullOrEmpty(target))
            {
                _onExecute(target, false);
            }
        }

        private void ShowPopup()
        {
            if (_isNavigatingOrExecuted || _attachedBox == null || !_attachedBox.Focused || _items.Count == 0)
            {
                HidePopup();
                return;
            }

            if (_anchorBoundsProvider == null || _anchorParent == null) return;

            UpdatePositionAndGeometry();

            if (!this.Visible)
            {
                ShowWindow(this.Handle, SW_SHOWNOACTIVATE);
                SetWindowPos(this.Handle, HWND_TOP, this.Left, this.Top, this.Width, this.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            this.Invalidate();
        }

        public void HidePopup()
        {
            _debounceTimer.Stop();
            if (_cts != null)
            {
                _cts.Cancel();
            }
            _items.Clear();
            _selectedIndex = -1;
            _hoveredIndex = -1;

            if (this.Visible)
            {
                ShowWindow(this.Handle, SW_HIDE);
                SetWindowPos(this.Handle, HWND_TOP, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_HIDEWINDOW | 0x0001 | 0x0002);
            }
        }

        private void UpdatePositionAndGeometry()
        {
            if (_anchorBoundsProvider == null || _anchorParent == null || _items.Count == 0) return;

            Rectangle anchorBounds = _anchorBoundsProvider();
            if (anchorBounds.Width <= 0 || anchorBounds.Height <= 0) return;

            Point screenPt = _anchorParent.PointToScreen(new Point(anchorBounds.Left, anchorBounds.Bottom));

            float dpi = DpiHelper.GetDpiScale(_anchorParent);
            int itemH = (int)Math.Round(BaseItemHeight * dpi);
            int totalH = (_items.Count * itemH) + (int)(8 * dpi);
            int width = Math.Max(200, anchorBounds.Width);

            // Screen boundary clamping
            Screen scr = Screen.FromPoint(screenPt);
            int left = screenPt.X;
            if (left + width > scr.WorkingArea.Right)
            {
                left = Math.Max(scr.WorkingArea.Left, scr.WorkingArea.Right - width);
            }

            int top = screenPt.Y + (int)(2 * dpi);
            if (top + totalH > scr.WorkingArea.Bottom)
            {
                // Place above anchor if no space below
                top = _anchorParent.PointToScreen(new Point(anchorBounds.Left, anchorBounds.Top)).Y - totalH - (int)(2 * dpi);
            }

            this.SetBounds(left, top, width, totalH);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int itemH = GetItemHeight();
            int padTop = (int)(4 * DpiHelper.GetDpiScale(this));
            int idx = (e.Y - padTop) / itemH;

            if (idx >= 0 && idx < _items.Count)
            {
                if (_hoveredIndex != idx)
                {
                    _hoveredIndex = idx;
                    _selectedIndex = idx;
                    this.Invalidate();
                }
            }
            else
            {
                if (_hoveredIndex != -1)
                {
                    _hoveredIndex = -1;
                    this.Invalidate();
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoveredIndex != -1)
            {
                _hoveredIndex = -1;
                this.Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            int itemH = GetItemHeight();
            int padTop = (int)(4 * DpiHelper.GetDpiScale(this));
            int idx = (e.Y - padTop) / itemH;

            if (idx >= 0 && idx < _items.Count)
            {
                ExecuteSuggestion(_items[idx]);
            }
        }

        private int GetItemHeight()
        {
            float dpi = DpiHelper.GetDpiScale(this);
            return Math.Max(28, (int)Math.Round(BaseItemHeight * dpi));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            float dpi = DpiHelper.GetDpiScale(this);
            int w = this.Width;
            int h = this.Height;

            // 1. Draw rounded background card
            Rectangle cardRect = new Rectangle(0, 0, w - 1, h - 1);
            using (GraphicsPath path = UITheme.CreateRoundedPath(cardRect, (int)(8 * dpi)))
            {
                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(32, 33, 36)))
                {
                    g.FillPath(bgBrush, path);
                }
                using (Pen borderPen = new Pen(Color.FromArgb(60, 64, 67), 1f))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            if (_items.Count == 0) return;

            int itemH = GetItemHeight();
            int top = (int)(4 * dpi);
            int iconSize = (int)(16 * dpi);

            Color accent = ThemeManager.CurrentAccentColor;

            for (int i = 0; i < _items.Count; i++)
            {
                SuggestionItem item = _items[i];
                Rectangle rowRect = new Rectangle((int)(4 * dpi), top, w - (int)(8 * dpi), itemH);
                bool isSelected = (i == _selectedIndex);

                // Row Highlight
                if (isSelected)
                {
                    using (GraphicsPath rowPath = UITheme.CreateRoundedPath(rowRect, (int)(4 * dpi)))
                    {
                        using (SolidBrush selBrush = new SolidBrush(Color.FromArgb(48, 50, 54)))
                        {
                            g.FillPath(selBrush, rowPath);
                        }
                    }

                    // Left vertical accent stripe
                    Rectangle stripeRect = new Rectangle(rowRect.X, rowRect.Y + (int)(4 * dpi), (int)(3 * dpi), rowRect.Height - (int)(8 * dpi));
                    using (SolidBrush stripeBrush = new SolidBrush(accent))
                    {
                        g.FillRectangle(stripeBrush, stripeRect);
                    }
                }

                // Left Icon
                int iconX = rowRect.X + (int)(12 * dpi);
                int iconY = rowRect.Y + (rowRect.Height - iconSize) / 2;
                Color iconColor = isSelected ? Color.FromArgb(240, 246, 252) : Color.FromArgb(154, 160, 166);
                if (item.Type == SuggestionType.Bookmark)
                {
                    iconColor = MaterialColors.AccentYellow;
                }
                MaterialIcons.DrawIcon(g, item.IconName, new Rectangle(iconX, iconY, iconSize, iconSize), iconColor);

                // Right Tag (e.g. Google Search, History, Bookmark, wb://)
                int rightMargin = rowRect.Right - (int)(10 * dpi);
                if (!string.IsNullOrEmpty(item.TagName))
                {
                    string tag = item.TagName;
                    SizeF tagSize = g.MeasureString(tag, _tagFont);
                    int tagW = (int)Math.Ceiling(tagSize.Width);
                    int tagH = (int)Math.Ceiling(tagSize.Height);
                    int tagX = rightMargin - tagW;
                    int tagY = rowRect.Y + (rowRect.Height - tagH) / 2;

                    Rectangle tagTextRect = new Rectangle(tagX, tagY, tagW, tagH);
                    using (SolidBrush tagBrush = new SolidBrush(isSelected ? accent : Color.FromArgb(128, 134, 139)))
                    {
                        g.DrawString(tag, _tagFont, tagBrush, tagTextRect);
                    }
                    rightMargin = tagX - (int)(8 * dpi);
                }

                // Title and Subtitle Text
                int textX = iconX + iconSize + (int)(10 * dpi);
                int textW = Math.Max(50, rightMargin - textX);

                bool hasSubtitle = !string.IsNullOrEmpty(item.Subtitle);
                if (hasSubtitle)
                {
                    // Two lines: Title and Subtitle
                    int halfH = rowRect.Height / 2;
                    Rectangle titleRect = new Rectangle(textX, rowRect.Y + (int)(2 * dpi), textW, halfH);
                    Rectangle subRect = new Rectangle(textX, rowRect.Y + halfH - (int)(1 * dpi), textW, halfH);

                    DrawHighlightedString(g, item.Title, item.MatchedQuery, titleRect, _titleFont, isSelected);

                    using (StringFormat sf = new StringFormat
                    {
                        Trimming = StringTrimming.EllipsisCharacter,
                        FormatFlags = StringFormatFlags.NoWrap,
                        LineAlignment = StringAlignment.Center
                    })
                    using (SolidBrush subBrush = new SolidBrush(Color.FromArgb(154, 160, 166)))
                    {
                        g.DrawString(item.Subtitle, _subFont, subBrush, subRect, sf);
                    }
                }
                else
                {
                    // Single line: Vertically centered Title
                    Rectangle titleRect = new Rectangle(textX, rowRect.Y, textW, rowRect.Height);
                    DrawHighlightedString(g, item.Title, item.MatchedQuery, titleRect, _titleFont, isSelected);
                }

                top += itemH;
            }
        }

        private void DrawHighlightedString(Graphics g, string text, string query, Rectangle bounds, Font baseFont, bool isSelected)
        {
            if (string.IsNullOrEmpty(text)) return;

            using (StringFormat sf = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center
            })
            using (SolidBrush normalBrush = new SolidBrush(isSelected ? Color.FromArgb(240, 246, 252) : Color.FromArgb(232, 234, 237)))
            {
                g.DrawString(text, baseFont, normalBrush, bounds, sf);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Detach();
                if (_debounceTimer != null)
                {
                    _debounceTimer.Stop();
                    _debounceTimer.Dispose();
                    _debounceTimer = null;
                }
                if (_cts != null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = null;
                }
                if (_titleFont != null) _titleFont.Dispose();
                if (_titleBoldFont != null) _titleBoldFont.Dispose();
                if (_subFont != null) _subFont.Dispose();
                if (_tagFont != null) _tagFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
