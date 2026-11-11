using System;
using System.Drawing;
using System.Windows.Forms;
using wb.Core;
using wb.UI;
using wb.Browser;

namespace wb.Downloads
{
    public class DownloadsForm : Form
    {
        private static DownloadsForm _activeInstance;
        private DownloadsView _view;

        public static void ShowDownload(DownloadItemModel model = null, IWin32Window owner = null)
        {
            // Find active or top-level MainBrowserForm
            MainBrowserForm targetForm = null;
            if (owner is MainBrowserForm)
            {
                targetForm = (MainBrowserForm)owner;
            }
            else
            {
                Form active = Form.ActiveForm;
                if (active is MainBrowserForm)
                {
                    targetForm = (MainBrowserForm)active;
                }
                else
                {
                    try
                    {
                        foreach (Form f in Application.OpenForms)
                        {
                            if (f is MainBrowserForm && !f.IsDisposed)
                            {
                                targetForm = (MainBrowserForm)f;
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }

            if (targetForm != null && !targetForm.IsDisposed)
            {
                if (targetForm.InvokeRequired)
                {
                    targetForm.BeginInvoke(new Action(() => targetForm.OpenDownloads(model)));
                }
                else
                {
                    targetForm.OpenDownloads(model);
                }
                return;
            }

            // Fallback standalone window if no MainBrowserForm exists
            if (_activeInstance == null || _activeInstance.IsDisposed)
            {
                _activeInstance = new DownloadsForm();
                if (owner != null) _activeInstance.Show(owner);
                else _activeInstance.Show();
            }
            else
            {
                if (_activeInstance.WindowState == FormWindowState.Minimized)
                    _activeInstance.WindowState = FormWindowState.Normal;
                _activeInstance.BringToFront();
                _activeInstance.Activate();
            }

            if (model != null && _activeInstance._view != null)
            {
                _activeInstance._view.SelectAndDisplayDownload(model.Id);
            }
        }

        public DownloadsForm()
        {
            UITheme.ApplyForm(this, AppStrings.Downloads.WindowTitle, new Size(1060, 680), new Size(820, 520));
            _view = new DownloadsView(() => this.Close());
            _view.Dock = DockStyle.Fill;
            this.Controls.Add(_view);
        }

        public void SelectAndDisplayDownload(long id)
        {
            if (_view != null && !_view.IsDisposed)
            {
                _view.SelectAndDisplayDownload(id);
            }
        }

        public void RefreshList()
        {
            if (_view != null && !_view.IsDisposed)
            {
                _view.RefreshList();
            }
        }
    }
}
