using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using wb.Core;

namespace wb.UI
{
    /// <summary>
    /// A sleek, dark-themed notification banner docked beneath the navigation/tab bar.
    /// Alerts the user during Google 2-Step Verification (security code challenge) and provides
    /// a 1-click action to open g.co/sc in their default system browser.
    /// Styled consistently with the Block Element panel and other top helper bars.
    /// </summary>
    public class GoogleSecurityCodeBanner : Panel
    {
        private Label _titleLabel;
        private Label _descLabel;
        private Button _actionButton;
        private Button _closeButton;
        private float _dpiScale = 1.0f;
        private readonly EventHandler _accentColorChangedHandler;

        public event EventHandler ActionClicked;
        public event EventHandler Dismissed;

        public GoogleSecurityCodeBanner()
        {
            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            this.DoubleBuffered = true;
            this.Dock = DockStyle.Top;
            this.Height = 36;
            this.BackColor = MaterialColors.NavBarBackground;
            this.Visible = false;

            InitializeComponents();

            _accentColorChangedHandler = (s, e) => UpdateThemeColors();
            ThemeManager.AccentColorChanged += _accentColorChangedHandler;
        }

        public float DpiScale
        {
            get { return _dpiScale; }
            set
            {
                if (Math.Abs(_dpiScale - value) > 0.01f)
                {
                    _dpiScale = value;
                    ApplyDpiScale();
                }
            }
        }

        private void InitializeComponents()
        {
            _titleLabel = new Label
            {
                Text = AppStrings.Browser.GoogleSecurityCodeBannerTitle,
                Font = UITheme.FontDefaultBold,
                ForeColor = ThemeManager.CurrentAccentColor,
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _descLabel = new Label
            {
                Text = AppStrings.Browser.GoogleSecurityCodeBannerDesc,
                Font = UITheme.FontDefault,
                ForeColor = MaterialColors.TextSecondary,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            _actionButton = new Button
            {
                Text = AppStrings.Browser.GoogleSecurityCodeBannerAction,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = UITheme.FontDefault
            };
            UITheme.ApplyButton(_actionButton);
            _actionButton.FlatAppearance.BorderSize = 0;
            _actionButton.Click += (s, e) =>
            {
                OpenSecurityCodeUrl();
                EventHandler handler = ActionClicked;
                if (handler != null) handler(this, EventArgs.Empty);
            };

            _closeButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            UITheme.ApplyButton(_closeButton);
            _closeButton.FlatAppearance.BorderSize = 0;
            _closeButton.Click += (s, e) => Dismiss();

            this.Controls.Add(_titleLabel);
            this.Controls.Add(_descLabel);
            this.Controls.Add(_actionButton);
            this.Controls.Add(_closeButton);

            ApplyDpiScale();
        }

        public void Dismiss()
        {
            this.Visible = false;
            EventHandler handler = Dismissed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void OpenSecurityCodeUrl()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://g.co/sc",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("GoogleSecurityCodeBanner: Failed to open g.co/sc: " + ex.Message);
            }
        }

        private void ApplyDpiScale()
        {
            this.Height = Math.Max(32, (int)Math.Round(36 * _dpiScale));

            int closeIconSize = Math.Max(10, (int)Math.Round(12 * _dpiScale));
            _closeButton.Image = MaterialIcons.GetDpiIcon("x", _dpiScale, closeIconSize, MaterialColors.TextSecondary);
            _closeButton.Size = new Size(Math.Max(24, (int)Math.Round(26 * _dpiScale)), Math.Max(22, (int)Math.Round(24 * _dpiScale)));

            _actionButton.Size = new Size(Math.Max(95, (int)Math.Round(105 * _dpiScale)), Math.Max(22, (int)Math.Round(24 * _dpiScale)));
            _actionButton.Font = UITheme.FontDefault;

            _titleLabel.Font = UITheme.FontDefaultBold;
            _descLabel.Font = UITheme.FontDefault;

            UpdateLayout();
            this.Invalidate();
        }

        private void UpdateThemeColors()
        {
            if (this.IsDisposed) return;
            if (_titleLabel != null)
            {
                _titleLabel.ForeColor = ThemeManager.CurrentAccentColor;
            }
            if (_actionButton != null)
            {
                _actionButton.Invalidate();
            }
            this.Invalidate();
        }

        private bool _inLayout;

        private void UpdateLayout()
        {
            if (_inLayout) return;
            if (_titleLabel == null || _descLabel == null || _actionButton == null || _closeButton == null) return;

            int h = this.ClientSize.Height;
            int w = this.ClientSize.Width;
            if (h <= 0 || w <= 0) return;

            _inLayout = true;
            this.SuspendLayout();
            try
            {
                int leftPadding = (int)Math.Round(12 * _dpiScale);
                int titleH = Math.Max(14, (int)Math.Round(16 * _dpiScale));
                _titleLabel.Location = new Point(leftPadding, (h - titleH) / 2);

                int rightMargin = (int)Math.Round(12 * _dpiScale);
                int closeW = _closeButton.Width;
                int closeH = _closeButton.Height;
                int closeLeft = w - rightMargin - closeW;
                _closeButton.Bounds = new Rectangle(closeLeft, (h - closeH) / 2, closeW, closeH);

                int btnWidth = _actionButton.Width;
                int btnHeight = _actionButton.Height;
                int btnSpacing = (int)Math.Round(8 * _dpiScale);
                int actionLeft = _closeButton.Left - btnSpacing - btnWidth;
                _actionButton.Bounds = new Rectangle(actionLeft, (h - btnHeight) / 2, btnWidth, btnHeight);

                int textLeft = _titleLabel.Right + (int)Math.Round(12 * _dpiScale);
                int textWidth = Math.Max(20, _actionButton.Left - textLeft - (int)Math.Round(8 * _dpiScale));
                _descLabel.Bounds = new Rectangle(textLeft, 0, textWidth, h);
            }
            finally
            {
                this.ResumeLayout(false);
                _inLayout = false;
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            UpdateLayout();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            UpdateLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Draw subtle 1px bottom separator line consistent with Block Element and navbar panels
            using (Pen sepPen = new Pen(MaterialColors.Separator))
            {
                e.Graphics.DrawLine(sepPen, 0, this.Height - 1, this.Width, this.Height - 1);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThemeManager.AccentColorChanged -= _accentColorChangedHandler;
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Checks whether the given URL and title correspond to Google's 2-Step Verification security code challenge.
        /// </summary>
        public static bool IsGoogleSecurityCodeChallenge(string url, string title)
        {
            if (string.IsNullOrEmpty(url)) return false;

            string lowerUrl = url.ToLowerInvariant();

            // Direct challenge endpoints
            if (lowerUrl.Contains("accounts.google.com") && (lowerUrl.Contains("/challenge/skotp") || lowerUrl.Contains("skotp")))
                return true;

            if (lowerUrl.Contains("myaccount.google.com/signinoptions/two-step-verification/security-code"))
                return true;

            if (lowerUrl.Contains("g.co/sc"))
                return true;

            // Accounts domain with security code in title
            if (lowerUrl.Contains("accounts.google.com") && !string.IsNullOrEmpty(title))
            {
                string lowerTitle = title.ToLowerInvariant();
                if (lowerTitle.Contains("security code") || lowerTitle.Contains("get a code to sign in"))
                    return true;
            }

            return false;
        }
    }
}
