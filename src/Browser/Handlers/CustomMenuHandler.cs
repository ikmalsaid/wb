using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CefSharp;
using wb.Core;

namespace wb.Browser.Handlers
{
    /// <summary>
    /// Native Windows context menu handler providing essential options
    /// tailored to the clicked web element (Links, Images, Text Selection, Editable inputs, or General Page).
    /// </summary>
    public class CustomMenuHandler : IContextMenuHandler
    {
        // Custom user command IDs in safe CEF range [26500, 28500]
        public const CefMenuCommand OpenLinkInNewTab = (CefMenuCommand)26501;
        public const CefMenuCommand ViewSourceCommand = (CefMenuCommand)26502;
        public const CefMenuCommand InspectElementCommand = (CefMenuCommand)26503;
        public const CefMenuCommand BlockElementCommand = (CefMenuCommand)26504;
        public const CefMenuCommand OpenLinkInNewWindow = (CefMenuCommand)26505;
        public const CefMenuCommand CopyLinkAddressCommand = (CefMenuCommand)26506;
        public const CefMenuCommand SaveLinkAsCommand = (CefMenuCommand)26507;
        public const CefMenuCommand OpenImageInNewTabCommand = (CefMenuCommand)26508;
        public const CefMenuCommand SaveImageAsCommand = (CefMenuCommand)26509;
        public const CefMenuCommand CopyImageCommand = (CefMenuCommand)26510;
        public const CefMenuCommand CopyImageAddressCommand = (CefMenuCommand)26511;
        public const CefMenuCommand SearchGoogleCommand = (CefMenuCommand)26512;
        public const CefMenuCommand SavePageAsCommand = (CefMenuCommand)26513;
        public const CefMenuCommand OpenLinkInIncognitoWindow = (CefMenuCommand)26514;

        private readonly Action<string> _openNewTabAction;
        private readonly Action<string> _openNewWindowAction;
        private readonly Action<string> _openNewIncognitoWindowAction;
        private readonly Action _viewSourceAction;
        private readonly Action _startBlockElementAction;

        public CustomMenuHandler(Action<string> openNewTabAction, Action viewSourceAction, Action startBlockElementAction = null)
            : this(openNewTabAction, null, null, viewSourceAction, startBlockElementAction)
        {
        }

        public CustomMenuHandler(Action<string> openNewTabAction, Action<string> openNewWindowAction, Action viewSourceAction, Action startBlockElementAction = null)
            : this(openNewTabAction, openNewWindowAction, null, viewSourceAction, startBlockElementAction)
        {
        }

        public CustomMenuHandler(Action<string> openNewTabAction, Action<string> openNewWindowAction, Action<string> openNewIncognitoWindowAction, Action viewSourceAction, Action startBlockElementAction = null)
        {
            _openNewTabAction = openNewTabAction;
            _openNewWindowAction = openNewWindowAction;
            _openNewIncognitoWindowAction = openNewIncognitoWindowAction;
            _viewSourceAction = viewSourceAction;
            _startBlockElementAction = startBlockElementAction;
        }

        public void OnBeforeContextMenu(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters, IMenuModel model)
        {
            model.Clear();

            bool hasLink = !string.IsNullOrEmpty(parameters.LinkUrl);
            bool hasSelection = !string.IsNullOrEmpty(parameters.SelectionText);
            bool isImage = parameters.MediaType == ContextMenuMediaType.Image ||
                           parameters.HasImageContents ||
                           (!string.IsNullOrEmpty(parameters.SourceUrl) && (parameters.TypeFlags & ContextMenuType.Media) != 0);
            bool isEditable = parameters.IsEditable;

            // 1. EDITABLE TEXT INPUT CONTEXT
            if (isEditable)
            {
                // Spelling suggestions
                if (!string.IsNullOrEmpty(parameters.MisspelledWord) && parameters.DictionarySuggestions != null && parameters.DictionarySuggestions.Count > 0)
                {
                    int maxCount = Math.Min(5, parameters.DictionarySuggestions.Count);
                    for (int i = 0; i < maxCount; i++)
                    {
                        model.AddItem((CefMenuCommand)((int)CefMenuCommand.SpellCheckSuggestion0 + i), parameters.DictionarySuggestions[i]);
                    }
                    model.AddSeparator();
                    model.AddItem(CefMenuCommand.AddToDictionary, AppStrings.Browser.CtxAddToDictionary);
                    model.AddSeparator();
                }

                model.AddItem(CefMenuCommand.Undo, AppStrings.Browser.CtxUndo);
                model.SetEnabled(CefMenuCommand.Undo, (parameters.EditStateFlags & ContextMenuEditState.CanUndo) != 0);

                model.AddItem(CefMenuCommand.Redo, AppStrings.Browser.CtxRedo);
                model.SetEnabled(CefMenuCommand.Redo, (parameters.EditStateFlags & ContextMenuEditState.CanRedo) != 0);

                model.AddSeparator();

                model.AddItem(CefMenuCommand.Cut, AppStrings.Browser.CtxCut);
                model.SetEnabled(CefMenuCommand.Cut, (parameters.EditStateFlags & ContextMenuEditState.CanCut) != 0);

                model.AddItem(CefMenuCommand.Copy, AppStrings.Browser.CtxCopy);
                model.SetEnabled(CefMenuCommand.Copy, (parameters.EditStateFlags & ContextMenuEditState.CanCopy) != 0);

                model.AddItem(CefMenuCommand.Paste, AppStrings.Browser.CtxPaste);
                model.SetEnabled(CefMenuCommand.Paste, (parameters.EditStateFlags & ContextMenuEditState.CanPaste) != 0);

                model.AddItem(CefMenuCommand.Delete, AppStrings.Browser.CtxDelete);
                model.SetEnabled(CefMenuCommand.Delete, (parameters.EditStateFlags & ContextMenuEditState.CanDelete) != 0);

                model.AddSeparator();

                model.AddItem(CefMenuCommand.SelectAll, AppStrings.Browser.CtxSelectAll);
                model.SetEnabled(CefMenuCommand.SelectAll, (parameters.EditStateFlags & ContextMenuEditState.CanSelectAll) != 0);

                model.AddSeparator();
                model.AddItem(InspectElementCommand, AppStrings.Browser.CtxInspectElement);
                return;
            }

            // 2. HYPERLINK CONTEXT
            if (hasLink)
            {
                model.AddItem(OpenLinkInNewTab, AppStrings.Browser.CtxOpenLinkInNewTab);
                model.AddItem(OpenLinkInNewWindow, AppStrings.Browser.CtxOpenLinkInNewWindow);
                model.AddItem(OpenLinkInIncognitoWindow, AppStrings.Browser.ContextMenuOpenIncognitoWindow);
                model.AddSeparator();
                model.AddItem(SaveLinkAsCommand, AppStrings.Browser.CtxSaveLinkAs);
                model.AddItem(CopyLinkAddressCommand, AppStrings.Browser.CtxCopyLinkAddress);

                // If element is an image hyperlink, also include image actions
                if (isImage)
                {
                    model.AddSeparator();
                    model.AddItem(OpenImageInNewTabCommand, AppStrings.Browser.CtxOpenImageInNewTab);
                    model.AddItem(SaveImageAsCommand, AppStrings.Browser.CtxSaveImageAs);
                    model.AddItem(CopyImageCommand, AppStrings.Browser.CtxCopyImage);
                    model.AddItem(CopyImageAddressCommand, AppStrings.Browser.CtxCopyImageAddress);
                }

                model.AddSeparator();
                if (_startBlockElementAction != null)
                {
                    model.AddItem(BlockElementCommand, AppStrings.AdBlock.ContextMenuBlockElement);
                }
                model.AddItem(InspectElementCommand, AppStrings.Browser.CtxInspectElement);
                return;
            }

            // 3. IMAGE CONTEXT (standalone image)
            if (isImage)
            {
                model.AddItem(OpenImageInNewTabCommand, AppStrings.Browser.CtxOpenImageInNewTab);
                model.AddItem(SaveImageAsCommand, AppStrings.Browser.CtxSaveImageAs);
                model.AddItem(CopyImageCommand, AppStrings.Browser.CtxCopyImage);
                model.AddItem(CopyImageAddressCommand, AppStrings.Browser.CtxCopyImageAddress);

                model.AddSeparator();
                if (_startBlockElementAction != null)
                {
                    model.AddItem(BlockElementCommand, AppStrings.AdBlock.ContextMenuBlockElement);
                }
                model.AddItem(InspectElementCommand, AppStrings.Browser.CtxInspectElement);
                return;
            }

            // 4. TEXT SELECTION CONTEXT
            if (hasSelection)
            {
                model.AddItem(CefMenuCommand.Copy, AppStrings.Browser.CtxCopy);

                string selText = parameters.SelectionText != null ? parameters.SelectionText.Trim() : string.Empty;
                string displayQuery = selText.Length > 20 ? selText.Substring(0, 18) + "..." : selText;
                displayQuery = displayQuery.Replace("\r", " ").Replace("\n", " ");
                model.AddItem(SearchGoogleCommand, string.Format(AppStrings.Browser.CtxSearchGoogleFormat, displayQuery));

                model.AddSeparator();
                model.AddItem(CefMenuCommand.Print, AppStrings.Browser.CtxPrint);

                model.AddSeparator();
                if (_startBlockElementAction != null)
                {
                    model.AddItem(BlockElementCommand, AppStrings.AdBlock.ContextMenuBlockElement);
                }
                model.AddItem(InspectElementCommand, AppStrings.Browser.CtxInspectElement);
                return;
            }

            // 5. GENERAL PAGE CONTEXT
            model.AddItem(CefMenuCommand.Back, AppStrings.Browser.CtxBack);
            model.SetEnabled(CefMenuCommand.Back, browser.CanGoBack);

            model.AddItem(CefMenuCommand.Forward, AppStrings.Browser.CtxForward);
            model.SetEnabled(CefMenuCommand.Forward, browser.CanGoForward);

            model.AddItem(CefMenuCommand.Reload, AppStrings.Browser.CtxReload);

            model.AddSeparator();

            model.AddItem(SavePageAsCommand, AppStrings.Browser.CtxSavePageAs);
            model.AddItem(CefMenuCommand.Print, AppStrings.Browser.CtxPrint);

            model.AddSeparator();

            if (_startBlockElementAction != null)
            {
                model.AddItem(BlockElementCommand, AppStrings.AdBlock.ContextMenuBlockElement);
            }
            model.AddItem(ViewSourceCommand, AppStrings.Browser.CtxViewPageSource);
            model.AddItem(InspectElementCommand, AppStrings.Browser.CtxInspectElement);
        }

        public bool OnContextMenuCommand(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters, CefMenuCommand commandId, CefEventFlags eventFlags)
        {
            if (commandId == BlockElementCommand)
            {
                if (_startBlockElementAction != null)
                {
                    _startBlockElementAction();
                }
                return true;
            }

            if (commandId == OpenLinkInNewTab)
            {
                if (_openNewTabAction != null && !string.IsNullOrEmpty(parameters.LinkUrl))
                {
                    _openNewTabAction(parameters.LinkUrl);
                }
                return true;
            }

            if (commandId == OpenLinkInNewWindow)
            {
                if (!string.IsNullOrEmpty(parameters.LinkUrl))
                {
                    if (_openNewWindowAction != null)
                    {
                        _openNewWindowAction(parameters.LinkUrl);
                    }
                    else if (_openNewTabAction != null)
                    {
                        _openNewTabAction(parameters.LinkUrl);
                    }
                }
                return true;
            }

            if (commandId == OpenLinkInIncognitoWindow)
            {
                if (!string.IsNullOrEmpty(parameters.LinkUrl))
                {
                    if (_openNewIncognitoWindowAction != null)
                    {
                        _openNewIncognitoWindowAction(parameters.LinkUrl);
                    }
                    else if (_openNewWindowAction != null)
                    {
                        _openNewWindowAction(parameters.LinkUrl);
                    }
                }
                return true;
            }

            if (commandId == CopyLinkAddressCommand)
            {
                string url = !string.IsNullOrEmpty(parameters.UnfilteredLinkUrl) ? parameters.UnfilteredLinkUrl : parameters.LinkUrl;
                if (!string.IsNullOrEmpty(url))
                {
                    ClipboardHelper.SetText(url);
                }
                return true;
            }

            if (commandId == SaveLinkAsCommand)
            {
                if (!string.IsNullOrEmpty(parameters.LinkUrl) && browser != null)
                {
                    browser.GetHost().StartDownload(parameters.LinkUrl);
                }
                return true;
            }

            if (commandId == OpenImageInNewTabCommand)
            {
                if (_openNewTabAction != null && !string.IsNullOrEmpty(parameters.SourceUrl))
                {
                    _openNewTabAction(parameters.SourceUrl);
                }
                return true;
            }

            if (commandId == SaveImageAsCommand)
            {
                if (!string.IsNullOrEmpty(parameters.SourceUrl) && browser != null)
                {
                    browser.GetHost().StartDownload(parameters.SourceUrl);
                }
                return true;
            }

            if (commandId == CopyImageAddressCommand)
            {
                if (!string.IsNullOrEmpty(parameters.SourceUrl))
                {
                    ClipboardHelper.SetText(parameters.SourceUrl);
                }
                return true;
            }

            if (commandId == CopyImageCommand)
            {
                if (!string.IsNullOrEmpty(parameters.SourceUrl))
                {
                    ClipboardHelper.CopyImageFromUrl(parameters.SourceUrl);
                }
                return true;
            }

            if (commandId == SearchGoogleCommand)
            {
                if (!string.IsNullOrEmpty(parameters.SelectionText) && _openNewTabAction != null)
                {
                    string searchUrl = "https://www.google.com/search?q=" + Uri.EscapeDataString(parameters.SelectionText.Trim());
                    _openNewTabAction(searchUrl);
                }
                return true;
            }

            if (commandId == SavePageAsCommand)
            {
                SavePage(browser, frame, parameters);
                return true;
            }

            if (commandId == ViewSourceCommand)
            {
                if (_viewSourceAction != null)
                {
                    _viewSourceAction();
                }
                return true;
            }

            if (commandId == InspectElementCommand)
            {
                browser.ShowDevTools(null, parameters.XCoord, parameters.YCoord);
                return true;
            }

            // Return false for built-in CEF commands (Back, Forward, Reload, Print, Undo, Redo, Cut, Copy, Paste, Delete, SelectAll, SpellCheck)
            return false;
        }

        private void SavePage(IBrowser browser, IFrame frame, IContextMenuParams parameters)
        {
            try
            {
                Thread staThread = new Thread(delegate()
                {
                    try
                    {
                        using (SaveFileDialog sfd = new SaveFileDialog())
                        {
                            sfd.Title = AppStrings.Browser.SaveWebpageTitle;
                            sfd.Filter = AppStrings.Browser.HtmlFilesFilter;

                            string pageUrl = !string.IsNullOrEmpty(parameters.PageUrl) ? parameters.PageUrl : (frame != null ? frame.Url : "");
                            string defaultName = "page";
                            try
                            {
                                if (!string.IsNullOrEmpty(pageUrl) && Uri.IsWellFormedUriString(pageUrl, UriKind.Absolute))
                                {
                                    Uri u = new Uri(pageUrl);
                                    string seg = Path.GetFileNameWithoutExtension(u.AbsolutePath);
                                    if (!string.IsNullOrEmpty(seg)) defaultName = seg;
                                    else if (!string.IsNullOrEmpty(u.Host)) defaultName = u.Host;
                                }
                            }
                            catch
                            {
                            }

                            sfd.FileName = defaultName + ".html";

                            if (sfd.ShowDialog() == DialogResult.OK)
                            {
                                string savePath = sfd.FileName;
                                if (frame != null && frame.IsValid)
                                {
                                    frame.GetSourceAsync().ContinueWith(delegate(System.Threading.Tasks.Task<string> t)
                                    {
                                        try
                                        {
                                            if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && !string.IsNullOrEmpty(t.Result))
                                            {
                                                File.WriteAllText(savePath, t.Result, Encoding.UTF8);
                                            }
                                        }
                                        catch
                                        {
                                        }
                                    });
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                });
                staThread.SetApartmentState(ApartmentState.STA);
                staThread.IsBackground = true;
                staThread.Start();
            }
            catch
            {
            }
        }

        public void OnContextMenuDismissed(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
        {
        }

        public bool RunContextMenu(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters, IMenuModel model, IRunContextMenuCallback callback)
        {
            // Return false to let CEF display the native Windows context menu UI
            return false;
        }
    }
}
