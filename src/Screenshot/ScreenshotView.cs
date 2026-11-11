using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.Screenshot
{
    public class ScreenshotView : UserControl
    {
        private ScreenshotSession _session;
        private Bitmap _baseImage;
        private Action _closeTabAction;

        private List<ScreenshotAnnotation> _annotations = new List<ScreenshotAnnotation>();
        private Stack<UndoStep> _undoStack = new Stack<UndoStep>();
        private Stack<UndoStep> _redoStack = new Stack<UndoStep>();

        private AnnotationTool _currentTool = AnnotationTool.Hand;
        private Color _currentColor = Color.FromArgb(234, 67, 53); // Red
        private float _currentStrokeWidth = 3.5f;

        // Viewport and Navigation
        private float _zoom = 1.0f;
        private PointF _panOffset = PointF.Empty;
        private bool _isPanning = false;
        private Point _panStartMouse = Point.Empty;
        private PointF _panStartOffset = PointF.Empty;
        private bool _isInitialFitDone = false;

        public AnnotationTool CurrentTool
        {
            get { return _currentTool; }
        }

        public PointF PanOffset
        {
            get { return _panOffset; }
            set { _panOffset = value; }
        }
        public Point PanStartMouse
        {
            get { return _panStartMouse; }
            set { _panStartMouse = value; }
        }
        public PointF PanStartOffset
        {
            get { return _panStartOffset; }
            set { _panStartOffset = value; }
        }

        // Live Drawing Temp State
        private bool _isDrawing = false;
        private PointF _drawStartPoint = PointF.Empty;
        private PointF _drawCurrentPoint = PointF.Empty;
        private ScreenshotAnnotation _tempAnnotation = null;

        public PointF DrawStartPoint
        {
            get { return _drawStartPoint; }
            set { _drawStartPoint = value; }
        }
        public PointF DrawCurrentPoint
        {
            get { return _drawCurrentPoint; }
            set { _drawCurrentPoint = value; }
        }

        // UI Controls
        private Panel _headerBar;
        private Panel _canvasContainer;
        private CanvasControl _canvas;

        private class UndoStep
        {
            public bool IsCrop { get; set; }
            public Bitmap PreviousImage { get; set; }
            public List<ScreenshotAnnotation> Annotations { get; set; }
        }

        public ScreenshotSession Session { get { return _session; } }
        public string SessionId { get { return _session != null ? _session.Id : null; } }

        public ScreenshotView(ScreenshotSession session, Action closeTabAction = null)
        {
            _session = session;
            _closeTabAction = closeTabAction;
            _baseImage = session != null ? session.Image : null;

            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(24, 25, 28);

            InitializeComponent();
            FitImageToWindow();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // 1. Sleek, minimal header bar
            _headerBar = new HeaderBar(this);
            _headerBar.Dock = DockStyle.Top;
            _headerBar.Height = 44;
            _headerBar.BackColor = MaterialColors.NavBarBackground;

            // 2. Canvas Container
            _canvasContainer = new Panel();
            _canvasContainer.Dock = DockStyle.Fill;
            _canvasContainer.BackColor = Color.FromArgb(20, 21, 24);

            _canvas = new CanvasControl(this);
            _canvas.Dock = DockStyle.Fill;
            _canvasContainer.Controls.Add(_canvas);

            this.Controls.Add(_canvasContainer);
            this.Controls.Add(_headerBar);

            this.ResumeLayout(false);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (this.Visible && !_isInitialFitDone && this.Width > 50 && this.Height > 50)
            {
                _isInitialFitDone = true;
                FitImageToWindow();
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (this.Width > 50 && this.Height > 50)
            {
                _isInitialFitDone = true;
                FitImageToWindow();
            }
        }

        public float Zoom
        {
            get { return _zoom; }
        }

        public void FitImageToWindow()
        {
            if (_baseImage == null) return;

            int canvasW = _canvas != null && _canvas.Width > 50 ? _canvas.Width : (this.Width > 50 ? this.Width : 1200);
            int canvasH = _canvas != null && _canvas.Height > 50 ? _canvas.Height : (this.Height > 94 ? this.Height - 44 : 800);

            int padding = 40;
            int availW = Math.Max(100, canvasW - padding);
            int availH = Math.Max(100, canvasH - padding);

            float scaleX = (float)availW / _baseImage.Width;
            float scaleY = (float)availH / _baseImage.Height;

            // If it is a long document/full-page screenshot (taller than standard aspect ratio)
            bool isTallPage = _baseImage.Height > _baseImage.Width * 1.3f;
            if (isTallPage)
            {
                // Fit by width so text and page elements remain readable
                _zoom = Math.Min(1.0f, scaleX);
                if (_zoom < 0.2f) _zoom = 0.2f;

                float scaledW = _baseImage.Width * _zoom;
                float scaledH = _baseImage.Height * _zoom;
                float x = (canvasW - scaledW) / 2f;
                float y = (scaledH <= canvasH) ? (canvasH - scaledH) / 2f : 20f;
                _panOffset = new PointF(x, y);
            }
            else
            {
                // Standard screenshot: fit within viewport
                _zoom = Math.Min(1.0f, Math.Min(scaleX, scaleY));
                if (_zoom < 0.2f) _zoom = 0.2f;

                float scaledW = _baseImage.Width * _zoom;
                float scaledH = _baseImage.Height * _zoom;

                float x = (canvasW - scaledW) / 2f;
                float y = (canvasH - scaledH) / 2f;
                _panOffset = new PointF(x, y);
            }

            if (_canvas != null) _canvas.Invalidate();
            if (_headerBar != null) _headerBar.Invalidate();
        }

        public void SetZoom(float targetZoom)
        {
            SetZoom(targetZoom, true);
        }

        public void SetZoom(float targetZoom, bool centerOnViewport)
        {
            if (_baseImage == null)
            {
                _zoom = Math.Max(0.1f, Math.Min(5.0f, targetZoom));
                if (_canvas != null) _canvas.Invalidate();
                if (_headerBar != null) _headerBar.Invalidate();
                return;
            }

            float oldZoom = _zoom;
            float newZoom = Math.Max(0.1f, Math.Min(5.0f, targetZoom));

            int canvasW = _canvas != null && _canvas.Width > 50 ? _canvas.Width : (this.Width > 50 ? this.Width : 1200);
            int canvasH = _canvas != null && _canvas.Height > 50 ? _canvas.Height : (this.Height > 94 ? this.Height - 44 : 800);

            float newScaledW = _baseImage.Width * newZoom;
            float newScaledH = _baseImage.Height * newZoom;

            float newX;
            float newY;

            // 1. Horizontal centering
            if (newScaledW <= canvasW)
            {
                // Fully fit within canvas width -> always strictly centered horizontally
                newX = (canvasW - newScaledW) / 2.0f;
            }
            else
            {
                // Expand/contract symmetrically around center of viewport
                float anchorX = canvasW / 2.0f;
                newX = anchorX - (anchorX - _panOffset.X) * (newZoom / oldZoom);

                // Clamp within bounds so image does not fly into void
                float minX = canvasW - newScaledW - 20;
                float maxX = 20;
                newX = Math.Max(minX, Math.Min(maxX, newX));
            }

            // 2. Vertical centering
            if (newScaledH <= canvasH)
            {
                // Fully fit within canvas height -> always strictly centered vertically
                newY = (canvasH - newScaledH) / 2.0f;
            }
            else
            {
                // Expand/contract symmetrically around center of viewport
                float anchorY = canvasH / 2.0f;
                newY = anchorY - (anchorY - _panOffset.Y) * (newZoom / oldZoom);

                // Clamp within bounds
                float minY = canvasH - newScaledH - 20;
                float maxY = 20;
                newY = Math.Max(minY, Math.Min(maxY, newY));
            }

            _zoom = newZoom;
            _panOffset = new PointF(newX, newY);

            if (_canvas != null) _canvas.Invalidate();
            if (_headerBar != null) _headerBar.Invalidate();
        }

        public void ZoomIn()
        {
            float target;
            if (_zoom < 0.95f) target = _zoom + 0.15f;
            else if (_zoom < 1.95f) target = _zoom + 0.25f;
            else target = _zoom + 0.5f;

            SetZoom(target, true);
        }

        public void ZoomOut()
        {
            float target;
            if (_zoom > 2.05f) target = _zoom - 0.5f;
            else if (_zoom > 1.05f) target = _zoom - 0.25f;
            else target = _zoom - 0.15f;

            SetZoom(target, true);
        }

        public void ResetZoomTo100()
        {
            if (_baseImage == null) return;
            int canvasW = _canvas != null && _canvas.Width > 50 ? _canvas.Width : (this.Width > 50 ? this.Width : 1200);
            int canvasH = _canvas != null && _canvas.Height > 50 ? _canvas.Height : (this.Height > 94 ? this.Height - 44 : 800);
            _zoom = 1.0f;
            float x = (canvasW - _baseImage.Width) / 2.0f;
            float y = (_baseImage.Height <= canvasH) ? (canvasH - _baseImage.Height) / 2.0f : 20f;
            _panOffset = new PointF(x, y);
            if (_canvas != null) _canvas.Invalidate();
            if (_headerBar != null) _headerBar.Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Oemplus) || keyData == (Keys.Control | Keys.Add))
            {
                ZoomIn();
                return true;
            }
            if (keyData == (Keys.Control | Keys.OemMinus) || keyData == (Keys.Control | Keys.Subtract))
            {
                ZoomOut();
                return true;
            }
            if (keyData == (Keys.Control | Keys.D0) || keyData == (Keys.Control | Keys.NumPad0))
            {
                FitImageToWindow();
                return true;
            }
            if (keyData == (Keys.Control | Keys.D1) || keyData == (Keys.Control | Keys.NumPad1))
            {
                ResetZoomTo100();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Z))
            {
                Undo();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z))
            {
                Redo();
                return true;
            }
            if (keyData == (Keys.Control | Keys.C))
            {
                CopyToClipboard();
                return true;
            }
            if (keyData == (Keys.Control | Keys.S))
            {
                SaveToFile();
                return true;
            }
            if (keyData == Keys.Enter && _canvas != null && _canvas.HasCropSelection)
            {
                ApplyCrop(Rectangle.Round(_canvas.CropRect));
                _canvas.ClearCrop();
                return true;
            }
            if (keyData == Keys.Escape && _canvas != null && _canvas.HasCropSelection)
            {
                _canvas.ClearCrop();
                _currentTool = AnnotationTool.Hand;
                if (_headerBar != null) _headerBar.Invalidate();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        internal static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            return UniversalScrollBar.CreateRoundedPath(rect, radius);
        }

        public void Undo()
        {
            if (_undoStack.Count > 0)
            {
                UndoStep step = _undoStack.Pop();
                _redoStack.Push(new UndoStep
                {
                    IsCrop = step.IsCrop,
                    PreviousImage = _baseImage,
                    Annotations = new List<ScreenshotAnnotation>(_annotations)
                });

                if (step.IsCrop && step.PreviousImage != null)
                {
                    _baseImage = step.PreviousImage;
                    FitImageToWindow();
                }

                _annotations = step.Annotations ?? new List<ScreenshotAnnotation>();
                if (_canvas != null) _canvas.Invalidate();
                if (_headerBar != null) _headerBar.Invalidate();
            }
            else if (_annotations.Count > 0)
            {
                _redoStack.Push(new UndoStep
                {
                    IsCrop = false,
                    PreviousImage = null,
                    Annotations = new List<ScreenshotAnnotation>(_annotations)
                });
                _annotations.Clear();
                if (_canvas != null) _canvas.Invalidate();
                if (_headerBar != null) _headerBar.Invalidate();
            }
        }

        public void Redo()
        {
            if (_redoStack.Count > 0)
            {
                UndoStep step = _redoStack.Pop();
                _undoStack.Push(new UndoStep
                {
                    IsCrop = step.IsCrop,
                    PreviousImage = _baseImage,
                    Annotations = new List<ScreenshotAnnotation>(_annotations)
                });

                if (step.IsCrop && step.PreviousImage != null)
                {
                    _baseImage = step.PreviousImage;
                    FitImageToWindow();
                }

                _annotations = step.Annotations ?? new List<ScreenshotAnnotation>();
                if (_canvas != null) _canvas.Invalidate();
                if (_headerBar != null) _headerBar.Invalidate();
            }
        }

        public void ApplyCrop(Rectangle cropRect)
        {
            if (_baseImage == null || cropRect.Width < 5 || cropRect.Height < 5) return;

            Rectangle clamped = Rectangle.Intersect(new Rectangle(0, 0, _baseImage.Width, _baseImage.Height), cropRect);
            if (clamped.Width <= 0 || clamped.Height <= 0) return;

            // Render existing composite to preserve annotations within cropped region
            Bitmap composite = RenderFullResolution();
            if (composite == null) return;

            Bitmap cropped = new Bitmap(clamped.Width, clamped.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(cropped))
            {
                g.DrawImage(composite, new Rectangle(0, 0, clamped.Width, clamped.Height), clamped, GraphicsUnit.Pixel);
            }
            composite.Dispose();

            // Push to undo stack
            _undoStack.Push(new UndoStep
            {
                IsCrop = true,
                PreviousImage = _baseImage,
                Annotations = new List<ScreenshotAnnotation>(_annotations)
            });
            _redoStack.Clear();

            _baseImage = cropped;
            _annotations.Clear();
            _currentTool = AnnotationTool.Hand;

            FitImageToWindow();
            if (_headerBar != null) _headerBar.Invalidate();

            OverlayPopup.ShowToast(this, "Image cropped", "check", 1800);
        }

        public Bitmap RenderFullResolution()
        {
            if (_baseImage == null) return null;

            Bitmap result = new Bitmap(_baseImage.Width, _baseImage.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                g.DrawImage(_baseImage, 0, 0, _baseImage.Width, _baseImage.Height);

                Rectangle imgBounds = new Rectangle(0, 0, _baseImage.Width, _baseImage.Height);
                g.SetClip(imgBounds);

                for (int i = 0; i < _annotations.Count; i++)
                {
                    _annotations[i].Draw(g, result);
                }
            }
            return result;
        }

        public void CopyToClipboard()
        {
            using (Bitmap composite = RenderFullResolution())
            {
                if (composite != null)
                {
                    ClipboardHelper.SetImage(composite);
                    OverlayPopup.ShowToast(this, "Copied to clipboard", "check", 2000);
                }
            }
        }

        public void SaveToFile()
        {
            using (Bitmap composite = RenderFullResolution())
            {
                if (composite == null) return;

                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Title = "Save Screenshot";
                    sfd.Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp";
                    sfd.FileName = string.Format("Screenshot_{0:yyyyMMdd_HHmmss}.png", DateTime.Now);

                    if (sfd.ShowDialog(this) == DialogResult.OK)
                    {
                        ImageFormat format = ImageFormat.Png;
                        string ext = Path.GetExtension(sfd.FileName).ToLowerInvariant();
                        if (ext == ".jpg" || ext == ".jpeg") format = ImageFormat.Jpeg;
                        else if (ext == ".bmp") format = ImageFormat.Bmp;

                        composite.Save(sfd.FileName, format);
                        OverlayPopup.ShowToast(this, "Saved successfully", "check", 2000);
                    }
                }
            }
        }



        public PointF ScreenToImagePoint(Point screenPoint)
        {
            if (_zoom <= 0 || _baseImage == null) return PointF.Empty;
            float imgX = (screenPoint.X - _panOffset.X) / _zoom;
            float imgY = (screenPoint.Y - _panOffset.Y) / _zoom;

            // Strictly clamp coordinates inside the image pixel boundary
            imgX = Math.Max(0f, Math.Min(_baseImage.Width, imgX));
            imgY = Math.Max(0f, Math.Min(_baseImage.Height, imgY));

            return new PointF(imgX, imgY);
        }

        // ==========================================
        // Canvas Control
        // ==========================================
        private class CanvasControl : Control
        {
            private ScreenshotView _parent;

            private RectangleF _cropRect = RectangleF.Empty;
            private bool _isDraggingCrop = false;
            private PointF _cropStartPoint = PointF.Empty;
            private Rectangle _applyCropBtnRect = Rectangle.Empty;
            private Rectangle _cancelCropBtnRect = Rectangle.Empty;
            private bool _isHoverApplyCrop = false;
            private bool _isHoverCancelCrop = false;

            // Scrollbar state
            private const int ScrollBarThickness = 10;
            private const int ScrollBarMargin = 3;

            private Rectangle _vTrackRect = Rectangle.Empty;
            private Rectangle _vThumbRect = Rectangle.Empty;
            private bool _isHoverVTrack = false;
            private bool _isHoverVThumb = false;
            private bool _isDraggingVThumb = false;
            private int _vThumbDragGrabY = 0;

            private Rectangle _hTrackRect = Rectangle.Empty;
            private Rectangle _hThumbRect = Rectangle.Empty;
            private bool _isHoverHTrack = false;
            private bool _isHoverHThumb = false;
            private bool _isDraggingHThumb = false;
            private int _hThumbDragGrabX = 0;

            public bool HasCropSelection
            {
                get { return _cropRect.Width > 5 && _cropRect.Height > 5; }
            }

            public RectangleF CropRect
            {
                get { return _cropRect; }
            }

            public void ClearCrop()
            {
                _cropRect = RectangleF.Empty;
                _isDraggingCrop = false;
                this.Invalidate();
            }

            public CanvasControl(ScreenshotView parent)
            {
                _parent = parent;
                this.SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.Selectable,
                    true);
                this.DoubleBuffered = true;
                this.Cursor = (_parent._currentTool == AnnotationTool.Hand) ? Cursors.Hand : Cursors.Default;
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                if (this.Width > 50 && this.Height > 50)
                {
                    _parent._isInitialFitDone = true;
                    _parent.FitImageToWindow();
                }
            }

            protected override void OnMouseDoubleClick(MouseEventArgs e)
            {
                base.OnMouseDoubleClick(e);
                _parent.FitImageToWindow();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);

                if (_parent._baseImage == null) return;

                bool isShift = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
                bool isAlt = (Control.ModifierKeys & Keys.Alt) == Keys.Alt;
                float scaledW = _parent._baseImage.Width * _parent.Zoom;
                float scaledH = _parent._baseImage.Height * _parent.Zoom;

                if (isShift && scaledW > this.Width)
                {
                    // Shift + wheel: horizontal scroll
                    int scrollAmount = e.Delta > 0 ? 80 : -80;
                    float newX = _parent.PanOffset.X + scrollAmount;
                    float minX = this.Width - scaledW - 20;
                    float maxX = 20;
                    if (minX < maxX)
                    {
                        newX = Math.Max(minX, Math.Min(maxX, newX));
                    }
                    _parent.PanOffset = new PointF(newX, _parent.PanOffset.Y);
                    this.Invalidate();
                }
                else if (isAlt && scaledH > this.Height)
                {
                    // Alt + wheel: vertical scroll
                    int scrollAmount = e.Delta > 0 ? 80 : -80;
                    float newY = _parent.PanOffset.Y + scrollAmount;
                    float minY = this.Height - scaledH - 20;
                    float maxY = 20;
                    if (minY < maxY)
                    {
                        newY = Math.Max(minY, Math.Min(maxY, newY));
                    }
                    _parent.PanOffset = new PointF(_parent.PanOffset.X, newY);
                    this.Invalidate();
                }
                else
                {
                    // Standard wheel always zooms in/out centered on viewport, even when zoomed in
                    float oldZoom = _parent.Zoom;
                    float zoomFactor = e.Delta > 0 ? 1.15f : 0.85f;
                    float newZoom = Math.Max(0.1f, Math.Min(5.0f, oldZoom * zoomFactor));
                    _parent.SetZoom(newZoom, true);
                }
            }

            private void UpdateScrollBarsGeometry(out bool showV, out bool showH)
            {
                showV = false;
                showH = false;
                _vTrackRect = Rectangle.Empty;
                _vThumbRect = Rectangle.Empty;
                _hTrackRect = Rectangle.Empty;
                _hThumbRect = Rectangle.Empty;

                if (_parent._baseImage == null || this.Width <= 20 || this.Height <= 20)
                    return;

                float scaledW = _parent._baseImage.Width * _parent.Zoom;
                float scaledH = _parent._baseImage.Height * _parent.Zoom;

                showV = scaledH > this.Height;
                showH = scaledW > this.Width;

                if (!showV && !showH)
                    return;

                int vTrackH = this.Height - (ScrollBarMargin * 2) - (showH ? ScrollBarThickness + ScrollBarMargin : 0);
                int hTrackW = this.Width - (ScrollBarMargin * 2) - (showV ? ScrollBarThickness + ScrollBarMargin : 0);

                if (showV && vTrackH > 30)
                {
                    _vTrackRect = new Rectangle(this.Width - ScrollBarThickness - ScrollBarMargin, ScrollBarMargin, ScrollBarThickness, vTrackH);

                    float ratio = (float)this.Height / scaledH;
                    int thumbH = Math.Max(28, (int)(_vTrackRect.Height * ratio));
                    if (thumbH > _vTrackRect.Height) thumbH = _vTrackRect.Height;

                    float maxY = 20f;
                    float minY = this.Height - scaledH - 20f;
                    float scrollRange = maxY - minY;

                    float t = 0f;
                    if (scrollRange > 0.001f)
                    {
                        t = (maxY - _parent.PanOffset.Y) / scrollRange;
                        t = Math.Max(0f, Math.Min(1f, t));
                    }

                    int thumbY = _vTrackRect.Y + (int)(t * (_vTrackRect.Height - thumbH));
                    _vThumbRect = new Rectangle(_vTrackRect.X, thumbY, _vTrackRect.Width, thumbH);
                }

                if (showH && hTrackW > 30)
                {
                    _hTrackRect = new Rectangle(ScrollBarMargin, this.Height - ScrollBarThickness - ScrollBarMargin, hTrackW, ScrollBarThickness);

                    float ratio = (float)this.Width / scaledW;
                    int thumbW = Math.Max(28, (int)(_hTrackRect.Width * ratio));
                    if (thumbW > _hTrackRect.Width) thumbW = _hTrackRect.Width;

                    float maxX = 20f;
                    float minX = this.Width - scaledW - 20f;
                    float scrollRange = maxX - minX;

                    float t = 0f;
                    if (scrollRange > 0.001f)
                    {
                        t = (maxX - _parent.PanOffset.X) / scrollRange;
                        t = Math.Max(0f, Math.Min(1f, t));
                    }

                    int thumbX = _hTrackRect.X + (int)(t * (_hTrackRect.Width - thumbW));
                    _hThumbRect = new Rectangle(thumbX, _hTrackRect.Y, thumbW, _hTrackRect.Height);
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                this.Focus();

                // Middle or Right click = pan
                if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right)
                {
                    _parent._isPanning = true;
                    _parent.PanStartMouse = e.Location;
                    _parent.PanStartOffset = _parent.PanOffset;
                    this.Cursor = Cursors.Hand;
                    return;
                }

                // Left click
                if (e.Button == MouseButtons.Left && _parent._baseImage != null)
                {
                    bool showV, showH;
                    UpdateScrollBarsGeometry(out showV, out showH);

                    // 1. Check Vertical Scrollbar click
                    if (showV)
                    {
                        if (_vThumbRect.Contains(e.Location))
                        {
                            _isDraggingVThumb = true;
                            _vThumbDragGrabY = e.Y - _vThumbRect.Y;
                            this.Capture = true;
                            this.Invalidate();
                            return;
                        }
                        else if (_vTrackRect.Contains(e.Location))
                        {
                            float scaledH = _parent._baseImage.Height * _parent.Zoom;
                            int thumbH = _vThumbRect.Height;
                            int targetThumbY = e.Y - (thumbH / 2);
                            targetThumbY = Math.Max(_vTrackRect.Y, Math.Min(_vTrackRect.Bottom - thumbH, targetThumbY));

                            float t = (_vTrackRect.Height > thumbH)
                                ? (float)(targetThumbY - _vTrackRect.Y) / (_vTrackRect.Height - thumbH)
                                : 0f;
                            t = Math.Max(0f, Math.Min(1f, t));

                            float maxY = 20f;
                            float minY = this.Height - scaledH - 20f;
                            _parent.PanOffset = new PointF(_parent.PanOffset.X, maxY - t * (maxY - minY));

                            _isDraggingVThumb = true;
                            _vThumbDragGrabY = thumbH / 2;
                            this.Capture = true;
                            this.Invalidate();
                            return;
                        }
                    }

                    // 2. Check Horizontal Scrollbar click
                    if (showH)
                    {
                        if (_hThumbRect.Contains(e.Location))
                        {
                            _isDraggingHThumb = true;
                            _hThumbDragGrabX = e.X - _hThumbRect.X;
                            this.Capture = true;
                            this.Invalidate();
                            return;
                        }
                        else if (_hTrackRect.Contains(e.Location))
                        {
                            float scaledW = _parent._baseImage.Width * _parent.Zoom;
                            int thumbW = _hThumbRect.Width;
                            int targetThumbX = e.X - (thumbW / 2);
                            targetThumbX = Math.Max(_hTrackRect.X, Math.Min(_hTrackRect.Right - thumbW, targetThumbX));

                            float t = (_hTrackRect.Width > thumbW)
                                ? (float)(targetThumbX - _hTrackRect.X) / (_hTrackRect.Width - thumbW)
                                : 0f;
                            t = Math.Max(0f, Math.Min(1f, t));

                            float maxX = 20f;
                            float minX = this.Width - scaledW - 20f;
                            _parent.PanOffset = new PointF(maxX - t * (maxX - minX), _parent.PanOffset.Y);

                            _isDraggingHThumb = true;
                            _hThumbDragGrabX = thumbW / 2;
                            this.Capture = true;
                            this.Invalidate();
                            return;
                        }
                    }

                    // 3. Crop mode button clicks
                    if (_parent._currentTool == AnnotationTool.Crop && HasCropSelection)
                    {
                        if (_applyCropBtnRect.Contains(e.Location))
                        {
                            _parent.ApplyCrop(Rectangle.Round(_cropRect));
                            ClearCrop();
                            return;
                        }
                        if (_cancelCropBtnRect.Contains(e.Location))
                        {
                            ClearCrop();
                            _parent._currentTool = AnnotationTool.Hand;
                            if (_parent._headerBar != null) _parent._headerBar.Invalidate();
                            return;
                        }
                    }

                    // Hand tool drag pan
                    if (_parent._currentTool == AnnotationTool.Hand)
                    {
                        _parent._isPanning = true;
                        _parent.PanStartMouse = e.Location;
                        _parent.PanStartOffset = _parent.PanOffset;
                        this.Cursor = Cursors.Hand;
                        return;
                    }

                    RectangleF imgScreenBounds = new RectangleF(
                        _parent.PanOffset.X,
                        _parent.PanOffset.Y,
                        _parent._baseImage.Width * _parent._zoom,
                        _parent._baseImage.Height * _parent._zoom
                    );

                    // If user clicked outside the image in the dark canvas, pan instead of drawing
                    if (!imgScreenBounds.Contains(e.Location))
                    {
                        _parent._isPanning = true;
                        _parent.PanStartMouse = e.Location;
                        _parent.PanStartOffset = _parent.PanOffset;
                        this.Cursor = Cursors.Hand;
                        return;
                    }

                    PointF imgPt = _parent.ScreenToImagePoint(e.Location);

                    // Crop tool dragging
                    if (_parent._currentTool == AnnotationTool.Crop)
                    {
                        _isDraggingCrop = true;
                        _cropStartPoint = imgPt;
                        _cropRect = new RectangleF(imgPt.X, imgPt.Y, 0, 0);
                        this.Invalidate();
                        return;
                    }

                    // Drawing annotations
                    _parent._isDrawing = true;
                    _parent.DrawStartPoint = imgPt;
                    _parent.DrawCurrentPoint = imgPt;

                    switch (_parent._currentTool)
                    {
                        case AnnotationTool.Pen:
                            PenAnnotation pa = new PenAnnotation();
                            pa.Color = _parent._currentColor;
                            pa.StrokeWidth = _parent._currentStrokeWidth;
                            pa.Points.Add(imgPt);
                            _parent._tempAnnotation = pa;
                            break;

                        case AnnotationTool.Highlighter:
                            HighlighterAnnotation ha = new HighlighterAnnotation();
                            ha.Color = _parent._currentColor;
                            ha.StrokeWidth = 18f;
                            ha.Points.Add(imgPt);
                            _parent._tempAnnotation = ha;
                            break;

                        case AnnotationTool.Arrow:
                            ArrowAnnotation aa = new ArrowAnnotation();
                            aa.Color = _parent._currentColor;
                            aa.StrokeWidth = _parent._currentStrokeWidth;
                            aa.StartPoint = imgPt;
                            aa.EndPoint = imgPt;
                            _parent._tempAnnotation = aa;
                            break;

                        case AnnotationTool.Rectangle:
                            RectAnnotation ra = new RectAnnotation();
                            ra.Color = _parent._currentColor;
                            ra.StrokeWidth = _parent._currentStrokeWidth;
                            ra.Rect = new RectangleF(imgPt.X, imgPt.Y, 0, 0);
                            _parent._tempAnnotation = ra;
                            break;
                    }

                    this.Invalidate();
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);

                if (_isDraggingVThumb)
                {
                    float scaledH = _parent._baseImage.Height * _parent.Zoom;
                    int thumbH = _vThumbRect.Height;
                    int newThumbY = e.Y - _vThumbDragGrabY;
                    newThumbY = Math.Max(_vTrackRect.Y, Math.Min(_vTrackRect.Bottom - thumbH, newThumbY));

                    float t = (_vTrackRect.Height > thumbH)
                        ? (float)(newThumbY - _vTrackRect.Y) / (_vTrackRect.Height - thumbH)
                        : 0f;
                    t = Math.Max(0f, Math.Min(1f, t));
                    float maxY = 20f;
                    float minY = this.Height - scaledH - 20f;
                    _parent.PanOffset = new PointF(_parent.PanOffset.X, maxY - t * (maxY - minY));
                    this.Invalidate();
                    return;
                }

                if (_isDraggingHThumb)
                {
                    float scaledW = _parent._baseImage.Width * _parent.Zoom;
                    int thumbW = _hThumbRect.Width;
                    int newThumbX = e.X - _hThumbDragGrabX;
                    newThumbX = Math.Max(_hTrackRect.X, Math.Min(_hTrackRect.Right - thumbW, newThumbX));

                    float t = (_hTrackRect.Width > thumbW)
                        ? (float)(newThumbX - _hTrackRect.X) / (_hTrackRect.Width - thumbW)
                        : 0f;
                    t = Math.Max(0f, Math.Min(1f, t));
                    float maxX = 20f;
                    float minX = this.Width - scaledW - 20f;
                    _parent.PanOffset = new PointF(maxX - t * (maxX - minX), _parent.PanOffset.Y);
                    this.Invalidate();
                    return;
                }

                bool showV, showH;
                UpdateScrollBarsGeometry(out showV, out showH);

                bool hoverVT = showV && _vTrackRect.Contains(e.Location);
                bool hoverVTh = showV && _vThumbRect.Contains(e.Location);
                bool hoverHT = showH && _hTrackRect.Contains(e.Location);
                bool hoverHTh = showH && _hThumbRect.Contains(e.Location);

                if (hoverVT != _isHoverVTrack || hoverVTh != _isHoverVThumb ||
                    hoverHT != _isHoverHTrack || hoverHTh != _isHoverHThumb)
                {
                    _isHoverVTrack = hoverVT;
                    _isHoverVThumb = hoverVTh;
                    _isHoverHTrack = hoverHT;
                    _isHoverHThumb = hoverHTh;
                    this.Invalidate();
                }

                if (hoverVTh || hoverHTh || hoverVT || hoverHT)
                {
                    this.Cursor = Cursors.Default;
                    return;
                }

                if (_parent._isPanning)
                {
                    Point startMouse = _parent.PanStartMouse;
                    PointF startOffset = _parent.PanStartOffset;
                    float dx = e.X - startMouse.X;
                    float dy = e.Y - startMouse.Y;
                    float newX = startOffset.X + dx;
                    float newY = startOffset.Y + dy;

                    float scaledW = _parent._baseImage.Width * _parent.Zoom;
                    float scaledH = _parent._baseImage.Height * _parent.Zoom;

                    if (scaledW > this.Width)
                    {
                        float minX = this.Width - scaledW - 20;
                        float maxX = 20;
                        newX = Math.Max(minX, Math.Min(maxX, newX));
                    }
                    else
                    {
                        newX = (this.Width - scaledW) / 2.0f;
                    }

                    if (scaledH > this.Height)
                    {
                        float minY = this.Height - scaledH - 20;
                        float maxY = 20;
                        newY = Math.Max(minY, Math.Min(maxY, newY));
                    }
                    else
                    {
                        newY = (this.Height - scaledH) / 2.0f;
                    }

                    if (newX != _parent.PanOffset.X || newY != _parent.PanOffset.Y)
                    {
                        _parent.PanOffset = new PointF(newX, newY);
                        this.Invalidate();
                    }
                    return;
                }

                // Crop tool dragging
                if (_isDraggingCrop)
                {
                    PointF cur = _parent.ScreenToImagePoint(e.Location);
                    float x = Math.Min(_cropStartPoint.X, cur.X);
                    float y = Math.Min(_cropStartPoint.Y, cur.Y);
                    float w = Math.Abs(_cropStartPoint.X - cur.X);
                    float h = Math.Abs(_cropStartPoint.Y - cur.Y);
                    _cropRect = new RectangleF(x, y, w, h);
                    this.Invalidate();
                    return;
                }

                if (_parent._currentTool == AnnotationTool.Crop && HasCropSelection)
                {
                    bool hoverApply = _applyCropBtnRect.Contains(e.Location);
                    bool hoverCancel = _cancelCropBtnRect.Contains(e.Location);
                    if (hoverApply != _isHoverApplyCrop || hoverCancel != _isHoverCancelCrop)
                    {
                        _isHoverApplyCrop = hoverApply;
                        _isHoverCancelCrop = hoverCancel;
                        this.Cursor = (hoverApply || hoverCancel) ? Cursors.Hand : Cursors.Cross;
                        this.Invalidate();
                        return;
                    }
                }

                if (!_parent._isDrawing && _parent._baseImage != null)
                {
                    if (_parent._currentTool == AnnotationTool.Hand)
                    {
                        this.Cursor = Cursors.Hand;
                    }
                    else
                    {
                        RectangleF imgScreenBounds = new RectangleF(
                            _parent.PanOffset.X,
                            _parent.PanOffset.Y,
                            _parent._baseImage.Width * _parent._zoom,
                            _parent._baseImage.Height * _parent._zoom
                        );
                        this.Cursor = imgScreenBounds.Contains(e.Location) ? Cursors.Cross : Cursors.Default;
                    }
                }

                if (_parent._isDrawing && _parent._tempAnnotation != null)
                {
                    PointF imgPt = _parent.ScreenToImagePoint(e.Location);
                    _parent.DrawCurrentPoint = imgPt;
                    PointF startPt = _parent.DrawStartPoint;

                    if (_parent._tempAnnotation is PenAnnotation)
                    {
                        ((PenAnnotation)_parent._tempAnnotation).Points.Add(imgPt);
                    }
                    else if (_parent._tempAnnotation is HighlighterAnnotation)
                    {
                        ((HighlighterAnnotation)_parent._tempAnnotation).Points.Add(imgPt);
                    }
                    else if (_parent._tempAnnotation is ArrowAnnotation)
                    {
                        ((ArrowAnnotation)_parent._tempAnnotation).EndPoint = imgPt;
                    }
                    else if (_parent._tempAnnotation is RectAnnotation)
                    {
                        float x = Math.Min(startPt.X, imgPt.X);
                        float y = Math.Min(startPt.Y, imgPt.Y);
                        float w = Math.Abs(startPt.X - imgPt.X);
                        float h = Math.Abs(startPt.Y - imgPt.Y);
                        ((RectAnnotation)_parent._tempAnnotation).Rect = new RectangleF(x, y, w, h);
                    }

                    this.Invalidate();
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);

                if (_isDraggingVThumb || _isDraggingHThumb)
                {
                    _isDraggingVThumb = false;
                    _isDraggingHThumb = false;
                    this.Capture = false;
                    this.Invalidate();
                    return;
                }

                if (_parent._isPanning)
                {
                    _parent._isPanning = false;
                    this.Cursor = (_parent._currentTool == AnnotationTool.Hand) ? Cursors.Hand : Cursors.Default;
                    return;
                }

                if (_isDraggingCrop)
                {
                    _isDraggingCrop = false;
                    if (_cropRect.Width < 5 || _cropRect.Height < 5)
                    {
                        _cropRect = RectangleF.Empty;
                    }
                    this.Invalidate();
                    return;
                }

                if (_parent._isDrawing && _parent._tempAnnotation != null)
                {
                    _parent._isDrawing = false;

                    // Push current state to undo stack before committing
                    _parent._undoStack.Push(new UndoStep
                    {
                        IsCrop = false,
                        PreviousImage = null,
                        Annotations = new List<ScreenshotAnnotation>(_parent._annotations)
                    });
                    _parent._redoStack.Clear();

                    _parent._annotations.Add(_parent._tempAnnotation);
                    _parent._tempAnnotation = null;
                    this.Invalidate();
                    if (_parent._headerBar != null) _parent._headerBar.Invalidate();
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (_isHoverVTrack || _isHoverVThumb || _isHoverHTrack || _isHoverHThumb)
                {
                    _isHoverVTrack = false;
                    _isHoverVThumb = false;
                    _isHoverHTrack = false;
                    _isHoverHThumb = false;
                    this.Invalidate();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;

                if (_parent._baseImage == null)
                {
                    using (Font f = new Font("Segoe UI", 11f))
                    using (SolidBrush b = new SolidBrush(Color.Gray))
                    {
                        g.DrawString("No image to preview", f, b, 20, 20);
                    }
                    return;
                }

                if (_parent._isPanning || _isDraggingVThumb || _isDraggingHThumb)
                {
                    g.SmoothingMode = SmoothingMode.HighSpeed;
                    g.InterpolationMode = InterpolationMode.Low;
                    g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                }
                else
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = (_parent._zoom >= 1.0f) ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                }

                // Canvas transform (pan + zoom)
                GraphicsState state = g.Save();
                g.TranslateTransform(_parent.PanOffset.X, _parent.PanOffset.Y);
                g.ScaleTransform(_parent._zoom, _parent._zoom);

                // 1. Image drop shadow
                Rectangle imgRect = new Rectangle(0, 0, _parent._baseImage.Width, _parent._baseImage.Height);
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                {
                    g.FillRectangle(shadowBrush, imgRect.X + 4, imgRect.Y + 4, imgRect.Width, imgRect.Height);
                }

                // 2. Base screenshot image - only draw visible slice to eliminate zoom/pan lag
                float invZ = 1.0f / _parent._zoom;
                float visibleX = -_parent.PanOffset.X * invZ;
                float visibleY = -_parent.PanOffset.Y * invZ;
                float visibleW = this.Width * invZ;
                float visibleH = this.Height * invZ;

                RectangleF imgBounds = new RectangleF(0, 0, _parent._baseImage.Width, _parent._baseImage.Height);
                RectangleF srcRectF = RectangleF.Intersect(imgBounds, new RectangleF(visibleX, visibleY, visibleW, visibleH));

                if (srcRectF.Width > 0 && srcRectF.Height > 0)
                {
                    Rectangle srcRect = Rectangle.Round(srcRectF);
                    srcRect.Intersect(new Rectangle(0, 0, _parent._baseImage.Width, _parent._baseImage.Height));
                    if (srcRect.Width > 0 && srcRect.Height > 0)
                    {
                        g.DrawImage(_parent._baseImage, srcRect, srcRect, GraphicsUnit.Pixel);
                    }
                }

                // Strictly clip all annotations to image boundary
                Rectangle imageBounds = new Rectangle(0, 0, _parent._baseImage.Width, _parent._baseImage.Height);
                g.SetClip(imageBounds);

                // 3. Committed annotations
                for (int i = 0; i < _parent._annotations.Count; i++)
                {
                    _parent._annotations[i].Draw(g, _parent._baseImage);
                }

                // 4. In-progress temp annotation
                if (_parent._tempAnnotation != null)
                {
                    _parent._tempAnnotation.Draw(g, _parent._baseImage);
                }

                g.ResetClip();

                // 5. Image border
                using (Pen borderPen = new Pen(Color.FromArgb(50, 255, 255, 255), 1f / _parent._zoom))
                {
                    g.DrawRectangle(borderPen, 0, 0, _parent._baseImage.Width, _parent._baseImage.Height);
                }

                g.Restore(state);

                // 6. Draw Crop Overlay (if active)
                if (HasCropSelection)
                {
                    DrawCropOverlay(g);
                }

                // 7. Draw custom rounded scrollbars on top of canvas
                DrawScrollBars(g);
            }

            private void DrawScrollBars(Graphics g)
            {
                bool showV, showH;
                UpdateScrollBarsGeometry(out showV, out showH);

                // Vertical Scrollbar
                if (showV && _vThumbRect.Height > 0)
                {
                    UniversalScrollBar.Draw(
                        g,
                        _vTrackRect,
                        _vThumbRect,
                        ScrollBarOrientation.Vertical,
                        _isHoverVTrack,
                        _isHoverVThumb,
                        _isDraggingVThumb);
                }

                // Horizontal Scrollbar
                if (showH && _hThumbRect.Width > 0)
                {
                    UniversalScrollBar.Draw(
                        g,
                        _hTrackRect,
                        _hThumbRect,
                        ScrollBarOrientation.Horizontal,
                        _isHoverHTrack,
                        _isHoverHThumb,
                        _isDraggingHThumb);
                }
            }

            private void DrawCropOverlay(Graphics g)
            {
                float sx = _parent.PanOffset.X + _cropRect.X * _parent._zoom;
                float sy = _parent.PanOffset.Y + _cropRect.Y * _parent._zoom;
                float sw = _cropRect.Width * _parent._zoom;
                float sh = _cropRect.Height * _parent._zoom;
                RectangleF screenCrop = new RectangleF(sx, sy, sw, sh);

                RectangleF imgScreen = new RectangleF(
                    _parent.PanOffset.X,
                    _parent.PanOffset.Y,
                    _parent._baseImage.Width * _parent._zoom,
                    _parent._baseImage.Height * _parent._zoom
                );

                // Shaded region outside crop box
                using (Region r = new Region(imgScreen))
                {
                    r.Exclude(screenCrop);
                    using (SolidBrush shade = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
                    {
                        g.FillRegion(shade, r);
                    }
                }

                // Crisp crop border
                Color accent = ThemeManager.CurrentAccentColor;
                using (Pen cropPen = new Pen(accent, 2f))
                {
                    g.DrawRectangle(cropPen, sx, sy, sw, sh);
                }

                // Dimension pill
                string dimText = string.Format("{0} × {1}", (int)_cropRect.Width, (int)_cropRect.Height);
                using (Font dimFont = new Font("Segoe UI", 9f, FontStyle.Bold))
                {
                    Size dimSize = TextRenderer.MeasureText(dimText, dimFont);
                    Rectangle dimRect = new Rectangle((int)sx + 6, (int)sy + 6, dimSize.Width + 8, dimSize.Height + 4);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 32, 33, 36)))
                    {
                        g.FillRectangle(b, dimRect);
                    }
                    TextRenderer.DrawText(
                        g, dimText, dimFont, dimRect, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }

                // Floating Action Pill below crop box
                int pillW = 160;
                int pillH = 30;
                int pillX = (int)(sx + (sw - pillW) / 2f);
                int pillY = (int)(sy + sh + 8);

                if (pillY + pillH > this.Height - 10)
                {
                    pillY = (int)(sy + sh - pillH - 8);
                }
                if (pillX < 10) pillX = 10;
                if (pillX + pillW > this.Width - 10) pillX = this.Width - pillW - 10;

                Rectangle pillRect = new Rectangle(pillX, pillY, pillW, pillH);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 32, 33, 36)))
                {
                    g.FillRectangle(b, pillRect);
                }
                using (Pen p = new Pen(Color.FromArgb(60, 64, 67), 1f))
                {
                    g.DrawRectangle(p, pillRect);
                }

                // Apply button
                _applyCropBtnRect = new Rectangle(pillX + 4, pillY + 3, 72, 24);
                Color applyBg = _isHoverApplyCrop ? Color.FromArgb(40, 160, 90) : Color.FromArgb(30, 142, 62);
                using (SolidBrush b = new SolidBrush(applyBg))
                {
                    g.FillRectangle(b, _applyCropBtnRect);
                }
                using (Font btnFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(
                        g, "Apply", btnFont, _applyCropBtnRect, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }

                // Cancel button
                _cancelCropBtnRect = new Rectangle(pillX + 84, pillY + 3, 72, 24);
                Color cancelBg = _isHoverCancelCrop ? Color.FromArgb(70, 72, 77) : Color.FromArgb(48, 50, 54);
                using (SolidBrush b = new SolidBrush(cancelBg))
                {
                    g.FillRectangle(b, _cancelCropBtnRect);
                }
                using (Font btnFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
                {
                    TextRenderer.DrawText(
                        g, "Cancel", btnFont, _cancelCropBtnRect, Color.FromArgb(232, 234, 237),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }


        // ==========================================
        // Sleek Minimal Header Bar
        // ==========================================
        private class HeaderBar : Panel
        {
            private ScreenshotView _parent;

            private Dictionary<AnnotationTool, Rectangle> _toolRects = new Dictionary<AnnotationTool, Rectangle>();
            private Dictionary<Color, Rectangle> _colorRects = new Dictionary<Color, Rectangle>();

            // Zoom controls
            private Rectangle _zoomGroupRect = Rectangle.Empty;
            private Rectangle _zoomOutBtnRect = Rectangle.Empty;
            private Rectangle _zoomSliderRect = Rectangle.Empty;
            private Rectangle _zoomInBtnRect = Rectangle.Empty;
            private Rectangle _zoomLabelBtnRect = Rectangle.Empty;
            private Rectangle _zoomFitBtnRect = Rectangle.Empty;
            private bool _isDraggingZoomSlider = false;

            private Rectangle _undoBtnRect = Rectangle.Empty;
            private Rectangle _redoBtnRect = Rectangle.Empty;
            private Rectangle _copyBtnRect = Rectangle.Empty;
            private Rectangle _saveBtnRect = Rectangle.Empty;
            private Rectangle _colorWheelRect = Rectangle.Empty;
            private Color _customColor = Color.Empty;

            private ToolTip _toolTip = new ToolTip();
            private string _lastToolTipText = "";

            private Point _mousePos = Point.Empty;

            // 4 Clean Curated Colors
            private readonly List<Color> _colors = new List<Color>
            {
                Color.FromArgb(234, 67, 53),   // Red
                Color.FromArgb(26, 115, 232),  // Blue
                Color.FromArgb(251, 188, 4),   // Yellow
                Color.White                     // White
            };

            public HeaderBar(ScreenshotView parent)
            {
                _parent = parent;
                this.SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw,
                    true);
                this.DoubleBuffered = true;
                _toolTip.InitialDelay = 300;
                _toolTip.ReshowDelay = 150;
            }

            private float ZoomToSliderT(float zoom)
            {
                zoom = Math.Max(0.1f, Math.Min(5.0f, zoom));
                if (zoom <= 1.0f)
                    return ((zoom - 0.1f) / 0.9f) * 0.5f;
                else
                    return 0.5f + ((zoom - 1.0f) / 4.0f) * 0.5f;
            }

            private float SliderTToZoom(float t)
            {
                t = Math.Max(0f, Math.Min(1f, t));
                if (t <= 0.5f)
                    return 0.1f + (t / 0.5f) * 0.9f;
                else
                    return 1.0f + ((t - 0.5f) / 0.5f) * 4.0f;
            }

            private void UpdateZoomFromSlider(int mouseX)
            {
                if (_zoomSliderRect.Width <= 0) return;
                float t = (float)(mouseX - _zoomSliderRect.X) / _zoomSliderRect.Width;
                t = Math.Max(0f, Math.Min(1f, t));
                float newZoom = SliderTToZoom(t);
                newZoom = (float)Math.Round(newZoom * 100f) / 100f;
                _parent.SetZoom(newZoom, true);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);

                if (_isDraggingZoomSlider)
                {
                    UpdateZoomFromSlider(e.X);
                    return;
                }

                _mousePos = e.Location;

                bool isHand = _zoomOutBtnRect.Contains(_mousePos) ||
                              _zoomInBtnRect.Contains(_mousePos) ||
                              _zoomLabelBtnRect.Contains(_mousePos) ||
                              _zoomFitBtnRect.Contains(_mousePos) ||
                              _zoomSliderRect.Contains(_mousePos) ||
                              _undoBtnRect.Contains(_mousePos) ||
                              _redoBtnRect.Contains(_mousePos) ||
                              _copyBtnRect.Contains(_mousePos) ||
                              _saveBtnRect.Contains(_mousePos) ||
                              _colorWheelRect.Contains(_mousePos);

                if (!isHand)
                {
                    foreach (Rectangle r in _toolRects.Values)
                    {
                        if (r.Contains(_mousePos)) { isHand = true; break; }
                    }
                }
                if (!isHand)
                {
                    foreach (Rectangle r in _colorRects.Values)
                    {
                        if (r.Contains(_mousePos)) { isHand = true; break; }
                    }
                }

                this.Cursor = isHand ? Cursors.Hand : Cursors.Default;
                UpdateToolTip(_mousePos);
                this.Invalidate();
            }

            private void UpdateToolTip(Point pt)
            {
                string text = "";
                foreach (KeyValuePair<AnnotationTool, Rectangle> kvp in _toolRects)
                {
                    if (kvp.Value.Contains(pt))
                    {
                        switch (kvp.Key)
                        {
                            case AnnotationTool.Hand: text = "Hand (Pan)"; break;
                            case AnnotationTool.Pen: text = "Pen"; break;
                            case AnnotationTool.Highlighter: text = "Highlighter"; break;
                            case AnnotationTool.Arrow: text = "Arrow"; break;
                            case AnnotationTool.Rectangle: text = "Rectangle"; break;
                            case AnnotationTool.Crop: text = "Crop"; break;
                        }
                        break;
                    }
                }
                if (string.IsNullOrEmpty(text))
                {
                    if (_colorWheelRect.Contains(pt))
                    {
                        Color cur = _parent._currentColor;
                        bool isCustom = !_colors.Contains(cur);
                        if (isCustom)
                        {
                            text = string.Format("Custom Color: #{0:X2}{1:X2}{2:X2}", cur.R, cur.G, cur.B);
                        }
                        else if (_customColor != Color.Empty)
                        {
                            text = string.Format("Color Wheel (Last: #{0:X2}{1:X2}{2:X2})", _customColor.R, _customColor.G, _customColor.B);
                        }
                        else
                        {
                            text = "Pick Custom Color";
                        }
                    }
                    else if (_undoBtnRect.Contains(pt)) text = "Undo";
                    else if (_redoBtnRect.Contains(pt)) text = "Redo";
                    else if (_copyBtnRect.Contains(pt)) text = "Copy to clipboard";
                    else if (_saveBtnRect.Contains(pt)) text = "Save image";
                    else if (_zoomOutBtnRect.Contains(pt)) text = "Zoom out";
                    else if (_zoomInBtnRect.Contains(pt)) text = "Zoom in";
                    else if (_zoomLabelBtnRect.Contains(pt)) text = "Reset zoom (100%)";
                    else if (_zoomFitBtnRect.Contains(pt)) text = "Fit to window";
                }

                if (text != _lastToolTipText)
                {
                    _lastToolTipText = text;
                    if (!string.IsNullOrEmpty(text))
                        _toolTip.SetToolTip(this, text);
                    else
                        _toolTip.RemoveAll();
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _mousePos = new Point(-1, -1);
                _lastToolTipText = "";
                _toolTip.RemoveAll();
                this.Cursor = Cursors.Default;
                this.Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (_isDraggingZoomSlider)
                {
                    _isDraggingZoomSlider = false;
                    this.Capture = false;
                    this.Invalidate();
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);

                // Tool selection
                foreach (KeyValuePair<AnnotationTool, Rectangle> kvp in _toolRects)
                {
                    if (kvp.Value.Contains(e.Location))
                    {
                        _parent._currentTool = kvp.Key;
                        if (_parent._canvas != null)
                        {
                            if (kvp.Key != AnnotationTool.Crop)
                            {
                                _parent._canvas.ClearCrop();
                            }
                            _parent._canvas.Cursor = (kvp.Key == AnnotationTool.Hand) ? Cursors.Hand : Cursors.Default;
                        }
                        this.Invalidate();
                        return;
                    }
                }

                // Color selection
                foreach (KeyValuePair<Color, Rectangle> kvp in _colorRects)
                {
                    if (kvp.Value.Contains(e.Location))
                    {
                        _parent._currentColor = kvp.Key;
                        this.Invalidate();
                        return;
                    }
                }

                // Color wheel button
                if (_colorWheelRect.Contains(e.Location))
                {
                    using (ColorDialog cd = new ColorDialog())
                    {
                        cd.Color = _parent._currentColor;
                        cd.FullOpen = true;
                        cd.AnyColor = true;
                        if (cd.ShowDialog(this.FindForm()) == DialogResult.OK)
                        {
                            _customColor = cd.Color;
                            _parent._currentColor = cd.Color;
                            this.Invalidate();
                            if (_parent._canvas != null) _parent._canvas.Invalidate();
                        }
                    }
                    return;
                }

                // Zoom controls
                if (_zoomOutBtnRect.Contains(e.Location))
                {
                    _parent.ZoomOut();
                    return;
                }
                if (_zoomInBtnRect.Contains(e.Location))
                {
                    _parent.ZoomIn();
                    return;
                }
                if (_zoomLabelBtnRect.Contains(e.Location))
                {
                    _parent.ResetZoomTo100();
                    return;
                }
                if (_zoomFitBtnRect.Contains(e.Location))
                {
                    _parent.FitImageToWindow();
                    return;
                }
                if (_zoomSliderRect.Contains(e.Location))
                {
                    _isDraggingZoomSlider = true;
                    this.Capture = true;
                    UpdateZoomFromSlider(e.X);
                    return;
                }

                if (_undoBtnRect.Contains(e.Location))
                {
                    _parent.Undo();
                    return;
                }
                if (_redoBtnRect.Contains(e.Location))
                {
                    _parent.Redo();
                    return;
                }
                if (_copyBtnRect.Contains(e.Location))
                {
                    _parent.CopyToClipboard();
                    return;
                }
                if (_saveBtnRect.Contains(e.Location))
                {
                    _parent.SaveToFile();
                    return;
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                int barH = this.Height;
                int yCenter = barH / 2;

                // 1. Left side: Tools (Pen, Highlighter, Arrow, Rectangle, Crop)
                int x = 14;
                _toolRects.Clear();

                DrawToolButton(g, ref x, AnnotationTool.Hand, "hand");
                DrawToolButton(g, ref x, AnnotationTool.Pen, "pencil-simple");
                DrawToolButton(g, ref x, AnnotationTool.Highlighter, "highlighter");
                DrawToolButton(g, ref x, AnnotationTool.Arrow, "arrow-up-right");
                DrawToolButton(g, ref x, AnnotationTool.Rectangle, "square");
                DrawToolButton(g, ref x, AnnotationTool.Crop, "crop");

                x += 6;

                // Subtle vertical separator
                using (Pen sepPen = new Pen(Color.FromArgb(60, 64, 67), 1f))
                {
                    g.DrawLine(sepPen, x, 10, x, barH - 10);
                }
                x += 16;

                // 2. 4 Clean Color Swatches + Color Wheel
                _colorRects.Clear();
                int swatchSize = 16;
                int swatchY = yCenter - (swatchSize / 2);

                for (int i = 0; i < _colors.Count; i++)
                {
                    Color col = _colors[i];
                    Rectangle cRect = new Rectangle(x, swatchY, swatchSize, swatchSize);
                    _colorRects[col] = cRect;

                    using (SolidBrush cb = new SolidBrush(col))
                    {
                        g.FillEllipse(cb, cRect);
                    }

                    if (_parent._currentColor == col)
                    {
                        using (Pen activeRing = new Pen(Color.White, 2f))
                        {
                            Rectangle ring = cRect;
                            ring.Inflate(3, 3);
                            g.DrawEllipse(activeRing, ring);
                        }
                    }
                    else
                    {
                        using (Pen borderPen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
                        {
                            g.DrawEllipse(borderPen, cRect);
                        }
                    }
                    x += 24;
                }

                // Color Wheel Button
                int wheelBtnSize = 24;
                int wheelCircleSize = 18;
                _colorWheelRect = new Rectangle(x, yCenter - (wheelBtnSize / 2), wheelBtnSize, wheelBtnSize);
                bool isCustomColor = !_colors.Contains(_parent._currentColor);
                bool hoverWheel = _colorWheelRect.Contains(_mousePos);

                if (hoverWheel)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(48, 50, 54)))
                    {
                        g.FillPath(b, CreateRoundedPath(_colorWheelRect, 4));
                    }
                }

                Rectangle wheelCircle = new Rectangle(
                    _colorWheelRect.X + (wheelBtnSize - wheelCircleSize) / 2,
                    yCenter - (wheelCircleSize / 2),
                    wheelCircleSize,
                    wheelCircleSize
                );

                Color displayedWheelColor = isCustomColor ? _parent._currentColor : (_customColor != Color.Empty ? _customColor : Color.Empty);
                DrawColorWheelCircle(g, wheelCircle, displayedWheelColor, isCustomColor);

                if (isCustomColor)
                {
                    using (Pen activeRing = new Pen(Color.White, 2f))
                    {
                        Rectangle ring = wheelCircle;
                        ring.Inflate(3, 3);
                        g.DrawEllipse(activeRing, ring);
                    }
                }
                else
                {
                    using (Pen borderPen = new Pen(Color.FromArgb(80, 255, 255, 255), 1f))
                    {
                        g.DrawEllipse(borderPen, wheelCircle);
                    }
                }

                x += wheelBtnSize + 8;
                int leftControlsEndX = x + 8;

                // 3. Right side: Undo, Redo, Save, Copy
                int rightX = this.Width - 14;

                // Copy (Primary Action Button: Icon + Side Label)
                int copyW = 78;
                int copyH = 28;
                rightX -= copyW;
                _copyBtnRect = new Rectangle(rightX, yCenter - (copyH / 2), copyW, copyH);

                Color accent = ThemeManager.CurrentAccentColor;
                bool hoverCopy = _copyBtnRect.Contains(_mousePos);
                Color copyBg = hoverCopy ? UITheme.AdjustBrightness(accent, 0.10f) : accent;
                Color copyFg = UITheme.GetContrastTextColor(copyBg);
                using (Font copyFont = new Font("Segoe UI", 9f, FontStyle.Bold))
                {
                    DrawIconTextButton(g, _copyBtnRect, "copy", "Copy", copyFont, copyBg, copyFg, null);
                }

                rightX -= 8;

                // Save button (Secondary Action Button: Icon + Side Label)
                int saveW = 76;
                int saveH = 28;
                rightX -= saveW;
                _saveBtnRect = new Rectangle(rightX, yCenter - (saveH / 2), saveW, saveH);

                bool hoverSave = _saveBtnRect.Contains(_mousePos);
                Color saveBg = hoverSave ? Color.FromArgb(56, 58, 62) : Color.FromArgb(41, 42, 45);
                using (Font saveFont = new Font("Segoe UI", 9f, FontStyle.Regular))
                {
                    DrawIconTextButton(g, _saveBtnRect, "floppy-disk", "Save", saveFont, saveBg, Color.FromArgb(232, 234, 237), Color.FromArgb(70, 72, 77));
                }

                rightX -= 10;

                // Subtle vertical separator
                using (Pen sepPen = new Pen(Color.FromArgb(60, 64, 67), 1f))
                {
                    g.DrawLine(sepPen, rightX, 10, rightX, barH - 10);
                }

                rightX -= 10;

                // Redo button (Icon-only)
                int redoW = 32;
                int redoH = 28;
                rightX -= redoW;
                _redoBtnRect = new Rectangle(rightX, yCenter - (redoH / 2), redoW, redoH);

                bool canRedo = _parent._redoStack.Count > 0;
                bool hoverRedo = _redoBtnRect.Contains(_mousePos) && canRedo;
                if (hoverRedo)
                {
                    using (SolidBrush btnBrush = new SolidBrush(Color.FromArgb(56, 58, 62)))
                    {
                        g.FillPath(btnBrush, CreateRoundedPath(_redoBtnRect, 4));
                    }
                }
                Color redoIconCol = canRedo ? (hoverRedo ? Color.White : Color.FromArgb(232, 234, 237)) : Color.FromArgb(95, 99, 104);
                MaterialIcons.DrawIcon(g, "arrow-u-up-right", new Rectangle(_redoBtnRect.X + (_redoBtnRect.Width - 16) / 2, yCenter - 8, 16, 16), redoIconCol);

                rightX -= 4;

                // Undo button (Icon-only)
                int undoW = 32;
                int undoH = 28;
                rightX -= undoW;
                _undoBtnRect = new Rectangle(rightX, yCenter - (undoH / 2), undoW, undoH);

                bool canUndo = _parent._undoStack.Count > 0 || _parent._annotations.Count > 0;
                bool hoverUndo = _undoBtnRect.Contains(_mousePos) && canUndo;
                if (hoverUndo)
                {
                    using (SolidBrush btnBrush = new SolidBrush(Color.FromArgb(56, 58, 62)))
                    {
                        g.FillPath(btnBrush, CreateRoundedPath(_undoBtnRect, 4));
                    }
                }
                Color undoIconCol = canUndo ? (hoverUndo ? Color.White : Color.FromArgb(232, 234, 237)) : Color.FromArgb(95, 99, 104);
                MaterialIcons.DrawIcon(g, "arrow-u-up-left", new Rectangle(_undoBtnRect.X + (_undoBtnRect.Width - 16) / 2, yCenter - 8, 16, 16), undoIconCol);

                // 4. Center: Sleek Zoom Control Pill [-] [===•===] [+] [ 100% ] [ Fit ]
                int zoomGroupW = 236;
                int zoomGroupH = 28;
                int zoomGroupY = yCenter - (zoomGroupH / 2);
                int zoomGroupX = (this.Width - zoomGroupW) / 2;

                if (zoomGroupX < leftControlsEndX) zoomGroupX = leftControlsEndX;
                if (zoomGroupX + zoomGroupW > rightX - 10) zoomGroupX = rightX - 10 - zoomGroupW;

                if (rightX - leftControlsEndX >= 160 && zoomGroupX >= leftControlsEndX)
                {
                    _zoomGroupRect = new Rectangle(zoomGroupX, zoomGroupY, zoomGroupW, zoomGroupH);

                    // Pill container background
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(32, 33, 36)))
                    {
                        g.FillPath(b, CreateRoundedPath(_zoomGroupRect, 5));
                    }
                    using (Pen p = new Pen(Color.FromArgb(50, 52, 56), 1f))
                    {
                        g.DrawPath(p, CreateRoundedPath(_zoomGroupRect, 5));
                    }

                    int curX = zoomGroupX + 4;

                    // Zoom Out [-] Button
                    _zoomOutBtnRect = new Rectangle(curX, zoomGroupY + 2, 24, zoomGroupH - 4);
                    bool hoverOut = _zoomOutBtnRect.Contains(_mousePos);
                    if (hoverOut)
                    {
                        using (SolidBrush hb = new SolidBrush(Color.FromArgb(48, 50, 54)))
                        {
                            g.FillPath(hb, CreateRoundedPath(_zoomOutBtnRect, 3));
                        }
                    }
                    int outMidY = _zoomOutBtnRect.Y + (_zoomOutBtnRect.Height / 2);
                    using (Pen mp = new Pen(Color.FromArgb(200, 204, 209), 1.8f))
                    {
                        g.DrawLine(mp, _zoomOutBtnRect.X + 7, outMidY, _zoomOutBtnRect.Right - 7, outMidY);
                    }
                    curX += 24 + 4;

                    // Slider Track & Thumb
                    int trackW = 76;
                    _zoomSliderRect = new Rectangle(curX, zoomGroupY + 4, trackW, zoomGroupH - 8);
                    int sliderMidY = zoomGroupY + (zoomGroupH / 2);

                    // Track background line
                    Rectangle trackLineRect = new Rectangle(curX, sliderMidY - 1, trackW, 3);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(55, 58, 62)))
                    {
                        g.FillRectangle(b, trackLineRect);
                    }

                    // Active filled portion of track
                    float sliderT = ZoomToSliderT(_parent.Zoom);
                    int thumbX = curX + (int)(sliderT * trackW);
                    int fillW = Math.Max(0, thumbX - curX);
                    if (fillW > 0)
                    {
                        Rectangle fillRect = new Rectangle(curX, sliderMidY - 1, fillW, 3);
                        using (SolidBrush fb = new SolidBrush(MaterialColors.AccentBlue))
                        {
                            g.FillRectangle(fb, fillRect);
                        }
                    }

                    // Draggable circular Thumb
                    bool hoverSlider = _zoomSliderRect.Contains(_mousePos) || _isDraggingZoomSlider;
                    Rectangle thumbRect = new Rectangle(thumbX - 5, sliderMidY - 5, 10, 10);
                    Color thumbFill = hoverSlider ? Color.White : Color.FromArgb(220, 225, 230);
                    using (SolidBrush thb = new SolidBrush(thumbFill))
                    {
                        g.FillEllipse(thb, thumbRect);
                    }
                    using (Pen thp = new Pen(hoverSlider ? MaterialColors.AccentBlue : Color.FromArgb(140, 145, 150), 1.2f))
                    {
                        g.DrawEllipse(thp, thumbRect);
                    }
                    curX += trackW + 4;

                    // Zoom In [+] Button
                    _zoomInBtnRect = new Rectangle(curX, zoomGroupY + 2, 24, zoomGroupH - 4);
                    bool hoverIn = _zoomInBtnRect.Contains(_mousePos);
                    if (hoverIn)
                    {
                        using (SolidBrush hb = new SolidBrush(Color.FromArgb(48, 50, 54)))
                        {
                            g.FillPath(hb, CreateRoundedPath(_zoomInBtnRect, 3));
                        }
                    }
                    int inMidX = _zoomInBtnRect.X + (_zoomInBtnRect.Width / 2);
                    int inMidY = _zoomInBtnRect.Y + (_zoomInBtnRect.Height / 2);
                    using (Pen pp = new Pen(Color.FromArgb(200, 204, 209), 1.8f))
                    {
                        g.DrawLine(pp, inMidX - 5, inMidY, inMidX + 5, inMidY);
                        g.DrawLine(pp, inMidX, inMidY - 5, inMidX, inMidY + 5);
                    }
                    curX += 24 + 5;

                    // Subtle separator line
                    using (Pen sep = new Pen(Color.FromArgb(50, 52, 56), 1f))
                    {
                        g.DrawLine(sep, curX, zoomGroupY + 5, curX, zoomGroupY + zoomGroupH - 5);
                    }
                    curX += 6;

                    // Zoom % Label Button (e.g. "100%", "75%") - click to reset
                    int pctW = 42;
                    _zoomLabelBtnRect = new Rectangle(curX, zoomGroupY + 2, pctW, zoomGroupH - 4);
                    bool hoverLabel = _zoomLabelBtnRect.Contains(_mousePos);
                    if (hoverLabel)
                    {
                        using (SolidBrush hb = new SolidBrush(Color.FromArgb(48, 50, 54)))
                        {
                            g.FillPath(hb, CreateRoundedPath(_zoomLabelBtnRect, 3));
                        }
                    }
                    string pctText = string.Format("{0}%", (int)Math.Round(_parent.Zoom * 100f));
                    using (Font pctFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                    {
                        TextRenderer.DrawText(
                            g, pctText, pctFont, _zoomLabelBtnRect, Color.FromArgb(220, 224, 230),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    }
                    curX += pctW + 4;

                    // Fit Button [Fit]
                    int fitW = 32;
                    _zoomFitBtnRect = new Rectangle(curX, zoomGroupY + 2, fitW, zoomGroupH - 4);
                    bool hoverFit = _zoomFitBtnRect.Contains(_mousePos);
                    if (hoverFit)
                    {
                        using (SolidBrush hb = new SolidBrush(Color.FromArgb(48, 50, 54)))
                        {
                            g.FillPath(hb, CreateRoundedPath(_zoomFitBtnRect, 3));
                        }
                    }
                    using (Font fitFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
                    {
                        TextRenderer.DrawText(
                            g, "Fit", fitFont, _zoomFitBtnRect, Color.FromArgb(170, 174, 180),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    }
                }
                else
                {
                    _zoomGroupRect = Rectangle.Empty;
                    _zoomOutBtnRect = Rectangle.Empty;
                    _zoomSliderRect = Rectangle.Empty;
                    _zoomInBtnRect = Rectangle.Empty;
                    _zoomLabelBtnRect = Rectangle.Empty;
                    _zoomFitBtnRect = Rectangle.Empty;
                }

                // Bottom 1px divider
                using (Pen borderPen = new Pen(MaterialColors.Separator, 1f))
                {
                    g.DrawLine(borderPen, 0, barH - 1, this.Width, barH - 1);
                }
            }

            private void DrawToolButton(Graphics g, ref int x, AnnotationTool tool, string iconName)
            {
                int btnW = 32;
                int btnH = 28;
                int y = (this.Height - btnH) / 2;

                Rectangle rect = new Rectangle(x, y, btnW, btnH);
                _toolRects[tool] = rect;

                bool isSelected = _parent._currentTool == tool;
                bool isHovered = rect.Contains(_mousePos);

                Color bg = isSelected ? Color.FromArgb(56, 58, 62) : (isHovered ? Color.FromArgb(48, 50, 54) : Color.Transparent);
                if (bg != Color.Transparent)
                {
                    using (SolidBrush b = new SolidBrush(bg))
                    {
                        g.FillPath(b, CreateRoundedPath(rect, 4));
                    }
                }

                if (isSelected)
                {
                    using (Pen activePen = new Pen(MaterialColors.AccentBlue, 1.5f))
                    {
                        g.DrawPath(activePen, CreateRoundedPath(rect, 4));
                    }
                }

                Color iconCol = isSelected ? Color.White : (isHovered ? Color.FromArgb(232, 234, 237) : Color.FromArgb(170, 174, 180));
                int iconSize = 18;
                int iconX = rect.X + (btnW - iconSize) / 2;
                int iconY = rect.Y + (btnH - iconSize) / 2;
                MaterialIcons.DrawIcon(g, iconName, new Rectangle(iconX, iconY, iconSize, iconSize), iconCol);

                x += btnW + 4;
            }

            private static void DrawColorWheelCircle(Graphics g, Rectangle rect, Color activeColor, bool isCustomColor)
            {
                if (rect.Width <= 0 || rect.Height <= 0) return;

                Color[] hues = new Color[]
                {
                    Color.FromArgb(234, 67, 53),   // Red
                    Color.FromArgb(255, 112, 67),  // Red-Orange
                    Color.FromArgb(251, 188, 4),   // Orange-Yellow
                    Color.FromArgb(255, 235, 59),  // Yellow
                    Color.FromArgb(174, 234, 0),   // Lime
                    Color.FromArgb(52, 168, 83),   // Green
                    Color.FromArgb(0, 188, 212),   // Cyan
                    Color.FromArgb(66, 133, 244),  // Blue
                    Color.FromArgb(92, 107, 192),  // Indigo
                    Color.FromArgb(156, 39, 176),  // Purple
                    Color.FromArgb(233, 30, 99),   // Pink
                    Color.FromArgb(240, 98, 146)   // Rose
                };

                GraphicsState st = g.Save();
                try
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float sliceAngle = 360f / hues.Length;
                    for (int i = 0; i < hues.Length; i++)
                    {
                        using (SolidBrush b = new SolidBrush(hues[i]))
                        {
                            g.FillPie(b, rect.X, rect.Y, rect.Width, rect.Height, i * sliceAngle, sliceAngle + 0.5f);
                        }
                    }

                    // Prominent center disc showing the active/selected custom color
                    int centerD = Math.Max(6, (int)Math.Round(rect.Width * 0.55));
                    Rectangle centerR = new Rectangle(
                        rect.X + (rect.Width - centerD) / 2,
                        rect.Y + (rect.Height - centerD) / 2,
                        centerD,
                        centerD
                    );

                    Color centerColor = (activeColor != Color.Empty && activeColor.A > 0)
                        ? activeColor
                        : Color.FromArgb(32, 33, 36);

                    using (SolidBrush cb = new SolidBrush(centerColor))
                    {
                        g.FillEllipse(cb, centerR);
                    }

                    using (Pen cp = new Pen(Color.FromArgb(160, 255, 255, 255), 1.2f))
                    {
                        g.DrawEllipse(cp, centerR);
                    }
                }
                finally
                {
                    g.Restore(st);
                }
            }

            private static void DrawIconTextButton(Graphics g, Rectangle btnRect, string iconName, string text, Font font, Color bgColor, Color fgColor, Color? borderColor)
            {
                using (SolidBrush b = new SolidBrush(bgColor))
                {
                    g.FillPath(b, CreateRoundedPath(btnRect, 4));
                }

                if (borderColor.HasValue && borderColor.Value != Color.Transparent)
                {
                    using (Pen p = new Pen(borderColor.Value, 1f))
                    {
                        g.DrawPath(p, CreateRoundedPath(btnRect, 4));
                    }
                }

                int iconSize = 16;
                int gap = 6;
                Size textSize = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int totalW = iconSize + gap + textSize.Width;
                int startX = btnRect.X + Math.Max(2, (btnRect.Width - totalW) / 2);
                int iconY = btnRect.Y + (btnRect.Height - iconSize) / 2;

                MaterialIcons.DrawIcon(g, iconName, new Rectangle(startX, iconY, iconSize, iconSize), fgColor);

                Rectangle textRect = new Rectangle(startX + iconSize + gap, btnRect.Y, textSize.Width + 2, btnRect.Height);
                TextRenderer.DrawText(
                    g, text, font, textRect, fgColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }

            private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
            {
                return UITheme.CreateRoundedPath(rect, radius);
            }
        }
    }
}
