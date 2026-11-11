using System;
using System.Windows.Forms;
using wb.UI;

namespace wb.Screenshot
{
    /// <summary>
    /// Countdown overlay for delayed screenshots.
    /// Delegates directly to the reusable OverlayPopup for consistent visual design and smooth animations.
    /// </summary>
    public class ScreenshotCountdownOverlay
    {
        private int _remainingSeconds;
        private Action _onComplete;
        private Action _onCancel;
        private OverlayPopup _popup;

        public ScreenshotCountdownOverlay(int seconds, Action onComplete, Action onCancel = null)
        {
            _remainingSeconds = Math.Max(1, seconds);
            _onComplete = onComplete;
            _onCancel = onCancel;
        }

        public void Start(Form parentForm)
        {
            _popup = OverlayPopup.ShowCountdown(parentForm, _remainingSeconds, _onComplete, _onCancel);
        }

        public void Cancel()
        {
            if (_popup != null && !_popup.IsDisposed)
            {
                _popup.CancelCountdown();
            }
        }
    }
}
