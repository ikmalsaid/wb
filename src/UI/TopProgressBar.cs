using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace wb.UI
{
    /// <summary>
    /// Sleek, modern accent progress line positioned along the top edge of the browser canvas
    /// that animates smoothly during navigation, with optional experimental Stardust particle effects.
    /// </summary>
    public class TopProgressBar : Control
    {
        private class StardustParticle
        {
            public float X;
            public float Y;
            public float Vx;
            public float Vy;
            public float Size;      // Star ray length or diamond radius
            public int Life;
            public int MaxLife;
            public Color Color;
            public int Type;        // 0 = 4-point sparkle star (✦), 1 = diamond glint star (◆), 2 = twinkling dust speck (•)
        }

        private const int MaxParticles = 50;

        private static readonly Color[] StardustColors = new Color[]
        {
            Color.FromArgb(255, 255, 255), // Pure Starlight White
            Color.FromArgb(255, 230, 100), // Starlight Gold
            Color.FromArgb(160, 240, 255), // Cosmic Cyan
            Color.FromArgb(255, 180, 220), // Fairy Pink
            Color.FromArgb(220, 180, 255), // Magic Lilac
            Color.FromArgb(255, 200, 110), // Sunburst Amber
            Color.FromArgb(180, 255, 210)  // Mint Sparkle
        };

        private float _progress = 0f;
        private float _targetProgress = 0f;
        private bool _isLoading = false;
        private Timer _animTimer;
        private Timer _hideTimer;
        private float _dpiScale = 1.0f;
        private bool _enableStardust = false;
        private readonly List<StardustParticle> _particles = new List<StardustParticle>();
        private readonly Random _rand = new Random();

        private readonly SolidBrush _barBrush = new SolidBrush(Color.White);
        private readonly SolidBrush _particleBrush = new SolidBrush(Color.White);
        private readonly SolidBrush _coreBrush = new SolidBrush(Color.White);
        private readonly Pen _particlePen = new Pen(Color.White, 1.0f);
        private readonly PointF[] _diamondPoints = new PointF[4];

        public TopProgressBar()
        {
            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            this.DoubleBuffered = true;
            this.Dock = DockStyle.Top;
            this.Visible = false;
            this.BackColor = MaterialColors.ToolbarBackground;

            _enableStardust = ExperimentalManager.EnableProgressBarStardust;
            UpdateHeight();
            ExperimentalManager.ConfigChanged += OnExperimentalConfigChanged;

            _animTimer = new Timer();
            _animTimer.Interval = 25;
            _animTimer.Tick += AnimTimer_Tick;

            _hideTimer = new Timer();
            _hideTimer.Interval = 200;
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                _animTimer.Stop();
                this.Visible = false;
                _progress = 0f;
                _targetProgress = 0f;
                _particles.Clear();
            };
        }

        private void OnExperimentalConfigChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new EventHandler(OnExperimentalConfigChanged), sender, e); } catch { }
                return;
            }
            this.EnableStardust = ExperimentalManager.EnableProgressBarStardust;
        }

        public bool EnableStardust
        {
            get { return _enableStardust; }
            set
            {
                if (_enableStardust != value)
                {
                    _enableStardust = value;
                    if (!_enableStardust)
                    {
                        _particles.Clear();
                    }
                    UpdateHeight();
                    this.Invalidate();
                }
            }
        }

        private void UpdateHeight()
        {
            int baseH = _enableStardust ? 5 : 3;
            this.Height = Math.Max(2, (int)Math.Round(baseH * _dpiScale));
        }

        public float DpiScale
        {
            get { return _dpiScale; }
            set
            {
                if (Math.Abs(_dpiScale - value) > 0.01f)
                {
                    _dpiScale = value;
                    UpdateHeight();
                }
            }
        }

        public bool IsLoading
        {
            get { return _isLoading; }
        }

        public void StartLoading()
        {
            _isLoading = true;
            _hideTimer.Stop();
            _particles.Clear();
            _progress = 0.08f;
            _targetProgress = 0.35f;
            this.Visible = true;
            this.BringToFront();
            _animTimer.Start();
            this.Invalidate();
        }

        public void StepProgress()
        {
            if (!_isLoading) return;
            if (_targetProgress < 0.85f)
            {
                _targetProgress += 0.12f;
                if (_targetProgress > 0.85f) _targetProgress = 0.85f;
            }
        }

        public void CompleteLoading()
        {
            _isLoading = false;
            _targetProgress = 1.0f;
            if (_enableStardust)
            {
                SpawnCompletionBurst();
            }
            this.Invalidate();
        }

        private Color GetRandomParticleColor()
        {
            // 20% chance to match the current accent color
            if (_rand.Next(5) == 0)
            {
                return MaterialColors.AccentBlue;
            }
            return StardustColors[_rand.Next(StardustColors.Length)];
        }

        private void SpawnLoadingParticles()
        {
            if (_particles.Count >= MaxParticles) return;

            int barWidth = (int)(this.Width * Math.Min(1.0f, _progress));
            if (barWidth <= 0) return;

            int barHeight = this.Height;

            // 1. Emit 1-2 stardust stars backwards from the leading head
            int count = _rand.Next(1, 3);
            for (int i = 0; i < count && _particles.Count < MaxParticles; i++)
            {
                // 50% 4-point sparkle star (0), 30% diamond star (1), 20% dust speck (2)
                int roll = _rand.Next(10);
                int type = (roll < 5) ? 0 : (roll < 8 ? 1 : 2);
                float size = (type == 0)
                    ? (float)(_rand.NextDouble() * 1.2 + 2.4)
                    : (type == 1 ? (float)(_rand.NextDouble() * 0.6 + 1.4) : (float)(_rand.NextDouble() * 0.5 + 1.0));

                _particles.Add(new StardustParticle
                {
                    X = barWidth + (float)(_rand.NextDouble() * 5 - 2),
                    Y = (float)(_rand.NextDouble() * (barHeight - 2) + 1.0f),
                    Vx = (float)(-_rand.NextDouble() * 1.5 - 0.4), // drift backwards in the wake
                    Vy = (float)((_rand.NextDouble() - 0.5) * 0.3),
                    Size = size,
                    Life = 0,
                    MaxLife = _rand.Next(16, 26),
                    Color = GetRandomParticleColor(),
                    Type = type
                });
            }

            // 2. Occasionally spawn a twinkling star along the loaded line
            if (_rand.Next(3) == 0 && _particles.Count < MaxParticles && barWidth > 15)
            {
                int type = (_rand.Next(3) == 0) ? 1 : 0;
                float size = (type == 0)
                    ? (float)(_rand.NextDouble() * 1.0 + 2.2)
                    : (float)(_rand.NextDouble() * 0.5 + 1.3);

                _particles.Add(new StardustParticle
                {
                    X = (float)(_rand.NextDouble() * barWidth),
                    Y = (float)(_rand.NextDouble() * (barHeight - 2) + 1.0f),
                    Vx = 0f,
                    Vy = (float)((_rand.NextDouble() - 0.5) * 0.1),
                    Size = size,
                    Life = 0,
                    MaxLife = _rand.Next(12, 20),
                    Color = GetRandomParticleColor(),
                    Type = type
                });
            }
        }

        private void SpawnCompletionBurst()
        {
            int barWidth = this.Width;
            int count = Math.Min(35, MaxParticles - _particles.Count);
            int barHeight = this.Height;
            for (int i = 0; i < count; i++)
            {
                int roll = _rand.Next(10);
                int type = (roll < 5) ? 0 : (roll < 8 ? 1 : 2);
                float size = (type == 0)
                    ? (float)(_rand.NextDouble() * 1.2 + 2.4)
                    : (type == 1 ? (float)(_rand.NextDouble() * 0.6 + 1.4) : (float)(_rand.NextDouble() * 0.5 + 1.0));

                _particles.Add(new StardustParticle
                {
                    X = (float)(_rand.NextDouble() * barWidth),
                    Y = (float)(_rand.NextDouble() * (barHeight - 2) + 1.0f),
                    Vx = (float)((_rand.NextDouble() - 0.5) * 1.6),
                    Vy = (float)((_rand.NextDouble() - 0.5) * 0.4),
                    Size = size,
                    Life = 0,
                    MaxLife = _rand.Next(16, 26),
                    Color = GetRandomParticleColor(),
                    Type = type
                });
            }
        }

        private void UpdateParticles()
        {
            if (_particles.Count == 0) return;

            int barHeight = this.Height;
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.X += p.Vx;
                p.Y += p.Vy;

                if (p.Y < 1.0f) { p.Y = 1.0f; p.Vy = Math.Abs(p.Vy); }
                if (p.Y > barHeight - 1.5f) { p.Y = barHeight - 1.5f; p.Vy = -Math.Abs(p.Vy); }

                p.Life++;
                if (p.Life >= p.MaxLife || p.X < 0)
                {
                    _particles.RemoveAt(i);
                }
            }
        }

        private void AnimTimer_Tick(object sender, EventArgs e)
        {
            if (_isLoading)
            {
                // Gradually creep forward while loading
                if (_targetProgress < 0.75f)
                {
                    _targetProgress += 0.005f;
                }

                float diff = _targetProgress - _progress;
                _progress += diff * 0.18f;
                if (_progress > 0.92f) _progress = 0.92f;

                if (_enableStardust && _progress > 0.02f)
                {
                    SpawnLoadingParticles();
                }

                UpdateParticles();
                this.Invalidate();
            }
            else
            {
                // Completing: rapid sweep to 100%
                float diff = 1.0f - _progress;
                _progress += Math.Max(0.08f, diff * 0.35f);

                if (_enableStardust)
                {
                    UpdateParticles();
                }

                if (_progress >= 0.99f)
                {
                    _progress = 1.0f;
                    if (!_hideTimer.Enabled)
                    {
                        _hideTimer.Interval = _enableStardust ? 280 : 200;
                        _hideTimer.Start();
                    }
                }

                this.Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!this.Visible || _progress <= 0f) return;

            Graphics g = e.Graphics;
            int barWidth = (int)(this.Width * Math.Min(1.0f, _progress));
            if (barWidth <= 0) return;

            int barLineHeight = _enableStardust ? Math.Max(2, (int)Math.Round(3.0f * _dpiScale)) : this.Height;
            Rectangle rect = new Rectangle(0, 0, barWidth, barLineHeight);

            // Draw modern accent blue progress line with subtle end glow
            _barBrush.Color = MaterialColors.AccentBlue;
            g.FillRectangle(_barBrush, rect);

            // Draw bright highlight at the leading edge without wrap-around seam glitch
            if (barWidth > 12 && _isLoading)
            {
                int glowWidth = Math.Min(36, barWidth);
                Rectangle glowRect = new Rectangle(barWidth - glowWidth, 0, glowWidth, barLineHeight);
                using (LinearGradientBrush glowBrush = new LinearGradientBrush(
                    glowRect,
                    Color.FromArgb(0, 255, 255, 255),
                    Color.FromArgb(160, 255, 255, 255),
                    LinearGradientMode.Horizontal))
                {
                    glowBrush.WrapMode = WrapMode.TileFlipXY;
                    g.FillRectangle(glowBrush, glowRect);
                }
            }

            // Draw Stardust Stars & Glints
            if (_enableStardust && _particles.Count > 0)
            {
                SmoothingMode prevMode = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                for (int i = 0; i < _particles.Count; i++)
                {
                    var p = _particles[i];
                    float factor = (float)p.Life / p.MaxLife;
                    float alphaNorm = (factor < 0.20f)
                        ? (factor / 0.20f)
                        : (1.0f - (factor - 0.20f) / 0.80f);
                    int alpha = (int)(Math.Max(0f, Math.Min(1f, alphaNorm)) * 255);
                    if (alpha <= 10) continue;

                    Color c = Color.FromArgb(alpha, p.Color.R, p.Color.G, p.Color.B);

                    if (p.Type == 0)
                    {
                        // ✦ 4-point sparkle star (delicate cross rays + bright center core)
                        float rayLen = p.Size;
                        _particlePen.Color = Color.FromArgb(Math.Max(0, alpha - 30), c);
                        g.DrawLine(_particlePen, p.X - rayLen, p.Y, p.X + rayLen, p.Y);
                        g.DrawLine(_particlePen, p.X, p.Y - rayLen, p.X, p.Y + rayLen);

                        _coreBrush.Color = Color.FromArgb(alpha, 255, 255, 255);
                        g.FillRectangle(_coreBrush, p.X - 0.6f, p.Y - 0.6f, 1.4f, 1.4f);
                    }
                    else if (p.Type == 1)
                    {
                        // ◆ Diamond mini-star (crisp 4-point diamond with white center)
                        float sz = p.Size;
                        _diamondPoints[0].X = p.X; _diamondPoints[0].Y = p.Y - sz;
                        _diamondPoints[1].X = p.X + sz; _diamondPoints[1].Y = p.Y;
                        _diamondPoints[2].X = p.X; _diamondPoints[2].Y = p.Y + sz;
                        _diamondPoints[3].X = p.X - sz; _diamondPoints[3].Y = p.Y;

                        _particleBrush.Color = c;
                        g.FillPolygon(_particleBrush, _diamondPoints);

                        _coreBrush.Color = Color.FromArgb(alpha, 255, 255, 255);
                        g.FillRectangle(_coreBrush, p.X - 0.4f, p.Y - 0.4f, 1.0f, 1.0f);
                    }
                    else
                    {
                        // • Shimmering stardust speck
                        float dotR = p.Size;
                        _particleBrush.Color = c;
                        g.FillEllipse(_particleBrush, p.X - dotR, p.Y - dotR, dotR * 2.0f, dotR * 2.0f);
                    }
                }

                g.SmoothingMode = prevMode;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ExperimentalManager.ConfigChanged -= OnExperimentalConfigChanged;
                if (_animTimer != null) { _animTimer.Dispose(); _animTimer = null; }
                if (_hideTimer != null) { _hideTimer.Dispose(); _hideTimer = null; }
                if (_barBrush != null) { _barBrush.Dispose(); }
                if (_particleBrush != null) { _particleBrush.Dispose(); }
                if (_coreBrush != null) { _coreBrush.Dispose(); }
                if (_particlePen != null) { _particlePen.Dispose(); }
                _particles.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
