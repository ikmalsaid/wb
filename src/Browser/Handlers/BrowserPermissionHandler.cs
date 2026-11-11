using System;
using CefSharp;
using CefSharp.Handler;

namespace wb.Browser.Handlers
{
    public class BrowserPermissionHandler : PermissionHandler
    {
        private readonly BrowserTab _tab;

        public BrowserPermissionHandler(BrowserTab tab)
        {
            _tab = tab;
        }

        protected override bool OnRequestMediaAccessPermission(
            IWebBrowser chromiumWebBrowser,
            IBrowser browser,
            IFrame frame,
            string requestingOrigin,
            MediaAccessPermissionType requestedPermissions,
            IMediaAccessCallback callback)
        {
            try
            {
                if (callback != null)
                {
                    callback.Continue(requestedPermissions);
                }
            }
            catch
            {
            }

            if (_tab != null && requestedPermissions != MediaAccessPermissionType.None)
            {
                _tab.SetMediaCaptureState(true, requestedPermissions.ToString());
            }

            return true;
        }
    }
}
