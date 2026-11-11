using System;
using System.Drawing;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.Bookmarks
{
    public class BookmarksForm : Form
    {
        private BookmarksView _view;

        public BookmarksForm(Action<string, bool> openUrlCallback)
        {
            UITheme.ApplyForm(this);
            this.Text = AppStrings.Bookmarks.WindowTitle;
            this.Size = new Size(840, 520);
            this.StartPosition = FormStartPosition.CenterParent;
            this.Font = UITheme.FontDefault;

            _view = new BookmarksView(openUrlCallback, () => this.Close());
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
