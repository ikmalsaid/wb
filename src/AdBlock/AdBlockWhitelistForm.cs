using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.AdBlock
{
    public class AdBlockWhitelistForm : Form
    {
        private ListView _listView;
        private Button _addBtn;
        private Button _removeBtn;
        private Button _clearBtn;
        private Button _closeBtn;
        private Label _countLabel;

        public AdBlockWhitelistForm()
        {
            InitializeComponent();
            RefreshList();
        }

        private void InitializeComponent()
        {
            UITheme.ApplyForm(this, AppStrings.AdBlock.WhitelistTitle, new Size(640, 460), new Size(540, 340));

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.RowCount = 3;
            mainLayout.ColumnCount = 1;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            this.Controls.Add(mainLayout);

            // Top Header
            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Fill;
            UITheme.ApplyTopToolbar(topPanel);
            topPanel.Padding = new Padding(12, 10, 12, 6);

            Label headerLabel = new Label();
            headerLabel.Text = AppStrings.AdBlock.WhitelistHeader;
            headerLabel.Font = UITheme.FontDefaultBold;
            headerLabel.ForeColor = UITheme.TextPrimary;
            headerLabel.AutoSize = true;
            headerLabel.Dock = DockStyle.Fill;
            topPanel.Controls.Add(headerLabel);
            mainLayout.Controls.Add(topPanel, 0, 0);

            // Center List
            _listView = new ListView();
            _listView.Dock = DockStyle.Fill;
            _listView.View = View.Details;
            _listView.FullRowSelect = true;
            _listView.GridLines = false;
            _listView.MultiSelect = true;
            _listView.HideSelection = false;
            _listView.Columns.Add(AppStrings.AdBlock.WhitelistColDomain, 440);
            _listView.Columns.Add(AppStrings.AdBlock.ColStatus, 140);

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
            _addBtn.Text = AppStrings.AdBlock.WhitelistBtnAdd;
            _addBtn.AutoSize = true;
            _addBtn.Height = 28;
            UITheme.ApplyButton(_addBtn, true);
            _addBtn.Click += AddBtn_Click;
            bottomLeft.Controls.Add(_addBtn);

            _removeBtn = new Button();
            _removeBtn.Text = AppStrings.AdBlock.WhitelistBtnRemove;
            _removeBtn.AutoSize = true;
            _removeBtn.Height = 28;
            _removeBtn.Enabled = false;
            UITheme.ApplyButton(_removeBtn);
            _removeBtn.Click += RemoveBtn_Click;
            bottomLeft.Controls.Add(_removeBtn);

            _clearBtn = new Button();
            _clearBtn.Text = AppStrings.AdBlock.WhitelistBtnClear;
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
            _closeBtn.Text = AppStrings.General.BtnClose;
            _closeBtn.Width = 80;
            _closeBtn.Height = 28;
            UITheme.ApplyButton(_closeBtn);
            _closeBtn.Click += (s, e) => this.Close();
            bottomRight.Controls.Add(_closeBtn);

            _countLabel = new Label();
            _countLabel.AutoSize = true;
            _countLabel.Font = UITheme.FontSmall;
            _countLabel.ForeColor = UITheme.TextSecondary;
            _countLabel.Margin = new Padding(6, 8, 12, 0);
            bottomRight.Controls.Add(_countLabel);

            bottomLayout.Controls.Add(bottomLeft, 0, 0);
            bottomLayout.Controls.Add(bottomRight, 1, 0);
            mainLayout.Controls.Add(bottomLayout, 0, 2);
        }

        private void RefreshList()
        {
            _listView.Items.Clear();
            List<string> domains = AppServices.AdBlock.GetWhitelistedDomains();

            _listView.BeginUpdate();
            for (int i = 0; i < domains.Count; i++)
            {
                ListViewItem item = new ListViewItem(domains[i]);
                item.SubItems.Add(AppStrings.AdBlock.StatusUBlockPaused);
                item.ForeColor = Color.FromArgb(180, 40, 40);
                _listView.Items.Add(item);
            }
            _listView.EndUpdate();

            _countLabel.Text = string.Format(AppStrings.AdBlock.DomainCountFormat, domains.Count, domains.Count == 1 ? "" : "s");
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
                UITheme.ApplyForm(prompt, AppStrings.AdBlock.WhitelistAddTitle, new Size(420, 160), new Size(420, 160));
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.MaximizeBox = false;
                prompt.MinimizeBox = false;
                prompt.StartPosition = FormStartPosition.CenterParent;

                Label lbl = new Label();
                lbl.Text = AppStrings.AdBlock.WhitelistAddPrompt;
                lbl.Location = new Point(16, 16);
                lbl.AutoSize = true;
                lbl.Font = UITheme.FontDefault;
                lbl.ForeColor = UITheme.TextPrimary;
                prompt.Controls.Add(lbl);

                TextBox txt = new TextBox();
                txt.Location = new Point(16, 42);
                txt.Width = 370;
                UITheme.ApplyTextBox(txt, AppStrings.AdBlock.WhitelistDomainPlaceholder);
                prompt.Controls.Add(txt);

                Button okBtn = new Button();
                okBtn.Text = AppStrings.General.BtnSave;
                okBtn.DialogResult = DialogResult.OK;
                okBtn.Location = new Point(226, 80);
                okBtn.Width = 75;
                UITheme.ApplyButton(okBtn, true);
                prompt.Controls.Add(okBtn);

                Button cancelBtn = new Button();
                cancelBtn.Text = AppStrings.General.BtnCancel;
                cancelBtn.DialogResult = DialogResult.Cancel;
                cancelBtn.Location = new Point(310, 80);
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
                        AppServices.AdBlock.WhitelistDomain(input);
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
                string domain = _listView.SelectedItems[i].Text;
                AppServices.AdBlock.UnwhitelistDomain(domain);
            }
            RefreshList();
        }

        private void ClearBtn_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, AppStrings.AdBlock.ConfirmClearWhitelist, AppStrings.AdBlock.ClearWhitelistTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                AppServices.AdBlock.ClearWhitelist();
                RefreshList();
            }
        }
    }
}
