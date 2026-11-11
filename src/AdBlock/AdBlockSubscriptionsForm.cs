using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.AdBlock
{
    public class AdBlockSubscriptionsForm : Form
    {
        private ListView _listView;
        private Button _addBtn;
        private Button _removeBtn;
        private Button _updateAllBtn;
        private Button _closeBtn;
        private Label _countLabel;
        private bool _isPopulating;

        public AdBlockSubscriptionsForm()
        {
            InitializeComponent();
            RefreshSubscriptions();

            AppServices.AdBlock.FilterListsUpdated += AdBlock_FilterListsUpdated;
            this.FormClosed += (s, e) => AppServices.AdBlock.FilterListsUpdated -= AdBlock_FilterListsUpdated;
        }

        private void InitializeComponent()
        {
            UITheme.ApplyForm(this, AppStrings.AdBlock.SubscriptionsTitle, new Size(760, 480), new Size(600, 360));

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.RowCount = 3;
            mainLayout.ColumnCount = 1;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            this.Controls.Add(mainLayout);

            // Top Header
            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Fill;
            UITheme.ApplyTopToolbar(topPanel);
            topPanel.Padding = new Padding(14, 8, 14, 6);

            Label headerLabel = new Label();
            headerLabel.UseMnemonic = false;
            headerLabel.Text = AppStrings.AdBlock.SubscriptionsHeader;
            headerLabel.Font = UITheme.FontDefaultBold;
            headerLabel.ForeColor = UITheme.TextPrimary;
            headerLabel.AutoSize = true;
            headerLabel.Location = new Point(14, 8);
            topPanel.Controls.Add(headerLabel);

            Label subLabel = new Label();
            subLabel.UseMnemonic = false;
            subLabel.Text = AppStrings.AdBlock.SubscriptionsDesc;
            subLabel.Font = UITheme.FontSmall;
            subLabel.ForeColor = UITheme.TextSecondary;
            subLabel.AutoSize = true;
            subLabel.Location = new Point(14, 26);
            topPanel.Controls.Add(subLabel);

            mainLayout.Controls.Add(topPanel, 0, 0);

            // Center ListView
            _listView = new ListView();
            _listView.Dock = DockStyle.Fill;
            _listView.View = View.Details;
            _listView.FullRowSelect = true;
            _listView.GridLines = false;
            _listView.CheckBoxes = true;
            _listView.HideSelection = false;
            _listView.Columns.Add(AppStrings.AdBlock.ColListName, 240);
            _listView.Columns.Add(AppStrings.AdBlock.ColGroup, 110);
            _listView.Columns.Add(AppStrings.AdBlock.ColRuleCount, 100);
            _listView.Columns.Add(AppStrings.AdBlock.ColLastUpdated, 140);
            _listView.Columns.Add(AppStrings.AdBlock.ColStatus, 120);

            Action autoSizeColumns = () =>
            {
                if (_listView.Columns.Count >= 5 && _listView.ClientSize.Width > 0)
                {
                    int otherWidth = _listView.Columns[1].Width + _listView.Columns[2].Width + _listView.Columns[3].Width + _listView.Columns[4].Width;
                    int available = _listView.ClientSize.Width - otherWidth - 4;
                    if (available > 150)
                    {
                        _listView.Columns[0].Width = available;
                    }
                }
            };
            _listView.Resize += (s, e) => autoSizeColumns();

            UITheme.ApplyListView(_listView);
            _listView.ItemChecked += ListView_ItemChecked;
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

            _updateAllBtn = new Button();
            _updateAllBtn.Text = AppStrings.AdBlock.BtnUpdateAll;
            _updateAllBtn.AutoSize = true;
            _updateAllBtn.Height = 28;
            UITheme.ApplyButton(_updateAllBtn, true);
            _updateAllBtn.Click += UpdateAllBtn_Click;
            bottomLeft.Controls.Add(_updateAllBtn);

            _addBtn = new Button();
            _addBtn.Text = AppStrings.AdBlock.BtnAddCustom;
            _addBtn.AutoSize = true;
            _addBtn.Height = 28;
            UITheme.ApplyButton(_addBtn);
            _addBtn.Click += AddBtn_Click;
            bottomLeft.Controls.Add(_addBtn);

            _removeBtn = new Button();
            _removeBtn.Text = AppStrings.AdBlock.BtnRemoveSelected;
            _removeBtn.AutoSize = true;
            _removeBtn.Height = 28;
            _removeBtn.Enabled = false;
            UITheme.ApplyButton(_removeBtn);
            _removeBtn.Click += RemoveBtn_Click;
            bottomLeft.Controls.Add(_removeBtn);

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

        private void AdBlock_FilterListsUpdated(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
            {
                try
                {
                    this.BeginInvoke(new Action(RefreshSubscriptions));
                }
                catch
                {
                }
                return;
            }
            RefreshSubscriptions();
        }

        private void RefreshSubscriptions()
        {
            _isPopulating = true;
            _listView.BeginUpdate();
            _listView.Items.Clear();

            List<FilterSubscription> subs = AppServices.AdBlock.GetSubscriptions();
            int totalActiveRules = 0;

            for (int i = 0; i < subs.Count; i++)
            {
                FilterSubscription sub = subs[i];
                ListViewItem item = new ListViewItem(sub.Name);
                item.Tag = sub;
                item.Checked = sub.IsEnabled;

                item.SubItems.Add(sub.Group);
                item.SubItems.Add(sub.RuleCount > 0 ? string.Format("{0:N0}", sub.RuleCount) : AppStrings.General.PlaceholderEmpty);

                string dateStr = sub.LastUpdatedUtc != DateTime.MinValue ?
                    sub.LastUpdatedUtc.ToLocalTime().ToString("g") :
                    AppStrings.AdBlock.StatusBundledSeed;
                item.SubItems.Add(dateStr);

                item.SubItems.Add(sub.LastStatus ?? AppStrings.AdBlock.StatusReady);

                if (sub.IsEnabled)
                {
                    totalActiveRules += sub.RuleCount;
                }
                else
                {
                    item.ForeColor = UITheme.TextMuted;
                }

                _listView.Items.Add(item);
            }

            _listView.EndUpdate();
            _isPopulating = false;

            _countLabel.Text = string.Format(AppStrings.AdBlock.TotalCompiledRulesFormat, AppServices.AdBlock.TotalRuleCount);
            UpdateButtonStates();

            if (!AppServices.AdBlock.IsUpdating)
            {
                _updateAllBtn.Enabled = true;
                _updateAllBtn.Text = AppStrings.AdBlock.BtnUpdateAll;
            }
        }

        private void ListView_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_isPopulating) return;

            FilterSubscription sub = e.Item.Tag as FilterSubscription;
            if (sub != null)
            {
                AppServices.AdBlock.ToggleSubscription(sub.Id, e.Item.Checked);
                e.Item.ForeColor = e.Item.Checked ? UITheme.TextPrimary : UITheme.TextMuted;
            }
        }

        private void UpdateButtonStates()
        {
            bool canRemove = false;
            if (_listView.SelectedItems.Count > 0)
            {
                FilterSubscription sub = _listView.SelectedItems[0].Tag as FilterSubscription;
                if (sub != null && sub.IsCustom)
                {
                    canRemove = true;
                }
            }
            _removeBtn.Enabled = canRemove;
        }

        private void UpdateAllBtn_Click(object sender, EventArgs e)
        {
            _updateAllBtn.Enabled = false;
            _updateAllBtn.Text = AppStrings.AdBlock.StatusUpdating;
            AppServices.AdBlock.CheckForUpdatesInBackground(true);
        }

        private void AddBtn_Click(object sender, EventArgs e)
        {
            using (Form prompt = new Form())
            {
                UITheme.ApplyForm(prompt, AppStrings.AdBlock.AddCustomTitle, new Size(460, 210), new Size(460, 210));
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.MaximizeBox = false;
                prompt.MinimizeBox = false;
                prompt.StartPosition = FormStartPosition.CenterParent;

                Label nameLbl = new Label();
                nameLbl.Text = AppStrings.AdBlock.AddCustomNameLabel;
                nameLbl.Location = new Point(16, 16);
                nameLbl.AutoSize = true;
                nameLbl.Font = UITheme.FontDefault;
                nameLbl.ForeColor = UITheme.TextPrimary;

                TextBox nameBox = new TextBox();
                nameBox.Location = new Point(16, 38);
                nameBox.Size = new Size(410, 24);
                nameBox.Text = AppStrings.AdBlock.AddCustomDefaultName;
                UITheme.ApplyTextBox(nameBox);

                Label urlLbl = new Label();
                urlLbl.Text = AppStrings.AdBlock.AddCustomUrlLabel;
                urlLbl.Location = new Point(16, 72);
                urlLbl.AutoSize = true;
                urlLbl.Font = UITheme.FontDefault;
                urlLbl.ForeColor = UITheme.TextPrimary;

                TextBox urlBox = new TextBox();
                urlBox.Location = new Point(16, 94);
                urlBox.Size = new Size(410, 24);
                UITheme.ApplyTextBox(urlBox, "https://example.com/filters.txt");

                Button okBtn = new Button();
                okBtn.Text = AppStrings.AdBlock.BtnSubscribe;
                okBtn.DialogResult = DialogResult.OK;
                okBtn.Location = new Point(236, 134);
                okBtn.Size = new Size(95, 28);
                UITheme.ApplyButton(okBtn, true);

                Button cancelBtn = new Button();
                cancelBtn.Text = AppStrings.General.BtnCancel;
                cancelBtn.DialogResult = DialogResult.Cancel;
                cancelBtn.Location = new Point(340, 134);
                cancelBtn.Size = new Size(86, 28);
                UITheme.ApplyButton(cancelBtn);

                prompt.Controls.Add(nameLbl);
                prompt.Controls.Add(nameBox);
                prompt.Controls.Add(urlLbl);
                prompt.Controls.Add(urlBox);
                prompt.Controls.Add(okBtn);
                prompt.Controls.Add(cancelBtn);
                prompt.AcceptButton = okBtn;
                prompt.CancelButton = cancelBtn;

                if (prompt.ShowDialog(this) == DialogResult.OK)
                {
                    string url = urlBox.Text.Trim();
                    string name = nameBox.Text.Trim();
                    if (!string.IsNullOrEmpty(url))
                    {
                        AppServices.AdBlock.AddCustomSubscription(name, url);
                        RefreshSubscriptions();
                    }
                }
            }
        }

        private void RemoveBtn_Click(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count == 0) return;
            FilterSubscription sub = _listView.SelectedItems[0].Tag as FilterSubscription;
            if (sub == null || !sub.IsCustom) return;

            DialogResult res = MessageBox.Show(
                this,
                string.Format(AppStrings.AdBlock.RemoveConfirmFormat, sub.Name),
                AppStrings.AdBlock.RemoveTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res == DialogResult.Yes)
            {
                AppServices.AdBlock.RemoveSubscription(sub.Id);
                RefreshSubscriptions();
            }
        }
    }
}
