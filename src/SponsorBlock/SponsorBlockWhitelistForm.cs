using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.SponsorBlock
{
    /// <summary>
    /// YouTube video exclusion management dialog for SponsorBlock.
    /// Provides listing, adding, removing and clearing whitelisted YouTube videos,
    /// matching the architecture and design language of AdBlockWhitelistForm.
    /// </summary>
    public class SponsorBlockWhitelistForm : Form
    {
        private ListView _listView;
        private Button _addBtn;
        private Button _removeBtn;
        private Button _clearBtn;
        private Button _closeBtn;
        private Label _countLabel;
        private EventHandler _whitelistChangedHandler;

        public SponsorBlockWhitelistForm()
        {
            InitializeComponent();
            HookEvents();
            RefreshList();
        }

        private void InitializeComponent()
        {
            UITheme.ApplyForm(this, AppStrings.SponsorBlock.WhitelistTitle, new Size(660, 480), new Size(540, 340));

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.RowCount = 3;
            mainLayout.ColumnCount = 1;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            this.Controls.Add(mainLayout);

            // Top Header
            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Fill;
            UITheme.ApplyTopToolbar(topPanel);
            topPanel.Padding = new Padding(14, 8, 14, 6);

            Label headerLabel = new Label();
            headerLabel.UseMnemonic = false;
            headerLabel.Text = AppStrings.SponsorBlock.WhitelistHeader;
            headerLabel.Font = UITheme.FontDefaultBold;
            headerLabel.ForeColor = UITheme.TextPrimary;
            headerLabel.AutoSize = true;
            headerLabel.Location = new Point(14, 8);
            topPanel.Controls.Add(headerLabel);

            Label descLabel = new Label();
            descLabel.UseMnemonic = false;
            descLabel.Text = AppStrings.SponsorBlock.WhitelistDesc;
            descLabel.Font = UITheme.FontSmall;
            descLabel.ForeColor = UITheme.TextSecondary;
            descLabel.AutoSize = true;
            descLabel.Location = new Point(14, 28);
            topPanel.Controls.Add(descLabel);

            mainLayout.Controls.Add(topPanel, 0, 0);

            // Center List
            _listView = new ListView();
            _listView.Dock = DockStyle.Fill;
            _listView.View = View.Details;
            _listView.FullRowSelect = true;
            _listView.GridLines = false;
            _listView.MultiSelect = true;
            _listView.HideSelection = false;
            _listView.Columns.Add(AppStrings.SponsorBlock.WhitelistColVideoId, 460);
            _listView.Columns.Add(AppStrings.SponsorBlock.WhitelistColStatus, 150);

            Action autoSizeColumns = () =>
            {
                if (_listView.Columns.Count >= 2 && _listView.ClientSize.Width > 0)
                {
                    int available = _listView.ClientSize.Width - _listView.Columns[1].Width - 4;
                    if (available > 150)
                    {
                        _listView.Columns[0].Width = available;
                    }
                }
            };
            _listView.Resize += (s, e) => autoSizeColumns();

            UITheme.ApplyListView(_listView);
            _listView.SelectedIndexChanged += (s, e) => UpdateButtonStates();
            _listView.KeyDown += ListView_KeyDown;
            _listView.DoubleClick += (s, e) => CopySelectedUrl();

            // Context Menu
            ContextMenuStrip contextMenu = new ContextMenuStrip();
            ToolStripMenuItem copyUrlItem = new ToolStripMenuItem(AppStrings.SponsorBlock.WhitelistMenuCopyUrl);
            copyUrlItem.Click += (s, e) => CopySelectedUrl();
            contextMenu.Items.Add(copyUrlItem);

            ToolStripMenuItem copyIdItem = new ToolStripMenuItem(AppStrings.SponsorBlock.WhitelistMenuCopyId);
            copyIdItem.Click += (s, e) => CopySelectedId();
            contextMenu.Items.Add(copyIdItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem removeItem = new ToolStripMenuItem(AppStrings.SponsorBlock.WhitelistBtnRemove);
            removeItem.Click += RemoveBtn_Click;
            contextMenu.Items.Add(removeItem);

            _listView.ContextMenuStrip = contextMenu;
            mainLayout.Controls.Add(_listView, 0, 1);

            // Bottom Actions Bar
            TableLayoutPanel bottomLayout = new TableLayoutPanel();
            UITheme.ApplyBottomBar(bottomLayout);
            bottomLayout.ColumnStyles.Clear();
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            FlowLayoutPanel bottomLeft = new FlowLayoutPanel();
            bottomLeft.Dock = DockStyle.Fill;
            bottomLeft.AutoSize = true;
            bottomLeft.WrapContents = false;
            bottomLeft.Margin = new Padding(0);

            _addBtn = new Button();
            _addBtn.UseMnemonic = false;
            _addBtn.Text = AppStrings.SponsorBlock.WhitelistBtnAdd;
            _addBtn.AutoSize = true;
            _addBtn.Height = 28;
            UITheme.ApplyButton(_addBtn, true);
            _addBtn.Click += AddBtn_Click;
            bottomLeft.Controls.Add(_addBtn);

            _removeBtn = new Button();
            _removeBtn.UseMnemonic = false;
            _removeBtn.Text = AppStrings.SponsorBlock.WhitelistBtnRemove;
            _removeBtn.AutoSize = true;
            _removeBtn.Height = 28;
            _removeBtn.Enabled = false;
            UITheme.ApplyButton(_removeBtn);
            _removeBtn.Click += RemoveBtn_Click;
            bottomLeft.Controls.Add(_removeBtn);

            _clearBtn = new Button();
            _clearBtn.UseMnemonic = false;
            _clearBtn.Text = AppStrings.SponsorBlock.WhitelistBtnClear;
            _clearBtn.AutoSize = true;
            _clearBtn.Height = 28;
            UITheme.ApplyButton(_clearBtn);
            _clearBtn.Click += ClearBtn_Click;
            bottomLeft.Controls.Add(_clearBtn);

            FlowLayoutPanel bottomRight = new FlowLayoutPanel();
            bottomRight.Dock = DockStyle.Fill;
            bottomRight.AutoSize = true;
            bottomRight.WrapContents = false;
            bottomRight.FlowDirection = FlowDirection.RightToLeft;
            bottomRight.Margin = new Padding(0);

            _closeBtn = new Button();
            _closeBtn.UseMnemonic = false;
            _closeBtn.Text = AppStrings.General.BtnClose;
            _closeBtn.Width = 80;
            _closeBtn.Height = 28;
            UITheme.ApplyButton(_closeBtn);
            _closeBtn.Click += (s, e) => this.Close();
            bottomRight.Controls.Add(_closeBtn);

            _countLabel = new Label();
            _countLabel.UseMnemonic = false;
            _countLabel.AutoSize = true;
            _countLabel.Font = UITheme.FontSmall;
            _countLabel.ForeColor = UITheme.TextSecondary;
            _countLabel.Margin = new Padding(6, 8, 12, 0);
            bottomRight.Controls.Add(_countLabel);

            bottomLayout.Controls.Add(bottomLeft, 0, 0);
            bottomLayout.Controls.Add(bottomRight, 1, 0);
            mainLayout.Controls.Add(bottomLayout, 0, 2);
        }

        private void HookEvents()
        {
            _whitelistChangedHandler = (s, e) =>
            {
                if (this.IsDisposed) return;
                if (this.InvokeRequired)
                {
                    try { this.BeginInvoke(new Action(RefreshList)); } catch { }
                    return;
                }
                RefreshList();
            };

            AppServices.SponsorBlock.WhitelistChanged += _whitelistChangedHandler;
            this.FormClosed += (s, e) =>
            {
                if (_whitelistChangedHandler != null)
                {
                    AppServices.SponsorBlock.WhitelistChanged -= _whitelistChangedHandler;
                    _whitelistChangedHandler = null;
                }
            };
        }

        private void RefreshList()
        {
            _listView.Items.Clear();
            List<string> videos = AppServices.SponsorBlock.GetWhitelistedVideos();

            _listView.BeginUpdate();
            for (int i = 0; i < videos.Count; i++)
            {
                string vid = videos[i];
                string displayUrl = "https://www.youtube.com/watch?v=" + vid;
                ListViewItem item = new ListViewItem(displayUrl);
                item.Tag = vid;
                item.SubItems.Add(AppStrings.SponsorBlock.WhitelistStatusPaused);
                item.ForeColor = Color.FromArgb(180, 40, 40);
                _listView.Items.Add(item);
            }
            _listView.EndUpdate();

            _countLabel.Text = string.Format(AppStrings.SponsorBlock.WhitelistVideoCountFormat, videos.Count, videos.Count == 1 ? "" : "s");
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            _removeBtn.Enabled = _listView.SelectedItems.Count > 0;
            _clearBtn.Enabled = _listView.Items.Count > 0;
        }

        private void AddBtn_Click(object sender, EventArgs e)
        {
            using (Form prompt = new Form())
            {
                UITheme.ApplyForm(prompt, AppStrings.SponsorBlock.WhitelistAddTitle, new Size(460, 165), new Size(460, 165));
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.MaximizeBox = false;
                prompt.MinimizeBox = false;
                prompt.StartPosition = FormStartPosition.CenterParent;

                Label lbl = new Label();
                lbl.UseMnemonic = false;
                lbl.Text = AppStrings.SponsorBlock.WhitelistAddPrompt;
                lbl.Location = new Point(16, 16);
                lbl.AutoSize = true;
                lbl.Font = UITheme.FontDefault;
                lbl.ForeColor = UITheme.TextPrimary;
                prompt.Controls.Add(lbl);

                TextBox txt = new TextBox();
                txt.Location = new Point(16, 44);
                txt.Width = 410;
                UITheme.ApplyTextBox(txt, AppStrings.SponsorBlock.WhitelistPlaceholder);
                prompt.Controls.Add(txt);

                Button okBtn = new Button();
                okBtn.UseMnemonic = false;
                okBtn.Text = AppStrings.General.BtnSave;
                okBtn.DialogResult = DialogResult.OK;
                okBtn.Location = new Point(266, 82);
                okBtn.Width = 75;
                UITheme.ApplyButton(okBtn, true);
                prompt.Controls.Add(okBtn);

                Button cancelBtn = new Button();
                cancelBtn.UseMnemonic = false;
                cancelBtn.Text = AppStrings.General.BtnCancel;
                cancelBtn.DialogResult = DialogResult.Cancel;
                cancelBtn.Location = new Point(350, 82);
                cancelBtn.Width = 75;
                UITheme.ApplyButton(cancelBtn);
                prompt.Controls.Add(cancelBtn);

                prompt.AcceptButton = okBtn;
                prompt.CancelButton = cancelBtn;

                if (prompt.ShowDialog(this) == DialogResult.OK)
                {
                    string input = txt.Text.Trim();
                    if (!string.IsNullOrEmpty(input))
                    {
                        AppServices.SponsorBlock.AddVideoWhitelist(input);
                        RefreshList();
                    }
                }
            }
        }

        private void RemoveBtn_Click(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count == 0) return;

            for (int i = 0; i < _listView.SelectedItems.Count; i++)
            {
                string vid = _listView.SelectedItems[i].Tag as string ?? _listView.SelectedItems[i].Text;
                AppServices.SponsorBlock.RemoveVideoWhitelist(vid);
            }
            RefreshList();
        }

        private void ClearBtn_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, AppStrings.SponsorBlock.WhitelistConfirmClear, AppStrings.SponsorBlock.WhitelistClearTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                AppServices.SponsorBlock.ClearVideoWhitelist();
                RefreshList();
            }
        }

        private void ListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && _listView.SelectedItems.Count > 0)
            {
                RemoveBtn_Click(sender, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelectedUrl();
                e.Handled = true;
            }
        }

        private void CopySelectedUrl()
        {
            if (_listView.SelectedItems.Count == 0) return;
            string vid = _listView.SelectedItems[0].Tag as string ?? _listView.SelectedItems[0].Text;
            string url = "https://www.youtube.com/watch?v=" + vid;
            ClipboardHelper.SetText(url);
        }

        private void CopySelectedId()
        {
            if (_listView.SelectedItems.Count == 0) return;
            string vid = _listView.SelectedItems[0].Tag as string ?? _listView.SelectedItems[0].Text;
            ClipboardHelper.SetText(vid);
        }
    }
}
