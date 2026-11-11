using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.AdBlock
{
    public class AdBlockManageFiltersForm : Form
    {
        private ListView _listView;
        private TextBox _searchBox;
        private Button _addBtn;
        private Button _removeBtn;
        private Button _clearBtn;
        private Button _reloadTabBtn;
        private Button _closeBtn;
        private Label _countLabel;
        private readonly Action _reloadTabAction;
        private EventHandler _filtersChangedHandler;

        public AdBlockManageFiltersForm(Action reloadTabAction = null)
        {
            _reloadTabAction = reloadTabAction;
            InitializeComponent();
            RefreshList();
            HookEvents();
        }

        private void HookEvents()
        {
            _filtersChangedHandler = (s, e) =>
            {
                if (this.IsDisposed || !this.IsHandleCreated) return;
                try
                {
                    if (this.InvokeRequired)
                    {
                        this.BeginInvoke(new Action(RefreshList));
                    }
                    else
                    {
                        RefreshList();
                    }
                }
                catch
                {
                }
            };
            AppServices.AdBlock.CustomFiltersChanged += _filtersChangedHandler;
            this.FormClosed += (s, e) =>
            {
                if (_filtersChangedHandler != null)
                {
                    AppServices.AdBlock.CustomFiltersChanged -= _filtersChangedHandler;
                }
            };
        }

        private void InitializeComponent()
        {
            UITheme.ApplyForm(this, AppStrings.AdBlock.ManageFiltersTitle, new Size(720, 500), new Size(600, 380));

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.RowCount = 3;
            mainLayout.ColumnCount = 1;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            this.Controls.Add(mainLayout);

            // Top Header & Search Panel
            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Fill;
            UITheme.ApplyTopToolbar(topPanel);
            topPanel.Padding = new Padding(12, 10, 12, 6);

            Label headerLabel = new Label();
            headerLabel.UseMnemonic = false;
            headerLabel.Text = AppStrings.AdBlock.ManageFiltersHeader;
            headerLabel.Font = UITheme.FontDefaultBold;
            headerLabel.ForeColor = UITheme.TextPrimary;
            headerLabel.Location = new Point(12, 14);
            headerLabel.AutoSize = true;
            topPanel.Controls.Add(headerLabel);

            _searchBox = new TextBox();
            _searchBox.Width = 220;
            _searchBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _searchBox.Location = new Point(topPanel.Width - 236, 12);
            UITheme.ApplyTextBox(_searchBox, AppStrings.AdBlock.SearchPlaceholderFilters);
            _searchBox.TextChanged += (s, e) => RefreshList();
            topPanel.Resize += (s, e) =>
            {
                _searchBox.Location = new Point(topPanel.Width - 236, 12);
            };
            topPanel.Controls.Add(_searchBox);

            mainLayout.Controls.Add(topPanel, 0, 0);

            // Center List
            _listView = new ListView();
            _listView.Dock = DockStyle.Fill;
            _listView.View = View.Details;
            _listView.FullRowSelect = true;
            _listView.GridLines = false;
            _listView.MultiSelect = true;
            _listView.HideSelection = false;
            _listView.Columns.Add(AppStrings.AdBlock.ColRule, 260);
            _listView.Columns.Add(AppStrings.AdBlock.ColDomain, 140);
            _listView.Columns.Add(AppStrings.AdBlock.ColSelector, 180);
            _listView.Columns.Add(AppStrings.AdBlock.ColDateAdded, 110);

            Action autoSizeColumns = () =>
            {
                if (_listView.Columns.Count >= 4 && _listView.ClientSize.Width > 0)
                {
                    int otherWidth = _listView.Columns[1].Width + _listView.Columns[2].Width + _listView.Columns[3].Width;
                    int available = _listView.ClientSize.Width - otherWidth - 4;
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
            _addBtn.Text = AppStrings.AdBlock.BtnAddRule;
            _addBtn.AutoSize = true;
            _addBtn.Height = 28;
            UITheme.ApplyButton(_addBtn, true);
            _addBtn.Click += AddBtn_Click;
            bottomLeft.Controls.Add(_addBtn);

            _removeBtn = new Button();
            _removeBtn.Text = AppStrings.AdBlock.BtnRemoveRule;
            _removeBtn.AutoSize = true;
            _removeBtn.Height = 28;
            _removeBtn.Enabled = false;
            UITheme.ApplyButton(_removeBtn);
            _removeBtn.Click += RemoveBtn_Click;
            bottomLeft.Controls.Add(_removeBtn);

            _clearBtn = new Button();
            _clearBtn.Text = AppStrings.AdBlock.BtnClearRules;
            _clearBtn.AutoSize = true;
            _clearBtn.Height = 28;
            UITheme.ApplyButton(_clearBtn);
            _clearBtn.Click += ClearBtn_Click;
            bottomLeft.Controls.Add(_clearBtn);

            if (_reloadTabAction != null)
            {
                _reloadTabBtn = new Button();
                _reloadTabBtn.Text = AppStrings.AdBlock.BtnReloadPageShort;
                _reloadTabBtn.AutoSize = true;
                _reloadTabBtn.Height = 28;
                UITheme.ApplyButton(_reloadTabBtn);
                _reloadTabBtn.Click += (s, e) =>
                {
                    try { _reloadTabAction(); } catch { }
                };
                bottomLeft.Controls.Add(_reloadTabBtn);
            }

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
            if (_listView.IsDisposed) return;
            string filterText = _searchBox != null ? _searchBox.Text.Trim() : string.Empty;

            _listView.Items.Clear();
            List<CustomFilterRule> rules = AppServices.AdBlock.GetCustomFilters();

            _listView.BeginUpdate();
            int matchedCount = 0;
            for (int i = 0; i < rules.Count; i++)
            {
                CustomFilterRule rule = rules[i];
                if (rule == null) continue;

                if (!string.IsNullOrEmpty(filterText))
                {
                    bool match = (rule.RawRule != null && rule.RawRule.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                 (rule.Domain != null && rule.Domain.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                 (rule.Selector != null && rule.Selector.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!match) continue;
                }

                matchedCount++;
                ListViewItem item = new ListViewItem(rule.RawRule);
                item.Tag = rule.Id;
                item.SubItems.Add(string.IsNullOrEmpty(rule.Domain) ? AppStrings.AdBlock.DomainGlobalAllSites : rule.Domain);
                item.SubItems.Add(rule.Selector);
                item.SubItems.Add(rule.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));

                if (string.IsNullOrEmpty(rule.Domain))
                {
                    item.ForeColor = Color.FromArgb(41, 98, 255); // Blue for global
                }
                else
                {
                    item.ForeColor = Color.FromArgb(40, 167, 69); // Green for site-specific
                }

                _listView.Items.Add(item);
            }
            _listView.EndUpdate();

            if (string.IsNullOrEmpty(filterText))
            {
                _countLabel.Text = string.Format(rules.Count == 1 ? AppStrings.AdBlock.RuleCountFormatSingle : AppStrings.AdBlock.RuleCountFormatPlural, rules.Count);
            }
            else
            {
                _countLabel.Text = string.Format(matchedCount == 1 ? AppStrings.AdBlock.RuleMatchedCountFormatSingle : AppStrings.AdBlock.RuleMatchedCountFormatPlural, matchedCount, rules.Count);
            }

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
                UITheme.ApplyForm(prompt, AppStrings.AdBlock.AddRuleTitle, new Size(460, 220), new Size(460, 220));
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.MaximizeBox = false;
                prompt.MinimizeBox = false;
                prompt.StartPosition = FormStartPosition.CenterParent;

                // Domain label & textbox
                Label domLbl = new Label();
                domLbl.Text = AppStrings.AdBlock.AddRuleDomainPrompt;
                domLbl.Location = new Point(16, 14);
                domLbl.AutoSize = true;
                domLbl.Font = UITheme.FontDefault;
                domLbl.ForeColor = UITheme.TextPrimary;
                prompt.Controls.Add(domLbl);

                TextBox domTxt = new TextBox();
                domTxt.Location = new Point(16, 36);
                domTxt.Width = 410;
                UITheme.ApplyTextBox(domTxt, AppStrings.AdBlock.DomainPlaceholder);
                prompt.Controls.Add(domTxt);

                // Selector label & textbox
                Label selLbl = new Label();
                selLbl.Text = AppStrings.AdBlock.AddRuleSelectorPrompt;
                selLbl.Location = new Point(16, 70);
                selLbl.AutoSize = true;
                selLbl.Font = UITheme.FontDefault;
                selLbl.ForeColor = UITheme.TextPrimary;
                prompt.Controls.Add(selLbl);

                TextBox selTxt = new TextBox();
                selTxt.Location = new Point(16, 92);
                selTxt.Width = 410;
                UITheme.ApplyTextBox(selTxt, AppStrings.AdBlock.SelectorPlaceholder);
                prompt.Controls.Add(selTxt);

                // Buttons
                Button okBtn = new Button();
                okBtn.Text = AppStrings.General.BtnSave;
                okBtn.DialogResult = DialogResult.OK;
                okBtn.Location = new Point(266, 136);
                okBtn.Width = 75;
                UITheme.ApplyButton(okBtn, true);
                prompt.Controls.Add(okBtn);

                Button cancelBtn = new Button();
                cancelBtn.Text = AppStrings.General.BtnCancel;
                cancelBtn.DialogResult = DialogResult.Cancel;
                cancelBtn.Location = new Point(350, 136);
                cancelBtn.Width = 75;
                UITheme.ApplyButton(cancelBtn);
                prompt.Controls.Add(cancelBtn);

                prompt.AcceptButton = okBtn;
                prompt.CancelButton = cancelBtn;

                if (prompt.ShowDialog(this) == DialogResult.OK)
                {
                    string domInput = domTxt.Text.Trim();
                    string selInput = selTxt.Text.Trim();
                    if (!string.IsNullOrEmpty(selInput))
                    {
                        AppServices.AdBlock.AddCustomFilter(domInput, selInput);
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
                string ruleId = _listView.SelectedItems[i].Tag as string;
                if (!string.IsNullOrEmpty(ruleId))
                {
                    AppServices.AdBlock.RemoveCustomFilter(ruleId);
                }
            }
            RefreshList();
        }

        private void ClearBtn_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, AppStrings.AdBlock.ConfirmClearRules, AppStrings.AdBlock.ClearRulesTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                AppServices.AdBlock.ClearCustomFilters();
                RefreshList();
            }
        }
    }
}
