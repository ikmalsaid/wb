using System;
using System.Drawing;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.History
{
    public class HistoryForm : Form
    {
        private HistoryView _view;

        public HistoryForm(Action<string, bool> openUrlCallback)
        {
            UITheme.ApplyForm(this);
            this.Text = AppStrings.History.WindowTitle;
            this.Size = new Size(900, 540);
            this.StartPosition = FormStartPosition.CenterParent;
            this.Font = UITheme.FontDefault;

            _view = new HistoryView(openUrlCallback, () => this.Close());
            _view.Dock = DockStyle.Fill;
            this.Controls.Add(_view);
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
