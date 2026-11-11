using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.Downloads
{
    public class DownloadBubblePopup : ToolStripDropDown
    {
        private readonly DownloadBubbleControl _bubbleControl;
        private readonly ToolStripControlHost _host;
        private readonly Action _openAllDownloadsAction;
        private readonly Timer _autoCloseTimer;
        private DateTime _lastClosedTime = DateTime.MinValue;
        private bool _isAutoOpened = false;

        public DateTime LastClosedTime { get { return _lastClosedTime; } }

        public DownloadBubblePopup(Action openAllDownloadsAction = null)
        {
            _openAllDownloadsAction = openAllDownloadsAction;
            this.AutoClose = false;
            this.DropShadowEnabled = true;
            this.Margin = Padding.Empty;
            this.Padding = Padding.Empty;
            this.BackColor = UITheme.CardBackground;
            this.Renderer = new BubbleMenuRenderer();

            _autoCloseTimer = new Timer();
            _autoCloseTimer.Interval = 7000;
            _autoCloseTimer.Tick += (s, e) =>
            {
                if (this.Visible && !this.IsDisposed)
                {
                    if (!_isAutoOpened)
                    {
                        _autoCloseTimer.Stop();
                        return;
                    }

                    Point cursor = Cursor.Position;
                    Rectangle bounds = this.Bounds;
                    bounds.Inflate(10, 10);
                    if (bounds.Contains(cursor))
                    {
                        // User is hovering or interacting with the popup - cancel auto-close
                        CancelAutoHide();
                        return;
                    }

                    if (_bubbleControl != null && !_bubbleControl.IsDisposed && _bubbleControl.IsHandleCreated)
                    {
                        Rectangle hostBounds = _bubbleControl.RectangleToScreen(_bubbleControl.ClientRectangle);
                        hostBounds.Inflate(10, 10);
                        if (hostBounds.Contains(cursor))
                        {
                            CancelAutoHide();
                            return;
                        }
                    }

                    this.Close();
                }
            };

            _bubbleControl = new DownloadBubbleControl(this, () =>
            {
                this.Close();
                if (_openAllDownloadsAction != null)
                {
                    _openAllDownloadsAction();
                }
            });

            _host = new ToolStripControlHost(_bubbleControl)
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false,
                Size = _bubbleControl.Size
            };

            this.Items.Add(_host);
            this.Size = _bubbleControl.Size;

            ToolStripDismissHook.Register(this);
        }

        public void ShowBubble(Point screenPt, bool autoOpened = false)
        {
            _isAutoOpened = autoOpened;
            this.Show(screenPt);
            if (autoOpened)
            {
                if (_autoCloseTimer != null)
                {
                    _autoCloseTimer.Stop();
                    _autoCloseTimer.Start();
                }
            }
            else
            {
                if (_autoCloseTimer != null)
                {
                    _autoCloseTimer.Stop();
                }
            }
        }

        public void CancelAutoHide()
        {
            _isAutoOpened = false;
            if (_autoCloseTimer != null)
            {
                _autoCloseTimer.Stop();
            }
        }

        public void PauseAutoHide()
        {
            CancelAutoHide();
        }

        public void ResumeAutoHide()
        {
            if (_isAutoOpened && _autoCloseTimer != null && this.Visible && !this.IsDisposed)
            {
                _autoCloseTimer.Stop();
                _autoCloseTimer.Start();
            }
        }

        public void UpdatePopupSize(int newWidth, int newHeight)
        {
            if (_host != null && !_host.IsDisposed)
            {
                _host.Size = new Size(newWidth, newHeight);
            }
            this.Size = new Size(newWidth, newHeight);
        }

        public void RefreshContent()
        {
            if (_bubbleControl != null && !_bubbleControl.IsDisposed)
            {
                _bubbleControl.RefreshItems();
            }
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (!_isAutoOpened && _autoCloseTimer != null)
            {
                _autoCloseTimer.Stop();
            }
        }

        protected override void OnClosing(ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason != ToolStripDropDownCloseReason.CloseCalled)
            {
                Point cursor = Cursor.Position;
                Rectangle bounds = this.Bounds;
                bounds.Inflate(8, 8);

                if (bounds.Contains(cursor))
                {
                    e.Cancel = true;
                    return;
                }

                if (_bubbleControl != null && !_bubbleControl.IsDisposed && _bubbleControl.IsHandleCreated)
                {
                    Rectangle hostBounds = _bubbleControl.RectangleToScreen(_bubbleControl.ClientRectangle);
                    hostBounds.Inflate(8, 8);
                    if (hostBounds.Contains(cursor))
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }

            if (_autoCloseTimer != null) _autoCloseTimer.Stop();
            _lastClosedTime = DateTime.UtcNow;
            base.OnClosing(e);
        }

        protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
        {
            if (_autoCloseTimer != null) _autoCloseTimer.Stop();
            _lastClosedTime = DateTime.UtcNow;
            base.OnClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_autoCloseTimer != null)
                {
                    _autoCloseTimer.Stop();
                    _autoCloseTimer.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private class BubbleMenuRenderer : ToolStripProfessionalRenderer
        {
            public BubbleMenuRenderer() : base(new MaterialColorTable()) { }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (Pen p = new Pen(MaterialColors.AddressBarBorder, 1))
                {
                    Rectangle r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                    e.Graphics.DrawRectangle(p, r);
                }
            }
        }
    }

    public class DownloadBubbleControl : UserControl
    {
        private readonly DownloadBubblePopup _ownerPopup;
        private readonly Action _showAllAction;
        private readonly Panel _headerPanel;
        private readonly Panel _itemsPanel;
        private readonly Panel _footerPanel;
        private readonly ToolTip _toolTip;
        private readonly Dictionary<long, DownloadItemCard> _activeCards = new Dictionary<long, DownloadItemCard>();

        private const int BubbleWidth = 360;
        private const int CardHeight = 68;
        private const int CardSpacing = 6;
        private const int HeaderHeight = 42;
        private const int FooterHeight = 38;
        private const int MaxBubbleHeight = 380;

        public DownloadBubbleControl(DownloadBubblePopup ownerPopup = null, Action showAllAction = null)
        {
            _ownerPopup = ownerPopup;
            _showAllAction = showAllAction;

            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            this.DoubleBuffered = true;
            this.BackColor = UITheme.CardBackground;
            this.Size = new Size(BubbleWidth, 140);
            this.Font = UITheme.FontDefault;

            this.MouseEnter += (s, e) => { if (_ownerPopup != null) _ownerPopup.CancelAutoHide(); };
            this.MouseMove += (s, e) => { if (_ownerPopup != null) _ownerPopup.CancelAutoHide(); };
            this.MouseLeave += (s, e) =>
            {
                if (_ownerPopup != null && !this.ClientRectangle.Contains(this.PointToClient(Cursor.Position)))
                {
                    _ownerPopup.ResumeAutoHide();
                }
            };

            _toolTip = new ToolTip
            {
                InitialDelay = 400,
                ReshowDelay = 150,
                AutoPopDelay = 4000
            };

            // 1. Header
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = HeaderHeight,
                BackColor = UITheme.CardBackground,
                Padding = new Padding(14, 0, 14, 0)
            };
            _headerPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Color.FromArgb(50, 52, 56), 1))
                {
                    e.Graphics.DrawLine(p, 0, _headerPanel.Height - 1, _headerPanel.Width, _headerPanel.Height - 1);
                }
            };

            Label titleLbl = new Label
            {
                Text = AppStrings.Downloads.BubbleTitle,
                Font = UITheme.FontHeader,
                ForeColor = MaterialColors.TextPrimary,
                AutoSize = false,
                Size = new Size(200, 24),
                Location = new Point(14, 9),
                TextAlign = ContentAlignment.MiddleLeft
            };
            titleLbl.MouseEnter += (s, e) => { if (_ownerPopup != null) _ownerPopup.CancelAutoHide(); };
            titleLbl.MouseMove += (s, e) => { if (_ownerPopup != null) _ownerPopup.CancelAutoHide(); };
            _headerPanel.Controls.Add(titleLbl);

            // 2. Footer
            _footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = FooterHeight,
                BackColor = MaterialColors.NavBarBackground,
                Padding = new Padding(8, 4, 8, 4)
            };
            _footerPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Color.FromArgb(50, 52, 56), 1))
                {
                    e.Graphics.DrawLine(p, 0, 0, _footerPanel.Width, 0);
                }
            };

            Button showAllBtn = new Button
            {
                Text = AppStrings.Downloads.BubbleShowAll,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Font = UITheme.FontDefault,
                ForeColor = MaterialColors.AccentBlue,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            showAllBtn.FlatAppearance.BorderSize = 0;
            showAllBtn.FlatAppearance.MouseOverBackColor = MaterialColors.ButtonHover;
            showAllBtn.MouseEnter += (s, e) => { if (_ownerPopup != null) _ownerPopup.CancelAutoHide(); };
            showAllBtn.MouseMove += (s, e) => { if (_ownerPopup != null) _ownerPopup.CancelAutoHide(); };
            showAllBtn.Click += (s, e) =>
            {
                if (_showAllAction != null) _showAllAction();
            };
            _footerPanel.Controls.Add(showAllBtn);

            // 3. Items Panel
            _itemsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = false,
                BackColor = UITheme.CardBackground,
                Padding = new Padding(8, 6, 8, 6)
            };

            _headerPanel.MouseEnter += (s, e) => { if (_ownerPopup != null) _ownerPopup.CancelAutoHide(); };
            _headerPanel.MouseMove += (s, e) => { if (_ownerPopup != null) _ownerPopup.CancelAutoHide(); };
            _footerPanel.MouseEnter += (s, e) => { if (_ownerPopup != null) _ownerPopup.CancelAutoHide(); };
            _footerPanel.MouseMove += (s, e) => { if (_ownerPopup != null) _ownerPopup.CancelAutoHide(); };
            _itemsPanel.MouseEnter += (s, e) => { if (_ownerPopup != null) _ownerPopup.CancelAutoHide(); };
            _itemsPanel.MouseMove += (s, e) => { if (_ownerPopup != null) _ownerPopup.CancelAutoHide(); };

            this.Controls.Add(_itemsPanel);
            this.Controls.Add(_footerPanel);
            this.Controls.Add(_headerPanel);

            RefreshItems();
        }

        public void RefreshItems()
        {
            if (this.IsDisposed) return;
            List<DownloadItemModel> all = AppServices.Downloads.GetAll();

            int desiredHeight;

            if (all.Count == 0)
            {
                _itemsPanel.SuspendLayout();
                _itemsPanel.Controls.Clear();
                _activeCards.Clear();

                Label emptyLbl = new Label
                {
                    Text = AppStrings.Downloads.BubbleNoRecentDownloads,
                    ForeColor = MaterialColors.TextSecondary,
                    Font = UITheme.FontDefault,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                _itemsPanel.Controls.Add(emptyLbl);
                _itemsPanel.ResumeLayout();

                desiredHeight = HeaderHeight + 64 + FooterHeight;
            }
            else
            {
                // Prioritize in-progress / queued items, then recent items up to 4 max (no scrollbar)
                List<DownloadItemModel> topItems = new List<DownloadItemModel>();
                for (int i = 0; i < all.Count; i++)
                {
                    string state = all[i].State;
                    if (string.Equals(state, "InProgress", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(state, "Queued", StringComparison.OrdinalIgnoreCase))
                    {
                        topItems.Add(all[i]);
                    }
                    if (topItems.Count >= 4) break;
                }

                for (int i = 0; i < all.Count && topItems.Count < 4; i++)
                {
                    if (!topItems.Contains(all[i]))
                    {
                        topItems.Add(all[i]);
                    }
                }

                // Check if current active cards match topItems in count and IDs
                bool sameItems = (_activeCards.Count == topItems.Count);
                if (sameItems)
                {
                    for (int i = 0; i < topItems.Count; i++)
                    {
                        if (!_activeCards.ContainsKey(topItems[i].Id))
                        {
                            sameItems = false;
                            break;
                        }
                    }
                }

                if (sameItems)
                {
                    // IN-PLACE UPDATE: Update labels and progress bars without destroying controls
                    for (int i = 0; i < topItems.Count; i++)
                    {
                        DownloadItemModel item = topItems[i];
                        DownloadItemCard card;
                        if (_activeCards.TryGetValue(item.Id, out card))
                        {
                            card.UpdateState(item);
                        }
                    }
                    return;
                }

                // List changed: Rebuild card layout
                _itemsPanel.SuspendLayout();
                _itemsPanel.Controls.Clear();
                _activeCards.Clear();

                int cardWidth = BubbleWidth - 16;
                int yOffset = 6;

                Action SafeRefresh = () =>
                {
                    if (this.IsDisposed) return;
                    try
                    {
                        if (this.IsHandleCreated)
                        {
                            this.BeginInvoke(new Action(RefreshItems));
                        }
                        else
                        {
                            RefreshItems();
                        }
                    }
                    catch { }
                };

                for (int i = 0; i < topItems.Count; i++)
                {
                    DownloadItemModel item = topItems[i];
                    DownloadItemCard card = new DownloadItemCard(
                        item,
                        cardWidth,
                        _toolTip,
                        id => { AppServices.Downloads.PauseDownload(id); SafeRefresh(); },
                        id => { AppServices.Downloads.ResumeDownload(id); SafeRefresh(); },
                        id => { AppServices.Downloads.CancelDownload(id); SafeRefresh(); },
                        id => { AppServices.Downloads.DeleteDownload(id, false); SafeRefresh(); },
                        id => { AppServices.Downloads.RetryDownload(id); SafeRefresh(); },
                        () => { if (_ownerPopup != null) _ownerPopup.CancelAutoHide(); },
                        () => { if (_ownerPopup != null) _ownerPopup.Close(); }
                    );
                    card.Location = new Point(8, yOffset);
                    _itemsPanel.Controls.Add(card);
                    _activeCards[item.Id] = card;
                    yOffset += CardHeight + CardSpacing;
                }

                _itemsPanel.ResumeLayout();

                int itemsContentHeight = (topItems.Count * CardHeight) + ((topItems.Count - 1) * CardSpacing) + 12;
                desiredHeight = Math.Max(144, HeaderHeight + itemsContentHeight + FooterHeight);
            }

            if (this.Height != desiredHeight)
            {
                this.Size = new Size(BubbleWidth, desiredHeight);
                if (_ownerPopup != null)
                {
                    _ownerPopup.UpdatePopupSize(BubbleWidth, desiredHeight);
                }
            }
        }

        public static string FormatCompactSize(long bytes)
        {
            if (bytes <= 0) return "0B";
            const long kb = 1024L;
            const long mb = 1024L * 1024L;
            const long gb = 1024L * 1024L * 1024L;

            long roundedMb = (long)Math.Round((double)bytes / mb);
            if (bytes >= gb || roundedMb >= 1024)
            {
                long g = (long)Math.Round((double)bytes / gb);
                if (g < 1) g = 1;
                return g + "GB";
            }

            long roundedKb = (long)Math.Round((double)bytes / kb);
            if (bytes >= mb || roundedKb >= 1024)
            {
                long m = (long)Math.Round((double)bytes / mb);
                if (m < 1) m = 1;
                return m + "MB";
            }

            if (bytes >= kb)
            {
                long k = (long)Math.Round((double)bytes / kb);
                if (k < 1) k = 1;
                return k + "KB";
            }

            return bytes + "B";
        }

        public static string FormatCompactEta(DownloadItemModel item)
        {
            if (item == null || item.CurrentSpeed <= 0 || item.TotalBytes <= 0 || item.ReceivedBytes >= item.TotalBytes)
            {
                return null;
            }
            long remainingBytes = item.TotalBytes - item.ReceivedBytes;
            double seconds = (double)remainingBytes / item.CurrentSpeed;
            if (seconds <= 0)
            {
                return AppStrings.Downloads.TimeZeroSecLeft;
            }
            if (seconds < 55)
            {
                int secs = Math.Max(1, (int)Math.Round(seconds));
                return string.Format(AppStrings.Downloads.TimeSecLeftFormat, secs);
            }
            if (seconds < 3600)
            {
                int mins = Math.Max(1, (int)Math.Round(seconds / 60.0));
                return string.Format(AppStrings.Downloads.TimeMinLeftFormat, mins);
            }
            int hrs = (int)(seconds / 3600);
            int remMins = (int)((seconds % 3600) / 60);
            return remMins > 0 ? string.Format(AppStrings.Downloads.TimeHoursMinsLeftFormat, hrs, remMins) : string.Format(AppStrings.Downloads.TimeHoursLeftFormat, hrs);
        }

        public static string FormatRelativeTime(DateTime? endTime, DateTime? referenceNow = null)
        {
            if (!endTime.HasValue) return AppStrings.Downloads.TimeComplete;
            DateTime now = referenceNow ?? DateTime.Now;
            DateTime localEnd = endTime.Value.Kind == DateTimeKind.Utc ? endTime.Value.ToLocalTime() : endTime.Value;
            TimeSpan elapsed = now - localEnd;
            if (elapsed.TotalSeconds < 0) elapsed = TimeSpan.Zero;

            if (elapsed.TotalSeconds < 45)
            {
                return AppStrings.Downloads.TimeJustNow;
            }
            if (elapsed.TotalMinutes < 60)
            {
                int mins = Math.Max(1, (int)Math.Round(elapsed.TotalMinutes));
                return string.Format(AppStrings.Downloads.TimeMinAgoFormat, mins);
            }
            if (elapsed.TotalHours < 24)
            {
                int hrs = Math.Max(1, (int)Math.Round(elapsed.TotalHours));
                return string.Format(AppStrings.Downloads.TimeHourAgoFormat, hrs);
            }
            int days = Math.Max(1, (int)Math.Round(elapsed.TotalDays));
            return string.Format(AppStrings.Downloads.TimeDayAgoFormat, days);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_toolTip != null)
                {
                    _toolTip.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private class DownloadItemCard : Panel
        {
            public long ItemId { get; private set; }
            private readonly CircleProgressIcon _circleIcon;
            private readonly FlowLayoutPanel _actionFlow;
            private readonly Label _nameLbl;
            private readonly Label _statusLbl;
            private readonly ToolTip _toolTip;

            private readonly Action<long> _pauseAction;
            private readonly Action<long> _resumeAction;
            private readonly Action<long> _cancelAction;
            private readonly Action<long> _removeAction;
            private readonly Action<long> _retryAction;
            private readonly Action _pauseTimerAction;
            private readonly Action _closePopupAction;

            private string _lastRenderedState = null;
            private string _currentFilePath = null;
            private bool _isCompleted = false;

            private static readonly Color CardNormalBg = Color.FromArgb(41, 42, 45);
            private static readonly Color CardHoverBg = Color.FromArgb(50, 52, 56);

            public DownloadItemCard(
                DownloadItemModel item,
                int cardWidth,
                ToolTip toolTip,
                Action<long> pauseAction,
                Action<long> resumeAction,
                Action<long> cancelAction,
                Action<long> removeAction,
                Action<long> retryAction,
                Action pauseTimerAction = null,
                Action closePopupAction = null)
            {
                ItemId = item.Id;
                _toolTip = toolTip;
                _pauseAction = pauseAction;
                _resumeAction = resumeAction;
                _cancelAction = cancelAction;
                _removeAction = removeAction;
                _retryAction = retryAction;
                _pauseTimerAction = pauseTimerAction;
                _closePopupAction = closePopupAction;

                this.Size = new Size(cardWidth, CardHeight);
                this.BackColor = CardNormalBg;
                this.Margin = Padding.Empty;
                this.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

                this.Paint += (s, e) =>
                {
                    using (Pen borderPen = new Pen(Color.FromArgb(60, 64, 67), 1))
                    {
                        e.Graphics.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
                    }
                };

                // 1. Leading Circle Progress Icon
                _circleIcon = new CircleProgressIcon
                {
                    Size = new Size(34, 34),
                    Location = new Point(12, 17),
                    Cursor = Cursors.Default
                };
                this.Controls.Add(_circleIcon);

                // 2. Trailing Action Flow
                _actionFlow = new FlowLayoutPanel
                {
                    Location = new Point(cardWidth - 74, 20),
                    Size = new Size(64, 28),
                    FlowDirection = FlowDirection.RightToLeft,
                    BackColor = Color.Transparent,
                    Padding = Padding.Empty,
                    Margin = Padding.Empty,
                    WrapContents = false
                };
                _actionFlow.MouseEnter += (s, e) => { if (_pauseTimerAction != null) _pauseTimerAction(); };
                _actionFlow.MouseMove += (s, e) => { if (_pauseTimerAction != null) _pauseTimerAction(); };
                this.Controls.Add(_actionFlow);

                // 3. Middle Text
                int textX = 56;
                int textW = Math.Max(100, cardWidth - 84 - textX);

                _nameLbl = new Label
                {
                    Text = item.FileName ?? AppStrings.Downloads.UnknownFileName,
                    Font = UITheme.FontDefaultBold,
                    ForeColor = MaterialColors.TextPrimary,
                    Location = new Point(textX, 15),
                    Size = new Size(textW, 18),
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Color.Transparent
                };
                if (_toolTip != null && !string.IsNullOrEmpty(item.FileName))
                {
                    _toolTip.SetToolTip(_nameLbl, item.FileName);
                }
                this.Controls.Add(_nameLbl);

                _statusLbl = new Label
                {
                    Font = new Font("Segoe UI", 8.25f),
                    Location = new Point(textX, 35),
                    Size = new Size(textW, 16),
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Color.Transparent
                };
                this.Controls.Add(_statusLbl);

                // Hover & Click-to-Open handlers
                Action openFile = () =>
                {
                    if (_isCompleted && !string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
                    {
                        try { Process.Start(_currentFilePath); } catch { }
                        if (_closePopupAction != null) _closePopupAction();
                    }
                };

                Action setHover = () =>
                {
                    this.BackColor = CardHoverBg;
                    if (_pauseTimerAction != null) _pauseTimerAction();
                    if (_isCompleted && !string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
                    {
                        this.Cursor = Cursors.Hand;
                        _circleIcon.Cursor = Cursors.Hand;
                    }
                    else
                    {
                        this.Cursor = Cursors.Default;
                        _circleIcon.Cursor = Cursors.Default;
                    }
                };

                Action setNormal = () =>
                {
                    this.BackColor = CardNormalBg;
                    this.Cursor = Cursors.Default;
                    _circleIcon.Cursor = Cursors.Default;
                };

                this.MouseEnter += (s, e) => setHover();
                this.MouseLeave += (s, e) => setNormal();
                _circleIcon.MouseEnter += (s, e) => setHover();
                _circleIcon.MouseLeave += (s, e) => setNormal();
                _nameLbl.MouseEnter += (s, e) => setHover();
                _nameLbl.MouseLeave += (s, e) => setNormal();
                _statusLbl.MouseEnter += (s, e) => setHover();
                _statusLbl.MouseLeave += (s, e) => setNormal();

                this.Click += (s, e) => openFile();
                _circleIcon.Click += (s, e) => openFile();
                _nameLbl.Click += (s, e) => openFile();
                _statusLbl.Click += (s, e) => openFile();

                UpdateState(item);
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (_actionFlow != null)
                {
                    _actionFlow.Location = new Point(this.Width - 74, 20);
                }
                int textW = Math.Max(100, this.Width - 84 - 56);
                if (_nameLbl != null)
                {
                    _nameLbl.Size = new Size(textW, 18);
                }
                if (_statusLbl != null)
                {
                    _statusLbl.Size = new Size(textW, 16);
                }
            }

            public void UpdateState(DownloadItemModel item)
            {
                _currentFilePath = item.FilePath;

                if (_nameLbl.Text != (item.FileName ?? AppStrings.Downloads.UnknownFileName))
                {
                    _nameLbl.Text = item.FileName ?? AppStrings.Downloads.UnknownFileName;
                    if (_toolTip != null) _toolTip.SetToolTip(_nameLbl, _nameLbl.Text);
                }

                string displayState = item.GetStatusDisplay();
                bool isCompleted = string.Equals(displayState, "Completed", StringComparison.OrdinalIgnoreCase);
                bool isDownloading = string.Equals(displayState, "Downloading", StringComparison.OrdinalIgnoreCase);
                bool isPaused = string.Equals(displayState, "Paused", StringComparison.OrdinalIgnoreCase);
                bool isCancelled = string.Equals(displayState, "Cancelled", StringComparison.OrdinalIgnoreCase);
                bool isFailed = displayState.StartsWith("Failed", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(displayState, "Interrupted", StringComparison.OrdinalIgnoreCase);

                // Update circular progress ring
                _circleIcon.ShowProgress = isDownloading || isPaused;
                _circleIcon.Value = Math.Max(0, Math.Min(100, item.Percent));
                _circleIcon.ProgressColor = isPaused ? MaterialColors.AccentYellow : MaterialColors.AccentBlue;

                // Update icon and action buttons only if functional state changed
                if (_lastRenderedState != displayState)
                {
                    _lastRenderedState = displayState;

                    string iconName = "download-simple";
                    Color iconColor = MaterialColors.AccentBlue;

                    if (isCompleted)
                    {
                        iconName = "check";
                        iconColor = MaterialColors.StatusSuccess;
                    }
                    else if (isPaused)
                    {
                        iconName = "pause";
                        iconColor = MaterialColors.AccentYellow;
                    }
                    else if (isCancelled)
                    {
                        iconName = "x-circle";
                        iconColor = MaterialColors.TextSecondary;
                    }
                    else if (isFailed)
                    {
                        iconName = "x-circle";
                        iconColor = MaterialColors.StatusDanger;
                    }

                    _circleIcon.IconImage = MaterialIcons.GetIcon(iconName, 16, iconColor);

                    _actionFlow.SuspendLayout();
                    _actionFlow.Controls.Clear();

                    if (isDownloading)
                    {
                        Button cancelBtn = CreateButton("x", AppStrings.Downloads.BubbleBtnCancelDownload, () => _cancelAction(ItemId));
                        Button pauseBtn = CreateButton("pause", AppStrings.Downloads.BubbleBtnPauseDownload, () => _pauseAction(ItemId));
                        _actionFlow.Controls.Add(cancelBtn);
                        _actionFlow.Controls.Add(pauseBtn);
                    }
                    else if (isPaused)
                    {
                        Button cancelBtn = CreateButton("x", AppStrings.Downloads.BubbleBtnCancelDownload, () => _cancelAction(ItemId));
                        Button resumeBtn = CreateButton("play", AppStrings.Downloads.BubbleBtnResumeDownload, () => _resumeAction(ItemId));
                        _actionFlow.Controls.Add(cancelBtn);
                        _actionFlow.Controls.Add(resumeBtn);
                    }
                    else if (isCompleted)
                    {
                        Button clearBtn = CreateButton("x", AppStrings.Downloads.BubbleBtnRemoveFromList, () => _removeAction(ItemId));
                        Button folderBtn = CreateButton("folder", AppStrings.Downloads.BubbleBtnShowInFolder, () =>
                        {
                            if (File.Exists(item.FilePath))
                            {
                                try { Process.Start("explorer.exe", "/select,\"" + item.FilePath + "\""); } catch { }
                            }
                            else
                            {
                                string d = Path.GetDirectoryName(item.FilePath);
                                if (Directory.Exists(d)) try { Process.Start(d); } catch { }
                            }
                            if (_closePopupAction != null) _closePopupAction();
                        });
                        _actionFlow.Controls.Add(clearBtn);
                        _actionFlow.Controls.Add(folderBtn);
                    }
                    else
                    {
                        Button clearBtn = CreateButton("x", AppStrings.Downloads.BubbleBtnRemoveFromList, () => _removeAction(ItemId));
                        Button retryBtn = CreateButton("arrow-clockwise", AppStrings.Downloads.BubbleBtnRetryDownload, () => _retryAction(ItemId));
                        _actionFlow.Controls.Add(clearBtn);
                        _actionFlow.Controls.Add(retryBtn);
                    }

                    _actionFlow.ResumeLayout();
                }

                // Subtitle status: reduced and compact format
                string statusText;
                if (isCompleted)
                {
                    long finalSize = item.TotalBytes > 0 ? item.TotalBytes : item.ReceivedBytes;
                    if (finalSize <= 0) finalSize = item.GetWbDownloadSize();
                    statusText = FormatCompactSize(finalSize) + " • " + FormatRelativeTime(item.EndTime);
                }
                else if (isDownloading)
                {
                    string eta = FormatCompactEta(item);
                    if (item.TotalBytes > 0)
                    {
                        string sizeStr = FormatCompactSize(item.ReceivedBytes) + "/" + FormatCompactSize(item.TotalBytes);
                        statusText = !string.IsNullOrEmpty(eta) ? (sizeStr + " • " + eta) : sizeStr;
                    }
                    else
                    {
                        statusText = FormatCompactSize(item.ReceivedBytes) + " • Downloading";
                    }
                }
                else if (isPaused)
                {
                    if (item.TotalBytes > 0)
                    {
                        statusText = FormatCompactSize(item.ReceivedBytes) + "/" + FormatCompactSize(item.TotalBytes) + " • Paused";
                    }
                    else
                    {
                        statusText = "Paused";
                    }
                }
                else if (isCancelled)
                {
                    statusText = "Cancelled";
                }
                else if (isFailed)
                {
                    if (string.Equals(displayState, "Interrupted", StringComparison.OrdinalIgnoreCase))
                    {
                        statusText = "Interrupted";
                    }
                    else if (displayState.StartsWith("Failed", StringComparison.OrdinalIgnoreCase))
                    {
                        statusText = "Failed";
                    }
                    else
                    {
                        statusText = displayState;
                    }
                }
                else if (string.Equals(displayState, "Missing (Deleted)", StringComparison.OrdinalIgnoreCase))
                {
                    statusText = AppStrings.Downloads.StatusDeleted;
                }
                else
                {
                    statusText = displayState;
                }

                if (_statusLbl.Text != statusText)
                {
                    _statusLbl.Text = statusText;
                }
                _statusLbl.ForeColor = isCancelled ? MaterialColors.TextSecondary : UITheme.GetStatusColor(displayState);
            }

            private Button CreateButton(string iconName, string tooltipText, Action onClick)
            {
                Button btn = new Button
                {
                    Size = new Size(28, 28),
                    FlatStyle = FlatStyle.Flat,
                    Margin = new Padding(2, 0, 2, 0),
                    Cursor = Cursors.Hand,
                    Image = MaterialIcons.GetIcon(iconName, 14, MaterialColors.TextSecondary),
                    BackColor = Color.Transparent
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.FlatAppearance.MouseOverBackColor = MaterialColors.ButtonHover;

                btn.MouseEnter += (s, e) =>
                {
                    btn.Image = MaterialIcons.GetIcon(iconName, 14, MaterialColors.TextPrimary);
                    if (_pauseTimerAction != null) _pauseTimerAction();
                };
                btn.MouseLeave += (s, e) => btn.Image = MaterialIcons.GetIcon(iconName, 14, MaterialColors.TextSecondary);

                if (_toolTip != null && !string.IsNullOrEmpty(tooltipText))
                {
                    _toolTip.SetToolTip(btn, tooltipText);
                }

                btn.Click += (s, e) =>
                {
                    if (onClick != null) onClick();
                };
                return btn;
            }
        }

        public class CircleProgressIcon : Control
        {
            private int _value = 0;
            private bool _showProgress = false;
            private Image _iconImage = null;
            private Color _progressColor = MaterialColors.AccentBlue;
            private Color _trackColor = Color.FromArgb(56, 58, 62);

            public int Value
            {
                get { return _value; }
                set
                {
                    int clamped = Math.Max(0, Math.Min(100, value));
                    if (_value != clamped)
                    {
                        _value = clamped;
                        Invalidate();
                    }
                }
            }

            public bool ShowProgress
            {
                get { return _showProgress; }
                set
                {
                    if (_showProgress != value)
                    {
                        _showProgress = value;
                        Invalidate();
                    }
                }
            }

            public Image IconImage
            {
                get { return _iconImage; }
                set
                {
                    if (_iconImage != value)
                    {
                        _iconImage = value;
                        Invalidate();
                    }
                }
            }

            public Color ProgressColor
            {
                get { return _progressColor; }
                set
                {
                    if (_progressColor != value)
                    {
                        _progressColor = value;
                        Invalidate();
                    }
                }
            }

            public Color TrackColor
            {
                get { return _trackColor; }
                set
                {
                    if (_trackColor != value)
                    {
                        _trackColor = value;
                        Invalidate();
                    }
                }
            }

            public CircleProgressIcon()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint |
                         ControlStyles.SupportsTransparentBackColor, true);
                DoubleBuffered = true;
                BackColor = Color.Transparent;
                Size = new Size(34, 34);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                if (_showProgress)
                {
                    const float strokeWidth = 2.5f;
                    RectangleF ringRect = new RectangleF(3.5f, 3.5f, 27f, 27f);

                    // 1. Inactive track ring
                    using (Pen trackPen = new Pen(_trackColor, strokeWidth))
                    {
                        g.DrawEllipse(trackPen, ringRect);
                    }

                    // 2. Active progress arc
                    if (_value >= 100)
                    {
                        using (Pen progressPen = new Pen(_progressColor, strokeWidth))
                        {
                            g.DrawEllipse(progressPen, ringRect);
                        }
                    }
                    else if (_value > 0)
                    {
                        float sweepAngle = (_value / 100f) * 360f;
                        if (sweepAngle > 0.5f)
                        {
                            using (Pen progressPen = new Pen(_progressColor, strokeWidth))
                            {
                                progressPen.StartCap = LineCap.Round;
                                progressPen.EndCap = LineCap.Round;
                                g.DrawArc(progressPen, ringRect, -90f, sweepAngle);
                            }
                        }
                    }
                }

                // 3. Center icon
                if (_iconImage != null)
                {
                    int ix = (Width - _iconImage.Width) / 2;
                    int iy = (Height - _iconImage.Height) / 2;
                    g.DrawImage(_iconImage, ix, iy);
                }
            }
        }
    }
}
