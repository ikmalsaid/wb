using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.SponsorBlock
{
    /// <summary>
    /// Configuration management dialog for YouTube SponsorBlock.
    /// Provides category action customization, timeline preview bar toggles,
    /// and skip notification settings.
    /// </summary>
    public class SponsorBlockSettingsForm : Form
    {
        private CheckBox _enableSbCheck;
        private CheckBox _showPreviewBarCheck;
        private CheckBox _showNoticeCheck;
        private CheckBox _audioNotifyCheck;
        private TableLayoutPanel _categoryTable;
        private Dictionary<string, ComboBox> _categoryCombos;
        private Button _resetDefaultsBtn;
        private Button _saveBtn;
        private Button _closeBtn;

        public SponsorBlockSettingsForm()
        {
            InitializeComponent();
            LoadCurrentValues();
        }

        private void InitializeComponent()
        {
            UITheme.ApplyForm(this, AppStrings.SponsorBlock.SettingsTitle, new Size(720, 660), new Size(640, 600));

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.RowCount = 3;
            mainLayout.ColumnCount = 1;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));   // Header
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // Options & Categories
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));   // Bottom Bar
            this.Controls.Add(mainLayout);

            // 1. Top Header
            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Fill;
            UITheme.ApplyTopToolbar(topPanel);
            topPanel.Padding = new Padding(20, 10, 20, 8);

            Label headerLabel = new Label();
            headerLabel.UseMnemonic = false;
            headerLabel.Text = AppStrings.SponsorBlock.SettingsHeader;
            headerLabel.Font = new Font(UITheme.FontDefault.FontFamily, 13F, FontStyle.Bold);
            headerLabel.ForeColor = UITheme.TextPrimary;
            headerLabel.AutoSize = true;
            headerLabel.Dock = DockStyle.Top;
            topPanel.Controls.Add(headerLabel);

            Label subLabel = new Label();
            subLabel.UseMnemonic = false;
            subLabel.Text = AppStrings.SponsorBlock.SettingsDesc;
            subLabel.Font = UITheme.FontSmall;
            subLabel.ForeColor = UITheme.TextSecondary;
            subLabel.AutoSize = true;
            subLabel.Dock = DockStyle.Top;
            subLabel.Padding = new Padding(0, 3, 0, 0);
            topPanel.Controls.Add(subLabel);
            subLabel.BringToFront();

            mainLayout.Controls.Add(topPanel, 0, 0);

            // 2. Main Scrollable Content: Checkboxes + Categories
            Panel scrollContainer = new Panel();
            scrollContainer.Dock = DockStyle.Fill;
            scrollContainer.AutoScroll = true;
            scrollContainer.Padding = new Padding(16, 10, 16, 6);
            scrollContainer.BackColor = UITheme.FormBackground;

            FlowLayoutPanel optionsFlow = new FlowLayoutPanel();
            optionsFlow.Dock = DockStyle.Top;
            optionsFlow.AutoSize = true;
            optionsFlow.FlowDirection = FlowDirection.TopDown;
            optionsFlow.WrapContents = false;
            optionsFlow.Padding = new Padding(0, 0, 0, 8);

            _enableSbCheck = CreateCheckBox(AppStrings.SponsorBlock.OptionEnableSb, true);
            _showPreviewBarCheck = CreateCheckBox(AppStrings.SponsorBlock.OptionShowPreviewBar, true);
            _showNoticeCheck = CreateCheckBox(AppStrings.SponsorBlock.OptionShowNotice, true);
            _audioNotifyCheck = CreateCheckBox(AppStrings.SponsorBlock.OptionAudioNotify, false);

            optionsFlow.Controls.Add(_enableSbCheck);
            optionsFlow.Controls.Add(_showPreviewBarCheck);
            optionsFlow.Controls.Add(_showNoticeCheck);
            optionsFlow.Controls.Add(_audioNotifyCheck);

            scrollContainer.Controls.Add(optionsFlow);

            // Categories Section Title
            Label catTitle = new Label();
            catTitle.UseMnemonic = false;
            catTitle.Text = AppStrings.SponsorBlock.SectionCategoriesActions;
            catTitle.Font = new Font(UITheme.FontDefault.FontFamily, 9F, FontStyle.Bold);
            catTitle.ForeColor = UITheme.TextSecondary;
            catTitle.AutoSize = true;
            catTitle.Dock = DockStyle.Top;
            catTitle.Padding = new Padding(0, 6, 0, 6);
            scrollContainer.Controls.Add(catTitle);
            catTitle.BringToFront();

            // Category Table
            _categoryTable = new TableLayoutPanel();
            _categoryTable.Dock = DockStyle.Top;
            _categoryTable.BackColor = UITheme.FormBackground;
            _categoryTable.AutoSize = true;
            _categoryTable.ColumnCount = 3;
            _categoryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24F));  // Color dot
            _categoryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // Name + Description
            _categoryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F)); // Action dropdown
            _categoryCombos = new Dictionary<string, ComboBox>(StringComparer.OrdinalIgnoreCase);

            int row = 0;
            foreach (var cat in SponsorCategory.All)
            {
                _categoryTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                // Dot
                Panel dot = new Panel();
                dot.Size = new Size(12, 12);
                dot.Margin = new Padding(4, 9, 4, 0);
                dot.BackColor = cat.Color;
                dot.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (Brush b = new SolidBrush(cat.Color))
                    {
                        e.Graphics.FillEllipse(b, 0, 0, 11, 11);
                    }
                };
                _categoryTable.Controls.Add(dot, 0, row);

                // Name & description
                Panel infoPanel = new Panel();
                infoPanel.AutoSize = true;
                infoPanel.Margin = new Padding(0, 3, 8, 4);

                Label nameLbl = new Label();
                nameLbl.Text = cat.Name;
                nameLbl.Font = UITheme.FontDefaultBold;
                nameLbl.ForeColor = UITheme.TextPrimary;
                nameLbl.AutoSize = true;
                nameLbl.Location = new Point(0, 0);
                infoPanel.Controls.Add(nameLbl);

                Label descLbl = new Label();
                descLbl.Text = cat.Description;
                descLbl.Font = UITheme.FontSmall;
                descLbl.ForeColor = UITheme.TextSecondary;
                descLbl.AutoSize = true;
                descLbl.Location = new Point(0, 18);
                infoPanel.Controls.Add(descLbl);

                _categoryTable.Controls.Add(infoPanel, 1, row);

                // Action Dropdown
                ComboBox combo = new ComboBox();
                combo.DropDownStyle = ComboBoxStyle.DropDownList;
                combo.Width = 170;
                combo.Margin = new Padding(0, 5, 0, 0);
                combo.Items.Add(AppStrings.SponsorBlock.ActionAutoSkip);
                combo.Items.Add(AppStrings.SponsorBlock.ActionManualSkip);
                combo.Items.Add(AppStrings.SponsorBlock.ActionMute);
                combo.Items.Add(AppStrings.SponsorBlock.ActionDisabled);
                combo.SelectedIndex = 0;
                UITheme.ApplyComboBox(combo);

                _categoryCombos[cat.Key] = combo;
                _categoryTable.Controls.Add(combo, 2, row);

                row++;
            }

            scrollContainer.Controls.Add(_categoryTable);
            _categoryTable.BringToFront();

            mainLayout.Controls.Add(scrollContainer, 0, 1);

            // 3. Bottom Buttons Bar
            TableLayoutPanel bottomLayout = new TableLayoutPanel();
            UITheme.ApplyBottomBar(bottomLayout);
            bottomLayout.Dock = DockStyle.Fill;
            bottomLayout.RowCount = 1;
            bottomLayout.ColumnCount = 3;
            bottomLayout.ColumnStyles.Clear();
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomLayout.Padding = new Padding(16, 0, 16, 0);

            FlowLayoutPanel leftButtons = new FlowLayoutPanel();
            leftButtons.Dock = DockStyle.Fill;
            leftButtons.AutoSize = true;
            leftButtons.WrapContents = false;
            leftButtons.Margin = new Padding(0);

            Button whitelistBtn = new Button();
            whitelistBtn.UseMnemonic = false;
            whitelistBtn.Text = AppStrings.SponsorBlock.BtnManageWhitelist;
            whitelistBtn.AutoSize = true;
            whitelistBtn.Height = 30;
            whitelistBtn.Margin = new Padding(0, 11, 8, 11);
            whitelistBtn.Click += (s, e) =>
            {
                using (var wForm = new SponsorBlockWhitelistForm())
                {
                    wForm.ShowDialog(this);
                }
            };
            UITheme.ApplyButton(whitelistBtn, false);
            leftButtons.Controls.Add(whitelistBtn);

            _resetDefaultsBtn = new Button();
            _resetDefaultsBtn.UseMnemonic = false;
            _resetDefaultsBtn.Text = AppStrings.SponsorBlock.BtnResetDefaults;
            _resetDefaultsBtn.AutoSize = true;
            _resetDefaultsBtn.Height = 30;
            _resetDefaultsBtn.Margin = new Padding(0, 11, 0, 11);
            _resetDefaultsBtn.Click += ResetDefaultsBtn_Click;
            UITheme.ApplyButton(_resetDefaultsBtn, false);
            leftButtons.Controls.Add(_resetDefaultsBtn);

            bottomLayout.Controls.Add(leftButtons, 0, 0);

            FlowLayoutPanel rightButtons = new FlowLayoutPanel();
            rightButtons.Dock = DockStyle.Fill;
            rightButtons.AutoSize = true;
            rightButtons.FlowDirection = FlowDirection.RightToLeft;
            rightButtons.WrapContents = false;
            rightButtons.Margin = new Padding(0);

            _closeBtn = new Button();
            _closeBtn.Text = AppStrings.General.BtnCancel;
            _closeBtn.Size = new Size(85, 30);
            _closeBtn.Margin = new Padding(8, 11, 0, 11);
            _closeBtn.Click += (s, e) => this.Close();
            UITheme.ApplyButton(_closeBtn, false);
            rightButtons.Controls.Add(_closeBtn);

            _saveBtn = new Button();
            _saveBtn.UseMnemonic = false;
            _saveBtn.Text = AppStrings.SponsorBlock.BtnSaveApply;
            _saveBtn.Size = new Size(115, 30);
            _saveBtn.Margin = new Padding(0, 11, 0, 11);
            _saveBtn.Click += SaveBtn_Click;
            UITheme.ApplyButton(_saveBtn, true);
            rightButtons.Controls.Add(_saveBtn);

            bottomLayout.Controls.Add(rightButtons, 2, 0);

            mainLayout.Controls.Add(bottomLayout, 0, 2);
        }

        private CheckBox CreateCheckBox(string text, bool defaultVal)
        {
            CheckBox cb = new CheckBox();
            cb.Text = text;
            cb.Checked = defaultVal;
            cb.AutoSize = true;
            cb.Font = UITheme.FontDefault;
            cb.ForeColor = UITheme.TextPrimary;
            cb.Margin = new Padding(0, 3, 0, 3);
            UITheme.ApplyCheckBox(cb);
            return cb;
        }

        private void LoadCurrentValues()
        {
            var config = AppServices.SponsorBlock.Config;
            _enableSbCheck.Checked = config.IsEnabled;
            _showPreviewBarCheck.Checked = config.ShowPreviewBar;
            _showNoticeCheck.Checked = config.ShowNotice;
            _audioNotifyCheck.Checked = config.AudioNotification;

            foreach (var cat in SponsorCategory.All)
            {
                ComboBox combo;
                if (_categoryCombos.TryGetValue(cat.Key, out combo))
                {
                    SponsorAction action = config.GetAction(cat.Key);
                    combo.SelectedIndex = (int)action;
                }
            }
        }

        private void ResetDefaultsBtn_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, AppStrings.SponsorBlock.MsgConfirmResetDefaults, AppStrings.SponsorBlock.DialogResetDefaultsTitle,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _enableSbCheck.Checked = true;
                _showPreviewBarCheck.Checked = true;
                _showNoticeCheck.Checked = true;
                _audioNotifyCheck.Checked = false;

                foreach (var cat in SponsorCategory.All)
                {
                    ComboBox combo;
                    if (_categoryCombos.TryGetValue(cat.Key, out combo))
                    {
                        combo.SelectedIndex = (int)cat.DefaultAction;
                    }
                }
            }
        }

        private void SaveBtn_Click(object sender, EventArgs e)
        {
            var config = AppServices.SponsorBlock.Config;
            config.IsEnabled = _enableSbCheck.Checked;
            config.ShowPreviewBar = _showPreviewBarCheck.Checked;
            config.ShowNotice = _showNoticeCheck.Checked;
            config.AudioNotification = _audioNotifyCheck.Checked;

            foreach (var cat in SponsorCategory.All)
            {
                ComboBox combo;
                if (_categoryCombos.TryGetValue(cat.Key, out combo))
                {
                    SponsorAction action = (SponsorAction)Math.Max(0, combo.SelectedIndex);
                    config.SetAction(cat.Key, action);
                }
            }

            AppServices.SponsorBlock.SaveConfig();
            this.Close();
        }
    }
}
