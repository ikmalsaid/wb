using System;
using System.Drawing;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.Browser
{
    /// <summary>
    /// Dark-mode modal dialog for adding or editing a new tab shortcut.
    /// </summary>
    public class AddShortcutDialog : Form
    {
        private TextBox _nameBox;
        private TextBox _urlBox;
        private Button _saveBtn;
        private Button _cancelBtn;

        public string ShortcutTitle
        {
            get { return _nameBox != null ? _nameBox.Text.Trim() : string.Empty; }
        }

        public string ShortcutUrl
        {
            get
            {
                if (_urlBox == null) return string.Empty;
                return UrlHelper.FormatDirectUrl(_urlBox.Text);
            }
        }

        public AddShortcutDialog(string existingTitle = null, string existingUrl = null)
        {
            InitializeComponent(existingTitle, existingUrl);
        }

        private void InitializeComponent(string initialTitle, string initialUrl)
        {
            bool isEditing = !string.IsNullOrEmpty(initialUrl);
            string title = isEditing ? AppStrings.NewTab.DialogTitleEditShortcut : AppStrings.NewTab.DialogTitleAddShortcut;
            UITheme.ApplyModalDialog(this, title, new Size(420, 220));

            // Title Label
            Label nameLabel = new Label
            {
                Text = AppStrings.Bookmarks.ColumnTitle,
                Location = new Point(24, 20),
                AutoSize = true,
                ForeColor = UITheme.TextSecondary,
                Font = UITheme.FontDefault
            };
            this.Controls.Add(nameLabel);

            // Name Box
            _nameBox = new TextBox
            {
                Location = new Point(24, 44),
                Size = new Size(372, 26),
                Text = initialTitle ?? string.Empty
            };
            UITheme.ApplyTextBox(_nameBox);
            this.Controls.Add(_nameBox);

            // URL Label
            Label urlLabel = new Label
            {
                Text = AppStrings.Bookmarks.ColumnUrl,
                Location = new Point(24, 84),
                AutoSize = true,
                ForeColor = UITheme.TextSecondary,
                Font = UITheme.FontDefault
            };
            this.Controls.Add(urlLabel);

            // URL Box
            _urlBox = new TextBox
            {
                Location = new Point(24, 108),
                Size = new Size(372, 26),
                Text = initialUrl ?? string.Empty
            };
            UITheme.ApplyTextBox(_urlBox);
            this.Controls.Add(_urlBox);

            // Cancel Button
            _cancelBtn = new Button
            {
                Text = AppStrings.General.BtnCancel,
                Location = new Point(216, 162),
                Size = new Size(86, 32),
                DialogResult = DialogResult.Cancel
            };
            UITheme.ApplyButton(_cancelBtn, isPrimary: false);
            this.Controls.Add(_cancelBtn);

            // Save Button
            _saveBtn = new Button
            {
                Text = isEditing ? AppStrings.General.BtnSave : AppStrings.General.BtnAdd,
                Location = new Point(310, 162),
                Size = new Size(86, 32)
            };
            UITheme.ApplyButton(_saveBtn, isPrimary: true);
            _saveBtn.Click += OnSaveClicked;
            this.Controls.Add(_saveBtn);

            this.AcceptButton = _saveBtn;
            this.CancelButton = _cancelBtn;

            _nameBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) OnSaveClicked(s, e); };
            _urlBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) OnSaveClicked(s, e); };
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            string url = _urlBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                _urlBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(_nameBox.Text.Trim()))
            {
                try
                {
                    Uri uri = new Uri(url.Contains("://") ? url : "https://" + url);
                    string host = uri.Host.Replace("www.", "").Trim();
                    if (!string.IsNullOrEmpty(host))
                    {
                        _nameBox.Text = host;
                    }
                }
                catch
                {
                    _nameBox.Text = url;
                }
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
