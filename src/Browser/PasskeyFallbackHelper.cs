using System;
using System.Text;
using CefSharp;
using wb.Core;

namespace wb.Browser
{
    /// <summary>
    /// Provides WebAuthn and Passkey fallback interception for embedded Chromium (CEF).
    /// Standard embedded browsers lack Google Password Manager cloud sync and the Chromium
    /// desktop QR-code/caBLEv2 phone picker dialog. When identity providers (such as Google or Microsoft)
    /// detect a platform authenticator on Windows without matching local credentials, they fail with
    /// "This device can't be used".
    /// 
    /// This helper shims `PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable` to return false,
    /// instructing identity providers that platform-bound passkeys are not supported on this device.
    /// This causes Google and OAuth services to cleanly fall back to password + standard 2FA (SMS, TOTP,
    /// or phone push notification) without erroring out.
    /// </summary>
    public static class PasskeyFallbackHelper
    {
        private static readonly string FallbackScript = @"(function() {
    try {
        if (window.__wbPasskeyFallbackInstalled) return;
        window.__wbPasskeyFallbackInstalled = true;

        if (window.PublicKeyCredential) {
            // 1. Tell identity providers (Google, Microsoft, Okta, etc.) that
            // no platform authenticator (Windows Hello) is configured on this device.
            // This instructs Google not to prompt for non-existent device-bound passkeys
            // and seamlessly fall back to Password + 2FA (SMS, TOTP Authenticator, Phone prompt).
            try {
                Object.defineProperty(PublicKeyCredential, 'isUserVerifyingPlatformAuthenticatorAvailable', {
                    value: function() { return Promise.resolve(false); },
                    writable: true,
                    configurable: true
                });
            } catch (e1) {
                PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable = function() {
                    return Promise.resolve(false);
                };
            }

            // 2. Disable conditional mediation (passkey autofill) so inputs don't prompt for passkeys
            if (typeof PublicKeyCredential.isConditionalMediationAvailable === 'function') {
                try {
                    Object.defineProperty(PublicKeyCredential, 'isConditionalMediationAvailable', {
                        value: function() { return Promise.resolve(false); },
                        writable: true,
                        configurable: true
                    });
                } catch (e2) {
                    PublicKeyCredential.isConditionalMediationAvailable = function() {
                        return Promise.resolve(false);
                    };
                }
            }
        }

        // 3. Intercept navigator.credentials.get:
        // If a site still requests a platform-bound passkey assertion (authenticatorAttachment: 'platform'),
        // reject with NotSupportedError as defined by the W3C WebAuthn specification for unsupported platform authenticators.
        // This avoids Chromium delegating to Windows Hello and returning NotAllowedError (which triggers Google's 'This device can't be used' error).
        if (navigator.credentials && typeof navigator.credentials.get === 'function') {
            var origGet = navigator.credentials.get.bind(navigator.credentials);
            navigator.credentials.get = function(options) {
                if (options && options.publicKey) {
                    if (options.publicKey.authenticatorAttachment === 'platform') {
                        return Promise.reject(new DOMException('The platform authenticator is not supported on this device.', 'NotSupportedError'));
                    }
                }
                return origGet(options);
            };
        }
    } catch (e) {
    }
})();";

        /// <summary>
        /// Returns the JavaScript payload that shims WebAuthn platform authenticator availability.
        /// </summary>
        public static string GetScript()
        {
            return FallbackScript;
        }

        /// <summary>
        /// Injects the passkey fallback script directly into an active frame if valid and not an internal page.
        /// </summary>
        public static void InjectIntoFrame(IFrame frame, string url = null)
        {
            if (frame == null || !frame.IsValid) return;
            if (!string.IsNullOrEmpty(url) && (InternalPageHelper.IsStartPage(url) || WebErrorPage.IsErrorPage(url)))
            {
                return;
            }

            try
            {
                frame.ExecuteJavaScriptAsync(FallbackScript);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Registers the passkey fallback script via Chrome DevTools Protocol (CDP) `Page.addScriptToEvaluateOnNewDocument`.
        /// When registered, Chromium evaluates this script on every document and iframe before any page scripts execute.
        /// </summary>
        public static void RegisterOnBrowserHost(IBrowserHost host)
        {
            if (host == null || host.IsDisposed) return;

            try
            {
                int msgId = host.GetNextDevToolsMessageId();
                string escapedScript = EscapeJson(FallbackScript);
                string jsonParams = "{\"source\":\"" + escapedScript + "\"}";
                host.ExecuteDevToolsMethod(msgId, "Page.addScriptToEvaluateOnNewDocument", jsonParams);
            }
            catch
            {
            }
        }

        private static string EscapeJson(string str)
        {
            return JsHelper.EscapeJson(str);
        }
    }
}
