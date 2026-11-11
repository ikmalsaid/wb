using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.AdBlock
{
    /// <summary>
    /// Native window dialog for confirming and creating custom element blocking rules.
    /// Strictly follows window design system (UITheme, theme accent colors) without any emojis.
    /// Allows selecting between specific child elements and parent containers.
    /// </summary>
    public class BlockElementForm : Form
    {
        public class ElementCandidate
        {
            public string label { get; set; }
            public string selector { get; set; }
            public int matches { get; set; }
            public int depth { get; set; }

            public override string ToString()
            {
                return string.Format(matches == 1 ? AppStrings.AdBlock.CandidateMatchCountFormatSingle : AppStrings.AdBlock.CandidateMatchCountFormatPlural, label, matches);
            }
        }

        private readonly IElementPickerTarget _target;
        private readonly string _domain;
        private readonly string _initialSelector;
        private readonly int _initialMatches;
        private readonly string _candidatesJson;

        private RadioButton _siteRadio;
        private RadioButton _globalRadio;
        private ComboBox _hierarchyCombo;
        private TextBox _selectorTxt;
        private Label _matchesLabel;
        private Button _previewBtn;
        private Button _pickAgainBtn;
        private Button _cancelBtn;
        private Button _blockBtn;
        private bool _isPreviewActive;
        private bool _isActionHandled;
        private List<ElementCandidate> _candidates;

        public BlockElementForm(IElementPickerTarget target, string domain, string selector, int matches)
            : this(target, domain, selector, matches, null)
        {
        }

        public BlockElementForm(IElementPickerTarget target, string domain, string selector, int matches, string candidatesJson)
        {
            _target = target;
            _domain = domain ?? string.Empty;
            _initialSelector = selector ?? string.Empty;
            _initialMatches = matches > 0 ? matches : 1;
            _candidatesJson = candidatesJson;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            UITheme.ApplyModalDialog(this, AppStrings.AdBlock.BlockElementDialogTitle, new Size(500, 360), new Size(460, 330));
            this.TopMost = true;

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.RowCount = 3;
            mainLayout.ColumnCount = 1;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            this.Controls.Add(mainLayout);

            // 1. Top Header
            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Fill;
            UITheme.ApplyTopToolbar(headerPanel);
            headerPanel.Padding = new Padding(14, 8, 14, 6);

            Label titleLabel = new Label();
            titleLabel.Text = AppStrings.AdBlock.BlockElementPanelTitle;
            titleLabel.Font = UITheme.FontHeader;
            titleLabel.ForeColor = UITheme.TextPrimary;
            titleLabel.Location = new Point(14, 8);
            titleLabel.AutoSize = true;
            headerPanel.Controls.Add(titleLabel);

            Label descLabel = new Label();
            descLabel.Text = AppStrings.AdBlock.BlockElementDialogDesc;
            descLabel.Font = UITheme.FontSmall;
            descLabel.ForeColor = UITheme.TextSecondary;
            descLabel.Location = new Point(14, 28);
            descLabel.AutoSize = true;
            headerPanel.Controls.Add(descLabel);

            mainLayout.Controls.Add(headerPanel, 0, 0);

            // 2. Center Content Panel
            Panel bodyPanel = new Panel();
            bodyPanel.Dock = DockStyle.Fill;
            bodyPanel.BackColor = UITheme.FormBackground;
            bodyPanel.Padding = new Padding(16, 10, 16, 6);

            // Scope Radios
            _siteRadio = new RadioButton();
            _siteRadio.Text = string.Format(AppStrings.AdBlock.BlockElementScopeSiteFormat, string.IsNullOrEmpty(_domain) ? AppStrings.AdBlock.CurrentSiteFallback : _domain);
            _siteRadio.Font = UITheme.FontDefault;
            _siteRadio.ForeColor = UITheme.TextPrimary;
            _siteRadio.Location = new Point(16, 10);
            _siteRadio.Size = new Size(230, 22);
            _siteRadio.Checked = true;
            UITheme.ApplyRadioButton(_siteRadio);
            bodyPanel.Controls.Add(_siteRadio);

            _globalRadio = new RadioButton();
            _globalRadio.Text = AppStrings.AdBlock.BlockElementScopeGlobal;
            _globalRadio.Font = UITheme.FontDefault;
            _globalRadio.ForeColor = UITheme.TextPrimary;
            _globalRadio.Location = new Point(256, 10);
            _globalRadio.Size = new Size(200, 22);
            UITheme.ApplyRadioButton(_globalRadio);
            bodyPanel.Controls.Add(_globalRadio);

            // Target Element Level (Child vs Parent)
            Label hierLabel = new Label();
            hierLabel.Text = AppStrings.AdBlock.TargetElementLevelLabel;
            hierLabel.Font = UITheme.FontDefaultBold;
            hierLabel.ForeColor = UITheme.TextPrimary;
            hierLabel.Location = new Point(16, 38);
            hierLabel.AutoSize = true;
            bodyPanel.Controls.Add(hierLabel);

            _hierarchyCombo = new ComboBox();
            _hierarchyCombo.Location = new Point(16, 58);
            _hierarchyCombo.Width = 450;
            UITheme.ApplyComboBox(_hierarchyCombo);
            bodyPanel.Controls.Add(_hierarchyCombo);

            _candidates = new List<ElementCandidate>();
            if (!string.IsNullOrEmpty(_candidatesJson))
            {
                List<ElementCandidate> list = StorageHelper.Deserialize<List<ElementCandidate>>(_candidatesJson);
                if (list != null && list.Count > 0)
                {
                    _candidates = list;
                }
            }

            if (_candidates.Count == 0)
            {
                _candidates.Add(new ElementCandidate
                {
                    label = AppStrings.AdBlock.CandidateSpecificChildElement,
                    selector = _initialSelector,
                    matches = _initialMatches,
                    depth = 0
                });
            }

            for (int i = 0; i < _candidates.Count; i++)
            {
                _hierarchyCombo.Items.Add(_candidates[i]);
            }
            _hierarchyCombo.SelectedIndex = 0;

            // Selector Label & Box
            Label selLabel = new Label();
            selLabel.Text = AppStrings.AdBlock.CssSelectorLabel;
            selLabel.Font = UITheme.FontDefaultBold;
            selLabel.ForeColor = UITheme.TextPrimary;
            selLabel.Location = new Point(16, 92);
            selLabel.AutoSize = true;
            bodyPanel.Controls.Add(selLabel);

            _selectorTxt = new TextBox();
            _selectorTxt.Location = new Point(16, 114);
            _selectorTxt.Width = 450;
            _selectorTxt.Text = !string.IsNullOrEmpty(_candidates[0].selector) ? _candidates[0].selector : _initialSelector;
            UITheme.ApplyTextBox(_selectorTxt, AppStrings.AdBlock.CssSelectorPlaceholder);
            bodyPanel.Controls.Add(_selectorTxt);

            _matchesLabel = new Label();
            _matchesLabel.Text = string.Format(_candidates[0].matches == 1 ? AppStrings.AdBlock.MatchesCountFormatSingle : AppStrings.AdBlock.MatchesCountFormatPlural, _candidates[0].matches);
            _matchesLabel.Font = UITheme.FontSmall;
            _matchesLabel.ForeColor = ThemeManager.CurrentAccentColor;
            _matchesLabel.Location = new Point(16, 144);
            _matchesLabel.AutoSize = true;
            bodyPanel.Controls.Add(_matchesLabel);

            _hierarchyCombo.SelectedIndexChanged += (s, e) =>
            {
                ElementCandidate sel = _hierarchyCombo.SelectedItem as ElementCandidate;
                if (sel != null && !string.IsNullOrEmpty(sel.selector))
                {
                    _selectorTxt.Text = sel.selector;
                    _matchesLabel.Text = string.Format(sel.matches == 1 ? AppStrings.AdBlock.MatchesCountFormatSingle : AppStrings.AdBlock.MatchesCountFormatPlural, sel.matches);
                    if (_isPreviewActive && _target != null)
                    {
                        _target.SetElementPickerPreview(true, sel.selector);
                    }
                }
            };

            _selectorTxt.TextChanged += (s, e) =>
            {
                if (_isPreviewActive && _target != null)
                {
                    string curSel = _selectorTxt.Text.Trim();
                    if (!string.IsNullOrEmpty(curSel))
                    {
                        _target.SetElementPickerPreview(true, curSel);
                    }
                }
            };

            _selectorTxt.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    ConfirmSelectedElement();
                }
            };

            mainLayout.Controls.Add(bodyPanel, 0, 1);

            // 3. Bottom Button Bar
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

            _previewBtn = new Button();
            _previewBtn.Text = AppStrings.AdBlock.BlockElementBtnPreview;
            _previewBtn.Height = 28;
            _previewBtn.AutoSize = true;
            UITheme.ApplyButton(_previewBtn);
            _previewBtn.Click += PreviewBtn_Click;
            bottomLeft.Controls.Add(_previewBtn);

            _pickAgainBtn = new Button();
            _pickAgainBtn.Text = AppStrings.AdBlock.BlockElementBtnPickAgain;
            _pickAgainBtn.Height = 28;
            _pickAgainBtn.AutoSize = true;
            UITheme.ApplyButton(_pickAgainBtn);
            _pickAgainBtn.Click += PickAgainBtn_Click;
            bottomLeft.Controls.Add(_pickAgainBtn);

            FlowLayoutPanel bottomRight = new FlowLayoutPanel();
            bottomRight.Dock = DockStyle.Fill;
            bottomRight.AutoSize = true;
            bottomRight.WrapContents = false;
            bottomRight.FlowDirection = FlowDirection.RightToLeft;
            bottomRight.Margin = new Padding(0);

            _blockBtn = new Button();
            _blockBtn.Text = AppStrings.AdBlock.BlockElementBtnBlock;
            _blockBtn.Height = 28;
            _blockBtn.AutoSize = true;
            UITheme.ApplyButton(_blockBtn, true);
            _blockBtn.Click += BlockBtn_Click;
            bottomRight.Controls.Add(_blockBtn);

            _cancelBtn = new Button();
            _cancelBtn.Text = AppStrings.General.BtnCancel;
            _cancelBtn.Height = 28;
            _cancelBtn.Width = 75;
            UITheme.ApplyButton(_cancelBtn);
            _cancelBtn.Click += CancelBtn_Click;
            bottomRight.Controls.Add(_cancelBtn);

            bottomLayout.Controls.Add(bottomLeft, 0, 0);
            bottomLayout.Controls.Add(bottomRight, 1, 0);
            mainLayout.Controls.Add(bottomLayout, 0, 2);

            this.AcceptButton = _blockBtn;
            this.CancelButton = _cancelBtn;
        }

        private void PreviewBtn_Click(object sender, EventArgs e)
        {
            if (_target == null) return;
            string sel = _selectorTxt.Text.Trim();
            if (string.IsNullOrEmpty(sel)) return;

            _isPreviewActive = !_isPreviewActive;
            _target.SetElementPickerPreview(_isPreviewActive, sel);

            if (_isPreviewActive)
            {
                _previewBtn.Text = AppStrings.AdBlock.BtnUndoPreview;
            }
            else
            {
                _previewBtn.Text = AppStrings.AdBlock.BlockElementBtnPreview;
            }
        }

        private void PickAgainBtn_Click(object sender, EventArgs e)
        {
            _isActionHandled = true;
            if (_isPreviewActive && _target != null)
            {
                _target.SetElementPickerPreview(false, _selectorTxt.Text);
            }
            if (_target != null)
            {
                _target.ResumeElementPicker();
            }
            this.DialogResult = DialogResult.Retry;
            this.Close();
        }

        private void CancelBtn_Click(object sender, EventArgs e)
        {
            HandleCancelCleanup();
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        public void ConfirmSelectedElement()
        {
            BlockBtn_Click(_blockBtn, EventArgs.Empty);
        }

        private void BlockBtn_Click(object sender, EventArgs e)
        {
            try
            {
                string rawSel = _selectorTxt != null ? _selectorTxt.Text.Trim() : string.Empty;
                if (string.IsNullOrEmpty(rawSel))
                {
                    ElementCandidate sel = _hierarchyCombo != null ? _hierarchyCombo.SelectedItem as ElementCandidate : null;
                    if (sel != null && !string.IsNullOrEmpty(sel.selector))
                    {
                        rawSel = sel.selector.Trim();
                    }
                    else
                    {
                        rawSel = !string.IsNullOrEmpty(_initialSelector) ? _initialSelector.Trim() : string.Empty;
                    }
                }

                string cleanSel = rawSel;
                if (cleanSel.StartsWith("##", StringComparison.Ordinal))
                {
                    cleanSel = cleanSel.Substring(2).Trim();
                }

                string targetDomain = (_siteRadio != null && _siteRadio.Checked) ? _domain : string.Empty;

                _isActionHandled = true;

                if (!string.IsNullOrEmpty(cleanSel))
                {
                    // 1. Commit block on tab immediately so element is hidden without delay
                    if (_target != null)
                    {
                        try
                        {
                            _target.CommitElementPickerBlock(cleanSel);
                        }
                        catch
                        {
                        }
                    }

                    // 2. Add custom filter to AdBlockManager
                    try
                    {
                        AppServices.AdBlock.AddCustomFilter(targetDomain, cleanSel);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            this.Activate();
            this.BringToFront();
            if (_selectorTxt != null)
            {
                _selectorTxt.Focus();
                _selectorTxt.SelectAll();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (this.DialogResult != DialogResult.OK && this.DialogResult != DialogResult.Retry)
            {
                HandleCancelCleanup();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.DialogResult != DialogResult.OK && this.DialogResult != DialogResult.Retry)
                {
                    HandleCancelCleanup();
                }
            }
            base.Dispose(disposing);
        }

        private void HandleCancelCleanup()
        {
            if (_isActionHandled) return;
            _isActionHandled = true;

            if (_isPreviewActive && _target != null)
            {
                try { _target.SetElementPickerPreview(false, _selectorTxt != null ? _selectorTxt.Text : string.Empty); } catch { }
            }
            if (_target != null)
            {
                try { _target.CancelElementPicker(); } catch { }
            }
        }
    }
}
