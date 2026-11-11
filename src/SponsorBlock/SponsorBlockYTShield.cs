using System;
using System.Text;
using wb.AdBlock;
using wb.Core;

namespace wb.SponsorBlock
{
    /// <summary>
    /// Provides client-side JavaScript injection for YouTube player SponsorBlock integration.
    /// Handles video detection, skip segments retrieval via C# backend, timeline preview bar rendering,
    /// player control buttons matching official SponsorBlock, 0% CPU auto-skipping and interactive skip notices.
    /// </summary>
    public static class SponsorBlockYTShield
    {
        /// <summary>
        /// Base64 data URL for the authentic SponsorBlock icon from assets/icons/sponsorblock.png.
        /// Injected directly into the YouTube player controls as an img element.
        /// </summary>
        public const string IconDataUrl = "data:image/png;base64,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";

        public static string GetSponsorBlockIconDataUrl()
        {
            return IconDataUrl;
        }

private const string SvgClose = @"<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 256 256"" width=""12"" height=""12"" style=""width:12px;height:12px;display:block;margin:auto;pointer-events:none;""><path fill=""currentColor"" d=""M205.66,194.34a8,8,0,0,1-11.32,11.32L128,139.31,61.66,205.66a8,8,0,0,1-11.32-11.32L116.69,128,50.34,61.66A8,8,0,0,1,61.66,50.34L128,116.69l66.34-66.35a8,8,0,0,1,11.32,11.32L139.31,128Z""/></svg>";

        public static bool IsYouTubeDomain(string urlOrDomain)
        {
            return UrlHelper.IsYouTubeDomain(urlOrDomain);
        }

        public static string GetSponsorBlockScript(string configJson)
        {
            if (string.IsNullOrEmpty(configJson))
            {
                configJson = "{}";
            }

            var colorsBuilder = new StringBuilder();
            var namesBuilder = new StringBuilder();
            foreach (var cat in SponsorCategory.All)
            {
                if (colorsBuilder.Length > 0) colorsBuilder.Append(",\n        ");
                colorsBuilder.AppendFormat("'{0}': '{1}'", cat.Key, cat.ColorHex);

                if (namesBuilder.Length > 0) namesBuilder.Append(",\n        ");
                namesBuilder.AppendFormat("'{0}': '{1}'", cat.Key, EscapeJs(cat.Name));
            }

            // JavaScript payload wrapped in self-executing closure with config parameters
            string script = @"(function(userConfig) {
    'use strict';

    if (window.__wbSponsorBlockInjected) {
        if (typeof window.__wbSponsorBlockUpdateConfig === 'function') {
            window.__wbSponsorBlockUpdateConfig(userConfig);
        }
        return;
    }
    window.__wbSponsorBlockInjected = true;

    function getCanonicalCategory(cat) {
        if (!cat) return '';
        var cleanCat = String(cat).toLowerCase().trim().replace(/[-_]/g, '');
        if (cleanCat === 'intermission' || cleanCat === 'intro') return 'intro';
        if (cleanCat === 'endcards' || cleanCat === 'outro') return 'outro';
        if (cleanCat === 'selfpromo' || cleanCat === 'selfpromotion') return 'selfpromo';
        if (cleanCat === 'musicofftopic' || cleanCat === 'nonmusic') return 'music_offtopic';
        if (cleanCat === 'poihighlight' || cleanCat === 'highlight' || cleanCat === 'pointofinterest') return 'poi_highlight';
        if (cleanCat === 'interaction' || cleanCat === 'subscribe') return 'interaction';
        if (cleanCat === 'preview' || cleanCat === 'recap') return 'preview';
        if (cleanCat === 'filler' || cleanCat === 'tangent') return 'filler';
        if (cleanCat === 'sponsor') return 'sponsor';
        if (cleanCat === 'chapter') return 'chapter';
        return cleanCat;
    }

    function normalizeCategoryActions(rawActions) {
        var result = {};
        if (!rawActions || typeof rawActions !== 'object') return result;
        for (var k in rawActions) {
            if (Object.prototype.hasOwnProperty.call(rawActions, k)) {
                var val = rawActions[k];
                var normVal = 'skip';
                if (typeof val === 'number') {
                    if (val === 1) normVal = 'manual';
                    else if (val === 2) normVal = 'mute';
                    else if (val === 3) normVal = 'disabled';
                    else normVal = 'skip';
                } else if (typeof val === 'string') {
                    var s = val.trim().toLowerCase();
                    if (s === '1' || s === 'manual' || s === 'manualskip' || s === 'prompt') normVal = 'manual';
                    else if (s === '2' || s === 'mute') normVal = 'mute';
                    else if (s === '3' || s === 'disabled' || s === 'disable' || s === 'off') normVal = 'disabled';
                    else normVal = 'skip';
                }
                var keyLower = k.toLowerCase().trim();
                var cleanKey = keyLower.replace(/[-_]/g, '');
                var canonKey = getCanonicalCategory(k);
                if (canonKey) result[canonKey] = normVal;
                result[keyLower] = normVal;
                result[cleanKey] = normVal;
            }
        }
        return result;
    }

    var config = userConfig || {};
    var isEnabled = config.IsEnabled !== false;
    var showPreviewBar = config.ShowPreviewBar !== false;
    var showNotice = config.ShowNotice !== false;
    var noticeDuration = (config.NoticeDurationSeconds || 4) * 1000;
    var categoryActions = normalizeCategoryActions(config.CategoryActions || config.categoryActions);
    var whitelistedVideos = config.WhitelistedVideoIds || config.whitelistedVideoIds || [];

    var categoryColors = {
        " + colorsBuilder.ToString() + @"
    };

    var categoryNames = {
        " + namesBuilder.ToString() + @"
    };

    var currentVideoId = null;
    var currentSegments = [];
    var skippedMap = {};
    var unskippedMap = {};
    var unmutedMap = {};
    var shownManualToasts = {};
    var manualSeekedSegments = {};
    var hasStartedPlayback = false;
    var isVideoEnded = false;
    var activeToast = null;
    var activeToastSeg = null;
    var activeToastMode = null;
    var activeToastIsHovered = false;
    var toastTimer = null;
    var wasMutedBySb = false;
    var previousMutedState = false;
    var cachedVideo = null;

    function scheduleToastDismiss(delayMs) {
        if (toastTimer) {
            clearTimeout(toastTimer);
            toastTimer = null;
        }
        if (typeof delayMs !== 'number' || delayMs <= 0) delayMs = 1000;
        toastTimer = setTimeout(function() {
            if (activeToastIsHovered) {
                scheduleToastDismiss(1500);
            } else {
                dismissToast();
            }
        }, delayMs);
    }

    var iconDataUrl = '" + IconDataUrl + @"';
    var svgCloseHtml = '" + SvgClose + @"';

    // Helper functions for wb Material Design aesthetics & WCAG contrast compliance
    function hexToRgba(hex, alpha) {
        if (!hex || typeof hex !== 'string' || hex.charAt(0) !== '#') return 'rgba(255, 255, 255, ' + alpha + ')';
        var r = 0, g = 0, b = 0;
        if (hex.length === 4) {
            r = parseInt(hex.charAt(1) + hex.charAt(1), 16);
            g = parseInt(hex.charAt(2) + hex.charAt(2), 16);
            b = parseInt(hex.charAt(3) + hex.charAt(3), 16);
        } else if (hex.length >= 7) {
            r = parseInt(hex.substr(1, 2), 16);
            g = parseInt(hex.substr(3, 2), 16);
            b = parseInt(hex.substr(5, 2), 16);
        }
        return 'rgba(' + (isNaN(r) ? 255 : r) + ', ' + (isNaN(g) ? 255 : g) + ', ' + (isNaN(b) ? 255 : b) + ', ' + alpha + ')';
    }

    function getContrastColor(hex) {
        if (!hex || typeof hex !== 'string' || hex.charAt(0) !== '#') return '#ffffff';
        var r = 0, g = 0, b = 0;
        if (hex.length === 4) {
            r = parseInt(hex.charAt(1) + hex.charAt(1), 16);
            g = parseInt(hex.charAt(2) + hex.charAt(2), 16);
            b = parseInt(hex.charAt(3) + hex.charAt(3), 16);
        } else if (hex.length >= 7) {
            r = parseInt(hex.substr(1, 2), 16);
            g = parseInt(hex.substr(3, 2), 16);
            b = parseInt(hex.substr(5, 2), 16);
        }
        var lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
        return lum > 0.55 ? '#202124' : '#ffffff';
    }

    // Inject CSS styles for timeline markers, tooltips, notices and player buttons
    function injectStyles() {
        if (document.getElementById('wb-sb-styles')) return;
        var style = document.createElement('style');
        style.id = 'wb-sb-styles';
        style.textContent = `
            #previewbar, .wb-sb-previewbar {
                position: absolute !important;
                top: 0 !important;
                left: 0 !important;
                width: 100% !important;
                height: 100% !important;
                pointer-events: none !important;
                z-index: 34 !important;
                margin: 0 !important;
                padding: 0 !important;
            }
            .wb-sb-marker, .previewbar {
                position: absolute !important;
                top: 0 !important;
                height: 100% !important;
                pointer-events: auto !important;
                cursor: pointer !important;
                opacity: 0.9 !important;
                border-radius: 1px !important;
                transition: opacity 0.15s ease, transform 0.15s ease !important;
                z-index: 35 !important;
            }
            .wb-sb-marker:hover, .previewbar:hover {
                opacity: 1 !important;
                transform: scaleY(1.3) !important;
                filter: brightness(1.25) !important;
                z-index: 36 !important;
            }
            .playerButton.ytp-button {
                display: inline-flex !important;
                align-items: center !important;
                justify-content: center !important;
                vertical-align: top !important;
                width: 40px !important;
                height: 100% !important;
                cursor: pointer !important;
                background: transparent !important;
                border: none !important;
                padding: 0 !important;
                opacity: 0.92 !important;
                box-sizing: border-box !important;
                transition: opacity 0.15s ease, background-color 0.15s ease !important;
            }
            .playerButton.ytp-button:hover {
                opacity: 1 !important;
            }
            #startSegmentButton.playerButton {
                display: none !important;
            }
            #infoButton.playerButton {
                width: 40px !important;
                height: 100% !important;
                margin: 0 !important;
                background: transparent !important;
                border: none !important;
                padding: 0 !important;
                display: flex !important;
                align-items: center !important;
                justify-content: center !important;
                cursor: pointer !important;
            }
            #infoButton.playerButton:hover {
                opacity: 1 !important;
                background: transparent !important;
            }
            #infoButton.playerButton svg,
            #infoButton.playerButton img,
            .playerButtonImage {
                width: 24px !important;
                height: 24px !important;
                display: block !important;
                margin: auto !important;
                pointer-events: none !important;
                shape-rendering: geometricPrecision !important;
            }
            .wb-sb-tooltip {
                position: fixed !important;
                padding: 5px 9px !important;
                background: rgba(32, 33, 36, 0.96) !important;
                color: #e8eaed !important;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif !important;
                font-size: 11px !important;
                font-weight: 500 !important;
                border-radius: 4px !important;
                border: 1px solid rgba(255, 255, 255, 0.12) !important;
                box-shadow: 0 4px 16px rgba(0, 0, 0, 0.5), inset 0 1px 0 rgba(255, 255, 255, 0.08) !important;
                pointer-events: none !important;
                z-index: 999999 !important;
                display: none !important;
                white-space: nowrap !important;
                backdrop-filter: blur(12px) saturate(180%) !important;
                -webkit-backdrop-filter: blur(12px) saturate(180%) !important;
            }

            /* wb Design Language: SponsorBlock Skip Banner / Toast
               Follows wb floating overlay architecture (Material #202124 surface, 1px border) */
            .wb-sb-toast {
                position: absolute !important;
                bottom: 74px !important;
                right: 20px !important;
                display: inline-flex !important;
                align-items: center !important;
                gap: 5px !important;
                padding: 4px 5px !important;
                background: rgba(32, 33, 36, 0.95) !important;
                color: #e8eaed !important;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif !important;
                font-size: 12px !important;
                font-weight: 500 !important;
                line-height: normal !important;
                border-radius: 5px !important;
                border: 1px solid rgba(255, 255, 255, 0.12) !important;
                box-shadow: 0 6px 20px rgba(0, 0, 0, 0.6), 0 2px 6px rgba(0, 0, 0, 0.35), inset 0 1px 0 rgba(255, 255, 255, 0.08) !important;
                z-index: 2147483647 !important;
                pointer-events: auto !important;
                backdrop-filter: blur(16px) saturate(180%) !important;
                -webkit-backdrop-filter: blur(16px) saturate(180%) !important;
                animation: wbSbSlideIn 0.22s cubic-bezier(0.16, 1, 0.3, 1) !important;
                transition: bottom 0.25s cubic-bezier(0.16, 1, 0.3, 1), opacity 0.22s cubic-bezier(0.16, 1, 0.3, 1), transform 0.22s cubic-bezier(0.16, 1, 0.3, 1) !important;
                user-select: none !important;
                -webkit-user-select: none !important;
                overflow: hidden !important;
                max-width: calc(100% - 40px) !important;
                box-sizing: border-box !important;
            }
            .ytp-autohide .wb-sb-toast {
                bottom: 24px !important;
            }
            .wb-sb-toast.wb-sb-hiding {
                opacity: 0 !important;
                transform: translateY(8px) scale(0.98) !important;
                pointer-events: none !important;
            }
            @keyframes wbSbSlideIn {
                from {
                    opacity: 0;
                    transform: translateY(12px) scale(0.97);
                }
                to {
                    opacity: 1;
                    transform: translateY(0) scale(1);
                }
            }

            .wb-sb-accent-bar {
                display: none !important;
            }

            /* Sleek category container (same height and consistent font size as unskip button) */
            .wb-sb-pill {
                display: inline-flex !important;
                align-items: center !important;
                gap: 6px !important;
                height: 26px !important;
                padding: 0 9px !important;
                border-radius: 4px !important;
                border: 1px solid rgba(255, 255, 255, 0.12) !important;
                font-family: inherit !important;
                font-size: 12px !important;
                font-weight: 600 !important;
                letter-spacing: 0.2px !important;
                line-height: normal !important;
                white-space: nowrap !important;
                flex-shrink: 0 !important;
                box-sizing: border-box !important;
            }
            .wb-sb-dot {
                width: 7px !important;
                height: 7px !important;
                border-radius: 50% !important;
                flex-shrink: 0 !important;
            }
            .wb-sb-pill-text {
                max-width: 160px !important;
                overflow: hidden !important;
                text-overflow: ellipsis !important;
                white-space: nowrap !important;
                color: #e8eaed !important;
                line-height: normal !important;
                display: inline-block !important;
                vertical-align: middle !important;
                padding: 2px 0 !important;
                box-sizing: border-box !important;
            }

            /* Mute status badge (wb design language, 26px height, compact, accessible contrast) */
            .wb-sb-badge-muted {
                display: inline-flex !important;
                align-items: center !important;
                gap: 5px !important;
                height: 26px !important;
                padding: 0 8px !important;
                border-radius: 4px !important;
                background: rgba(234, 67, 53, 0.16) !important;
                border: 1px solid rgba(234, 67, 53, 0.35) !important;
                color: #f28b82 !important;
                font-family: inherit !important;
                font-size: 11px !important;
                font-weight: 600 !important;
                letter-spacing: 0.2px !important;
                line-height: normal !important;
                white-space: nowrap !important;
                flex-shrink: 0 !important;
                box-sizing: border-box !important;
            }

            /* Action Button (same height and consistent font size as category container, docked to edge) */
            .wb-sb-btn-action {
                appearance: none !important;
                -webkit-appearance: none !important;
                display: inline-flex !important;
                align-items: center !important;
                justify-content: center !important;
                height: 26px !important;
                border-radius: 4px !important;
                padding: 0 11px !important;
                margin: 0 !important;
                font-family: inherit !important;
                font-size: 12px !important;
                font-weight: 600 !important;
                letter-spacing: 0.2px !important;
                line-height: normal !important;
                overflow: visible !important;
                cursor: pointer !important;
                transition: background 0.15s ease, border-color 0.15s ease, transform 0.1s ease, filter 0.15s ease !important;
                flex-shrink: 0 !important;
                outline: none !important;
                box-sizing: border-box !important;
            }
            .wb-sb-btn-secondary {
                background: rgba(255, 255, 255, 0.08) !important;
                color: #e8eaed !important;
                border: 1px solid rgba(255, 255, 255, 0.16) !important;
            }
            .wb-sb-btn-secondary:hover {
                background: rgba(255, 255, 255, 0.18) !important;
                border-color: rgba(255, 255, 255, 0.32) !important;
                color: #ffffff !important;
            }
            .wb-sb-btn-secondary:active {
                background: rgba(255, 255, 255, 0.24) !important;
                transform: scale(0.97) !important;
            }
            .wb-sb-btn-primary {
                border: 1px solid transparent !important;
                box-shadow: 0 1px 3px rgba(0, 0, 0, 0.3) !important;
            }
            .wb-sb-btn-primary:hover {
                filter: brightness(1.1) !important;
            }
            .wb-sb-btn-primary:active {
                filter: brightness(0.92) !important;
                transform: scale(0.97) !important;
            }

            /* Close / Dismiss button (wb MaterialIcons x-icon style) */
            .wb-sb-toast-close {
                appearance: none !important;
                -webkit-appearance: none !important;
                display: flex !important;
                align-items: center !important;
                justify-content: center !important;
                width: 24px !important;
                height: 24px !important;
                background: transparent !important;
                border: none !important;
                border-radius: 4px !important;
                color: #9aa0a6 !important;
                cursor: pointer !important;
                padding: 0 !important;
                line-height: 1 !important;
                flex-shrink: 0 !important;
                transition: background 0.15s ease, color 0.15s ease !important;
                outline: none !important;
            }
            .wb-sb-toast-close:hover {
                background: rgba(255, 255, 255, 0.10) !important;
                color: #ffffff !important;
            }
            .wb-sb-toast-close:active {
                background: rgba(255, 255, 255, 0.18) !important;
            }
        `;
        (document.head || document.documentElement).appendChild(style);
    }

    injectStyles();

    // Tooltip singleton
    var tooltip = document.createElement('div');
    tooltip.className = 'wb-sb-tooltip';
    (document.body || document.documentElement).appendChild(tooltip);

    function showTooltip(x, y, text) {
        tooltip.textContent = text;
        tooltip.style.left = (x + 10) + 'px';
        tooltip.style.top = (y - 32) + 'px';
        tooltip.style.display = 'block';
    }

    function hideTooltip() {
        tooltip.style.display = 'none';
    }

    // Video element reference helper (cached to avoid querySelector overhead)
    function getVideo() {
        if (cachedVideo && cachedVideo.parentNode && document.contains(cachedVideo)) return cachedVideo;
        cachedVideo = document.querySelector('video.html5-main-video') ||
                      document.querySelector('.html5-video-player video') ||
                      document.querySelector('video');
        if (cachedVideo) {
            attachVideoListeners(cachedVideo);
        }
        return cachedVideo;
    }

    // Detect whether an in-stream advert or interruption is currently active in the YouTube player
    function isPlayerInAd() {
        var player = document.getElementById('movie_player') || document.querySelector('.html5-video-player');
        if (!player) {
            return document.querySelector('.ad-showing, .ad-interrupting') !== null;
        }

        // 1. Direct YouTube player CSS class markers for active ad interruptions
        if (player.classList.contains('ad-showing') ||
            player.classList.contains('ad-interrupting') ||
            player.classList.contains('ad-created')) {
            return true;
        }

        // 2. YouTube HTML5 Player API ad queries
        try {
            if (typeof player.getAdState === 'function' && player.getAdState() > 0) return true;
            if (typeof player.isAd === 'function' && player.isAd()) return true;
        } catch(e) {}

        // 3. YouTube ad player overlay & module containers
        var adModule = player.querySelector('.video-ads.ytp-ad-module, .video-ads');
        if (adModule && adModule.children.length > 0) {
            return true;
        }

        var adOverlay = player.querySelector('.ytp-ad-player-overlay-layout, .ytp-ad-player-overlay');
        if (adOverlay && (adOverlay.offsetWidth > 0 || adOverlay.offsetHeight > 0 || adOverlay.children.length > 0)) {
            return true;
        }

        // 4. Specific YouTube ad badge, countdown, or skip button markers
        if (player.querySelector('.ytp-ad-badge, .ytp-ad-simple-ad-badge, .ytp-ad-duration-remaining, .ytp-ad-text, .ytp-ad-skip-button, .ytp-ad-skip-button-modern, .ytp-skip-ad-button, .ytp-ad-preview-text, .ytp-ad-survey')) {
            return true;
        }

        return false;
    }

    // Validates whether video element and player are genuinely ready and playing the target video
    function isPlaybackReady(allowPaused) {
        if (!isEnabled || !currentVideoId) return false;
        if (isPlayerInAd()) return false;

        var player = document.getElementById('movie_player') || document.querySelector('.html5-video-player');
        if (player) {
            // Verify player has loaded the target video and not an ad or previous video
            try {
                if (typeof player.getVideoData === 'function') {
                    var vData = player.getVideoData();
                    if (vData && vData.video_id && vData.video_id !== currentVideoId) {
                        return false;
                    }
                }
            } catch(e) {}

            // Verify player state is not unstarted (-1) or cued (5)
            try {
                if (typeof player.getPlayerState === 'function') {
                    var state = player.getPlayerState();
                    if (state === -1 || state === 5) {
                        return false;
                    }
                    if (!allowPaused && state === 2) {
                        return false;
                    }
                }
            } catch(e) {}
        }

        var v = getVideo();
        if (!v) return false;
        if (!allowPaused && (v.paused || v.ended)) return false;
        if (v.readyState < 2) return false;
        if (v.seeking) return false;
        if (!v.duration || isNaN(v.duration) || v.duration <= 0) return false;

        return true;
    }

    // Video ID extractor from URL
    function extractVideoId() {
        try {
            var url = new URL(window.location.href);
            if (url.searchParams.has('v')) {
                return url.searchParams.get('v');
            }
            var path = url.pathname.split('/').filter(Boolean);
            if (path.length >= 2 && (path[0] === 'shorts' || path[0] === 'embed' || path[0] === 'live')) {
                return path[1];
            }
        } catch(e) {}
        return null;
    }

    function formatTime(sec) {
        if (!sec || sec < 0) sec = 0;
        var m = Math.floor(sec / 60);
        var s = Math.floor(sec % 60);
        var h = Math.floor(m / 60);
        m = m % 60;
        if (h > 0) {
            return h + ':' + (m < 10 ? '0' : '') + m + ':' + (s < 10 ? '0' : '') + s;
        }
        return (m < 10 ? '0' : '') + m + ':' + (s < 10 ? '0' : '') + s;
    }

    function getAction(seg) {
        if (!seg) return 'disabled';
        var rawCat = typeof seg === 'string' ? seg : (seg.category || '');
        if (!rawCat) return 'disabled';
        var cat = String(rawCat).toLowerCase().trim();
        var cleanCat = cat.replace(/[-_]/g, '');
        var canonical = getCanonicalCategory(cat);

        var userAction = null;
        if (categoryActions) {
            userAction = categoryActions[canonical] ||
                         categoryActions[cleanCat] ||
                         categoryActions[cat] ||
                         (typeof seg === 'object' && seg.category ? categoryActions[seg.category] : null);
        }

        if (userAction && typeof userAction === 'string') {
            userAction = userAction.trim().toLowerCase();
            if (userAction === 'disabled' || userAction === 'disable' || userAction === 'off' || userAction === '3') return 'disabled';
            if (userAction === 'manual' || userAction === 'manualskip' || userAction === 'prompt' || userAction === '1') return 'manual';
            if (userAction === 'mute' || userAction === '2') return 'mute';
            if (userAction === 'skip' || userAction === 'autoskip' || userAction === 'auto' || userAction === '0') return 'skip';
        }

        // Fallback default actions if not explicitly configured by user
        if (typeof seg === 'object' && seg.actionType === 'mute') return 'mute';
        if (canonical === 'intro' || canonical === 'outro' || canonical === 'filler') return 'manual';
        if (canonical === 'poi_highlight') return 'disabled';
        return 'skip';
    }

    // Request segments through C# backend to completely bypass YouTube page CSP restrictions
    function requestSegments(vid) {
        if (!vid) return;
        if (!isEnabled || whitelistedVideos.indexOf(vid) !== -1) {
            currentSegments = [];
            renderPreviewBar();
            return;
        }

        // console.log is intercepted by CefSharp BrowserDisplayHandler.OnConsoleMessage and routed to C#
        console.log('wb-action:sponsorblock-fetch?videoid=' + encodeURIComponent(vid));
    }

    function processSegments(vid, data) {
        if (vid !== currentVideoId) return;
        if (!data || !Array.isArray(data)) {
            currentSegments = [];
        } else {
            var list = [];
            for (var i = 0; i < data.length; i++) {
                var d = data[i];
                if (d && d.segment && d.segment.length >= 2) {
                    list.push({
                        uuid: d.UUID || ('sb_' + i),
                        category: d.category || 'sponsor',
                        actionType: d.actionType || 'skip',
                        start: d.segment[0],
                        end: d.segment[1],
                        videoDuration: d.videoDuration || 0
                    });
                }
            }
            list.sort(function(a, b) { return a.start - b.start; });
            currentSegments = list;
        }
        attachVideoListeners();
        renderPreviewBar();
        updatePlayerControlsButtons();
        if (hasStartedPlayback && isPlaybackReady(false)) {
            checkPlayback();
        }
    }

    // Timeline Preview Bar markers rendering
    function renderPreviewBar() {
        var existing = document.getElementById('previewbar');
        if (existing) existing.remove();

        if (!showPreviewBar || !isEnabled || currentSegments.length === 0) return;
        if (isPlayerInAd()) return;

        var player = document.getElementById('movie_player') || document.querySelector('.html5-video-player');
        if (player && typeof player.getVideoData === 'function') {
            try {
                var vData = player.getVideoData();
                if (vData && vData.video_id && vData.video_id !== currentVideoId) return;
            } catch(e) {}
        }

        var progressBar = document.querySelector('.ytp-progress-bar') ||
                          document.querySelector('.ytProgressBarLineHost') ||
                          document.querySelector('.YtProgressBarLineHost') ||
                          document.querySelector('.ytp-progress-list');
        if (!progressBar) return;

        var v = getVideo();
        var duration = v && v.duration && !isNaN(v.duration) && v.duration > 0 ? v.duration : 0;
        if (!duration || duration <= 0) {
            var ariaVal = parseFloat(progressBar.getAttribute('aria-valuemax'));
            if (ariaVal && !isNaN(ariaVal) && ariaVal > 0) {
                duration = ariaVal;
            }
        }
        if (!duration || duration <= 0) {
            for (var i = 0; i < currentSegments.length; i++) {
                if (currentSegments[i].videoDuration > 0) {
                    duration = currentSegments[i].videoDuration;
                    break;
                }
            }
        }
        if (!duration || duration <= 0) return;

        var barContainer = document.createElement('div');
        barContainer.id = 'previewbar';
        barContainer.className = 'wb-sb-previewbar';

        for (var k = 0; k < currentSegments.length; k++) {
            var seg = currentSegments[k];
            var action = getAction(seg);
            if (action === 'disabled') continue;

            var leftPct = Math.max(0, (seg.start / duration) * 100);
            var widthPct = Math.max(0.2, ((seg.end - seg.start) / duration) * 100);
            if (leftPct + widthPct > 100) widthPct = 100 - leftPct;

            var canonCat = getCanonicalCategory(seg.category);
            var marker = document.createElement('div');
            marker.className = 'wb-sb-marker previewbar';
            marker.style.left = leftPct.toFixed(3) + '%';
            marker.style.width = widthPct.toFixed(3) + '%';
            marker.style.backgroundColor = categoryColors[canonCat] || categoryColors[seg.category] || '#00d400';

            (function(s) {
                marker.addEventListener('mouseenter', function(e) {
                    var canonKey = getCanonicalCategory(s.category);
                    var catTitle = categoryNames[canonKey] || categoryNames[s.category] || s.category;
                    var label = catTitle + ' [' + formatTime(s.start) + ' - ' + formatTime(s.end) + ']';
                    showTooltip(e.clientX, e.clientY, label);
                });
                marker.addEventListener('mousemove', function(e) {
                    var canonKey = getCanonicalCategory(s.category);
                    var catTitle = categoryNames[canonKey] || categoryNames[s.category] || s.category;
                    var label = catTitle + ' [' + formatTime(s.start) + ' - ' + formatTime(s.end) + ']';
                    showTooltip(e.clientX, e.clientY, label);
                });
                marker.addEventListener('mouseleave', function() {
                    hideTooltip();
                });
                marker.addEventListener('click', function(e) {
                    e.stopPropagation();
                    manualSeekedSegments[s.uuid] = true;
                    delete skippedMap[s.uuid];
                    delete unskippedMap[s.uuid];
                    delete shownManualToasts[s.uuid];
                    var vidEl = getVideo();
                    var player = document.getElementById('movie_player') || document.querySelector('.html5-video-player');
                    if (player && typeof player.seekTo === 'function') {
                        try { player.seekTo(s.start, true); } catch(e) {}
                    }
                    if (vidEl) vidEl.currentTime = s.start;
                    var act = getAction(s);
                    if (act !== 'disabled') {
                        shownManualToasts[s.uuid] = true;
                        showSkipToast(s, 'manual');
                    }
                });
            })(seg);

            barContainer.appendChild(marker);
        }

        progressBar.appendChild(barContainer);
    }

    // Skip Notification Toast (Supports auto-skip revert, manual skip prompt and mute indicator)
    function showSkipToast(seg, mode) {
        if (!showNotice) return;
        dismissToast(true);

        var player = document.getElementById('movie_player') ||
                     document.querySelector('.html5-video-player') ||
                     document.querySelector('#player-container') ||
                     document.body;

        var canonCat = getCanonicalCategory(seg.category);
        var catColor = categoryColors[canonCat] || categoryColors[seg.category] || '#00d400';
        var catName = categoryNames[canonCat] || categoryNames[seg.category] || seg.category;

        var toast = document.createElement('div');
        toast.className = 'wb-sb-toast';
        if (player === document.body) {
            toast.style.position = 'fixed';
        }
        toast.addEventListener('click', function(e) { e.stopPropagation(); });

        // Category container (same height & font size as unskip button)
        var pill = document.createElement('div');
        pill.className = 'wb-sb-pill';
        pill.style.backgroundColor = hexToRgba(catColor, 0.12);
        pill.style.borderColor = hexToRgba(catColor, 0.28);

        var dot = document.createElement('span');
        dot.className = 'wb-sb-dot';
        dot.style.backgroundColor = catColor;
        dot.style.boxShadow = '0 0 6px ' + hexToRgba(catColor, 0.85);
        pill.appendChild(dot);

        var pillText = document.createElement('span');
        pillText.className = 'wb-sb-pill-text';
        pillText.textContent = catName;
        pill.appendChild(pillText);

        toast.appendChild(pill);

        if (mode === 'mute') {
            // Mute mode: Inform user that audio is muted & provide quick Unmute option
            var muteBadge = document.createElement('div');
            muteBadge.className = 'wb-sb-badge-muted';
            muteBadge.innerHTML = '<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 24 24"" width=""13"" height=""13"" style=""fill:currentColor;flex-shrink:0;""><path d=""M16.5 12c0-1.77-1.02-3.29-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51C20.63 14.91 21 13.5 21 12c0-4.28-2.99-7.86-7-8.77v2.06c2.89.86 5 3.54 5 6.71zM4.27 3L3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06c1.38-.31 2.63-.95 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4L9.91 6.09 12 8.18V4z""/></svg><span>' +
                '" + EscapeJs(AppStrings.SponsorBlock.StatusMuted) + @"' + '</span>';
            toast.appendChild(muteBadge);

            var unmuteBtn = document.createElement('button');
            unmuteBtn.className = 'wb-sb-btn-action wb-sb-btn-secondary';
            unmuteBtn.textContent = '" + EscapeJs(AppStrings.SponsorBlock.BtnUnmute) + @"';
            unmuteBtn.addEventListener('click', function(e) {
                e.stopPropagation();
                var v = getVideo();
                if (v) {
                    v.muted = false;
                    wasMutedBySb = false;
                    unmutedMap[seg.uuid] = true;
                }
                dismissToast();
            });
            toast.appendChild(unmuteBtn);
        } else if (mode === 'manual') {
            // Manual skip mode: Interactive prompt showing [Skip] option
            var skipBtn = document.createElement('button');
            skipBtn.className = 'wb-sb-btn-action wb-sb-btn-primary';
            skipBtn.style.backgroundColor = catColor;
            skipBtn.style.color = getContrastColor(catColor);
            skipBtn.textContent = '" + EscapeJs(AppStrings.SponsorBlock.BtnSkip) + @"';
            skipBtn.addEventListener('click', function(e) {
                e.stopPropagation();
                var v = getVideo();
                if (v) {
                    skippedMap[seg.uuid] = true;
                    delete manualSeekedSegments[seg.uuid];
                    var saved = Math.max(0, seg.end - v.currentTime);
                    var p = document.getElementById('movie_player') || document.querySelector('.html5-video-player');
                    var dur = (v.duration && isFinite(v.duration) && v.duration > 0) ? v.duration : (seg.videoDuration || 0);
                    var isEnd = (dur > 0 && (seg.end >= dur || (dur > 2.0 && seg.end >= (dur - 1.0))));

                    if (isEnd) {
                        // End-of-video segment: Transition to YouTube end card / end screen instead of pausing on last frame
                        isVideoEnded = true;
                        v.currentTime = dur;
                        try {
                            v.dispatchEvent(new Event('ended'));
                        } catch(e) {}
                        if (p) {
                            p.classList.add('ended-mode');
                            p.classList.remove('playing-mode', 'paused-mode');
                            var endcards = p.querySelectorAll('.ytp-ce-element');
                            for (var j = 0; j < endcards.length; j++) {
                                endcards[j].classList.add('ytp-ce-element-show');
                            }
                            var endscreen = p.querySelector('.html5-endscreen') || p.querySelector('.ytp-endscreen-content');
                            if (endscreen) {
                                endscreen.style.display = '';
                                endscreen.style.opacity = '1';
                            }
                        }
                    } else {
                        var targetEnd = (dur > 0 && seg.end > dur) ? dur : seg.end;
                        if (p && typeof p.seekTo === 'function') {
                            try { p.seekTo(targetEnd, true); } catch(e) {}
                        }
                        v.currentTime = targetEnd;
                    }
                    console.log('wb-action:sponsorblock-skip?videoid=' + encodeURIComponent(currentVideoId || '') +
                                '&category=' + encodeURIComponent(seg.category) +
                                '&saved=' + saved.toFixed(2));
                }
                dismissToast();
            });
            toast.appendChild(skipBtn);
        } else {
            // Auto skip mode: [Unskip] button allowing user to revert jump
            var unskipBtn = document.createElement('button');
            unskipBtn.className = 'wb-sb-btn-action wb-sb-btn-secondary';
            unskipBtn.textContent = '" + EscapeJs(AppStrings.SponsorBlock.BtnUnskip) + @"';
            unskipBtn.addEventListener('click', function(e) {
                e.stopPropagation();
                var v = getVideo();
                if (v) {
                    var p = document.getElementById('movie_player') || document.querySelector('.html5-video-player');
                    if (p) {
                        p.classList.remove('ended-mode');
                    }
                    if (p && typeof p.seekTo === 'function') {
                        try { p.seekTo(seg.start, true); } catch(e) {}
                    }
                    v.currentTime = seg.start;
                    unskippedMap[seg.uuid] = true;
                    delete skippedMap[seg.uuid];
                    try { v.play(); } catch(e) {}
                    if (p && typeof p.playVideo === 'function') {
                        try { p.playVideo(); } catch(e) {}
                    }
                }
                dismissToast();
            });
            toast.appendChild(unskipBtn);
        }

        // Close / Dismiss button
        var closeBtn = document.createElement('button');
        closeBtn.className = 'wb-sb-toast-close';
        closeBtn.setAttribute('title', '" + EscapeJs(AppStrings.General.BtnClose) + @"');
        setElementSvg(closeBtn, svgCloseHtml);
        closeBtn.addEventListener('click', function(e) {
            e.stopPropagation();
            dismissToast();
        });
        toast.appendChild(closeBtn);

        // Hover awareness: delay auto-dismiss while mouse is over toast (matches FullScreenExitOverlay)
        toast.addEventListener('mouseenter', function() {
            activeToastIsHovered = true;
        });
        toast.addEventListener('mouseleave', function() {
            activeToastIsHovered = false;
            scheduleToastDismiss(1500);
        });

        player.appendChild(toast);
        activeToast = toast;
        activeToastSeg = seg;
        activeToastMode = mode;

        // Display duration:
        // For 'manual' and 'mute': display exactly for the period of the highlighted area.
        // For 'skip' (autoskip): segment is already skipped, so display for configured noticeDuration.
        var displayDurationMs = (config.NoticeDurationSeconds || 4) * 1000;
        var v = getVideo();
        if (mode === 'manual' || mode === 'mute') {
            if (seg && typeof seg.end === 'number' && typeof seg.start === 'number') {
                var curTime = (v && typeof v.currentTime === 'number') ? v.currentTime : seg.start;
                var rate = (v && typeof v.playbackRate === 'number' && v.playbackRate > 0) ? v.playbackRate : 1.0;
                var remainingSec = Math.max(0.1, seg.end - curTime);
                displayDurationMs = Math.round((remainingSec / rate) * 1000);
            }
        }

        if (!v || !v.paused) {
            scheduleToastDismiss(displayDurationMs);
        }
    }

    function dismissToast(immediate) {
        if (toastTimer) {
            clearTimeout(toastTimer);
            toastTimer = null;
        }
        activeToastSeg = null;
        activeToastMode = null;
        activeToastIsHovered = false;
        if (activeToast) {
            var target = activeToast;
            activeToast = null;
            if (immediate) {
                if (target && target.parentNode) target.parentNode.removeChild(target);
            } else {
                target.classList.add('wb-sb-hiding');
                setTimeout(function() {
                    if (target && target.parentNode) target.parentNode.removeChild(target);
                }, 250);
            }
        }
    }

    // Official SponsorBlock player controls buttons integration (.ytp-right-controls)
    function getControls() {
        var selectors = ['.ytp-right-controls', '.vjs-control-bar', '.shaka-bottom-controls', '.html5-player-chrome'];
        for (var i = 0; i < selectors.length; i++) {
            var el = document.querySelector(selectors[i]);
            if (el) return el;
        }
        return null;
    }

    function setElementSvg(el, svgHtml) {
        if (!el || !svgHtml) return;
        if (svgHtml.indexOf('xmlns=') === -1) {
            svgHtml = svgHtml.replace('<svg', '<svg xmlns=""http://www.w3.org/2000/svg""');
        }
        try {
            var parser = new DOMParser();
            var doc = parser.parseFromString(svgHtml, 'image/svg+xml');
            if (doc && doc.documentElement && doc.documentElement.nodeName !== 'parsererror') {
                while (el.firstChild) el.removeChild(el.firstChild);
                el.appendChild(document.importNode(doc.documentElement, true));
                return;
            }
        } catch(e) {}

        try {
            if (window.trustedTypes && typeof window.trustedTypes.createPolicy === 'function') {
                try {
                    if (!window.__wbSbPolicy) {
                        window.__wbSbPolicy = window.trustedTypes.createPolicy('wb-sb-svg', {
                            createHTML: function(s) { return s; }
                        });
                    }
                    if (window.__wbSbPolicy) {
                        el.innerHTML = window.__wbSbPolicy.createHTML(svgHtml);
                        return;
                    }
                } catch(e) {}
            }
        } catch(e) {}

        try {
            el.innerHTML = svgHtml;
        } catch(e) {}
    }

    function updatePlayerControlsButtons() {
        if (isPlayerInAd()) return;
        var controls = getControls();
        if (!controls) return;

        // Clean up redundant startSegmentButton if present in player DOM
        var startBtn = document.getElementById('startSegmentButton');
        if (startBtn && startBtn.parentNode) {
            startBtn.parentNode.removeChild(startBtn);
        }

        var infoBtn = document.getElementById('infoButton');
        if (!infoBtn) {
            infoBtn = document.createElement('button');
            infoBtn.id = 'infoButton';
            infoBtn.className = 'playerButton ytp-button';
            infoBtn.setAttribute('title', '" + EscapeJs(AppStrings.SponsorBlock.TooltipSettings) + @"');
            var iconImg = document.createElement('img');
            iconImg.className = 'playerButtonImage';
            iconImg.src = iconDataUrl;
            iconImg.alt = 'SponsorBlock';
            infoBtn.appendChild(iconImg);
            infoBtn.addEventListener('click', function(e) {
                e.stopPropagation();
                console.log('wb-action:sponsorblock-menu');
            });
        } else if (!infoBtn.querySelector('img.playerButtonImage')) {
            while (infoBtn.firstChild) infoBtn.removeChild(infoBtn.firstChild);
            var iconImg = document.createElement('img');
            iconImg.className = 'playerButtonImage';
            iconImg.src = iconDataUrl;
            iconImg.alt = 'SponsorBlock';
            infoBtn.appendChild(iconImg);
        }

        if (!controls.contains(infoBtn)) {
            controls.prepend(infoBtn);
        }

        if (infoBtn) {
            var count = currentSegments ? currentSegments.length : 0;
            var tip = count === 1 ?
                '" + EscapeJs(AppStrings.SponsorBlock.TooltipSegmentsFoundSingular) + @"' :
                (count > 1 ? ('" + EscapeJs(AppStrings.SponsorBlock.TooltipSegmentsFoundPlural) + @"').replace('{0}', count) :
                '" + EscapeJs(AppStrings.SponsorBlock.TooltipActive) + @"');
            infoBtn.setAttribute('title', tip);
        }
    }

    // High-precision playback tracking & auto-skipping engine (zero lag, event-driven)
    function checkPlayback() {
        if (!isEnabled || currentSegments.length === 0) return;
        if (isPlayerInAd()) {
            if (wasMutedBySb) {
                wasMutedBySb = false;
                var vMute = getVideo();
                if (vMute) vMute.muted = previousMutedState;
            }
            dismissToast(true);
            return;
        }

        if (!hasStartedPlayback) {
            if (isPlaybackReady(false)) {
                hasStartedPlayback = true;
            } else {
                return;
            }
        } else if (!isPlaybackReady(false)) {
            return;
        }

        var v = getVideo();
        if (!v || v.paused || v.ended) return;

        var ct = v.currentTime;
        var insideMuteSegment = false;
        var player = document.getElementById('movie_player') || document.querySelector('.html5-video-player');

        for (var i = 0; i < currentSegments.length; i++) {
            var seg = currentSegments[i];
            var segDur = (v.duration && isFinite(v.duration) && v.duration > 0) ? v.duration : (seg.videoDuration || 0);
            var isEndSeg = (segDur > 0 && (seg.end >= segDur || (segDur > 2.0 && seg.end >= (segDur - 1.0))));

            // If user sought back before segment start, allow it to trigger again
            // For segments near video beginning (start <= 1.5s), check if ct is at/near beginning (ct <= 0.5s)
            var isBeforeStart = (seg.start <= 1.5) ? (ct <= 0.5 || ct < (seg.start - 0.05)) : (ct < (seg.start - 0.5));
            if (isBeforeStart) {
                if (!isEndSeg || ct > 1.0) {
                    delete skippedMap[seg.uuid];
                    delete shownManualToasts[seg.uuid];
                    delete unskippedMap[seg.uuid];
                    delete unmutedMap[seg.uuid];
                    delete manualSeekedSegments[seg.uuid];
                }
            }

            var action = getAction(seg);
            if (action === 'disabled') continue;

            // Check if current playback time is inside this segment
            if (ct >= (seg.start - 0.05) && ct < (seg.end - 0.02)) {
                var isManualSeekOverride = manualSeekedSegments[seg.uuid] === true;

                if (action === 'skip' && !isManualSeekOverride) {
                    if (unskippedMap[seg.uuid] || skippedMap[seg.uuid]) continue;

                    skippedMap[seg.uuid] = true;
                    var saved = Math.max(0, seg.end - ct);

                    if (isEndSeg) {
                        // End-of-video segment: Transition to YouTube end card / end screen instead of pausing on last frame
                        isVideoEnded = true;
                        v.currentTime = segDur;
                        try {
                            v.dispatchEvent(new Event('ended'));
                        } catch(e) {}
                        if (player) {
                            player.classList.add('ended-mode');
                            player.classList.remove('playing-mode', 'paused-mode');
                            var endcards = player.querySelectorAll('.ytp-ce-element');
                            for (var j = 0; j < endcards.length; j++) {
                                endcards[j].classList.add('ytp-ce-element-show');
                            }
                            var endscreen = player.querySelector('.html5-endscreen') || player.querySelector('.ytp-endscreen-content');
                            if (endscreen) {
                                endscreen.style.display = '';
                                endscreen.style.opacity = '1';
                            }
                        }
                    } else {
                        var targetEnd = (segDur > 0 && seg.end > segDur) ? segDur : seg.end;
                        if (player && typeof player.seekTo === 'function') {
                            try { player.seekTo(targetEnd, true); } catch(e) {}
                        }
                        v.currentTime = targetEnd;
                    }

                    showSkipToast(seg, 'skip');

                    // Notify C# backend
                    console.log('wb-action:sponsorblock-skip?videoid=' + encodeURIComponent(currentVideoId || '') +
                                '&category=' + encodeURIComponent(seg.category) +
                                '&saved=' + saved.toFixed(2));
                    return;
                } else if (action === 'manual' || (action === 'skip' && isManualSeekOverride)) {
                    // Manual skip or manual-seek-into-autoskip: Show Skip button so user can skip when ready
                    if (!unskippedMap[seg.uuid] && !skippedMap[seg.uuid] && !shownManualToasts[seg.uuid]) {
                        shownManualToasts[seg.uuid] = true;
                        showSkipToast(seg, 'manual');
                    }
                } else if (action === 'mute') {
                    if (!unmutedMap[seg.uuid]) {
                        insideMuteSegment = true;
                        if (!v.muted) {
                            previousMutedState = false;
                            v.muted = true;
                            wasMutedBySb = true;
                            showSkipToast(seg, 'mute');
                        }
                    }
                }
            } else if (ct >= seg.end) {
                delete manualSeekedSegments[seg.uuid];
            }
        }

        // If active toast is showing for a manual skip segment, dismiss it as soon as playback exits the highlighted area
        if (activeToast && activeToastSeg && activeToastMode === 'manual') {
            if (ct < (activeToastSeg.start - 0.5) || ct >= activeToastSeg.end) {
                dismissToast();
            }
        }

        // Unmute if playback exited all mute segments
        if (!insideMuteSegment && wasMutedBySb) {
            wasMutedBySb = false;
            v.muted = previousMutedState;
            dismissToast();
        }
    }

    function resetPlaybackStateForReplay() {
        isVideoEnded = false;
        skippedMap = {};
        unskippedMap = {};
        unmutedMap = {};
        shownManualToasts = {};
        manualSeekedSegments = {};
        dismissToast(true);
        var p = document.getElementById('movie_player') || document.querySelector('.html5-video-player');
        if (p) {
            p.classList.remove('ended-mode');
        }
    }

    function onVideoEnded() {
        isVideoEnded = true;
    }

    function onVideoPause() {
        if (activeToast && (activeToastMode === 'manual' || activeToastMode === 'mute')) {
            if (toastTimer) {
                clearTimeout(toastTimer);
                toastTimer = null;
            }
        }
    }

    function onVideoPlay() {
        if (isPlayerInAd()) {
            dismissToast(true);
            return;
        }

        var v = getVideo();
        var ct = (v && typeof v.currentTime === 'number') ? v.currentTime : 0;
        var p = document.getElementById('movie_player') || document.querySelector('.html5-video-player');
        var wasEnded = isVideoEnded || (p && p.classList.contains('ended-mode'));

        // If playback starts near beginning after video ended (or user clicked Replay), reset segments for fresh playback
        if (wasEnded && ct <= 2.0) {
            resetPlaybackStateForReplay();
        }

        if (isPlaybackReady(false)) {
            hasStartedPlayback = true;
        }
        if (activeToast && activeToastSeg && (activeToastMode === 'manual' || activeToastMode === 'mute')) {
            if (v) {
                var rate = (v.playbackRate > 0) ? v.playbackRate : 1.0;
                var remaining = Math.max(0.1, activeToastSeg.end - v.currentTime);
                if (remaining <= 0.1) {
                    dismissToast();
                } else {
                    scheduleToastDismiss(Math.round((remaining / rate) * 1000));
                }
            }
        }
        if (hasStartedPlayback) {
            checkPlayback();
        }
    }

    function onSeeked() {
        if (!isEnabled || currentSegments.length === 0) return;
        if (isPlayerInAd()) return;
        if (!hasStartedPlayback || !isPlaybackReady(true)) return;

        var v = getVideo();
        if (!v) return;
        var ct = v.currentTime;

        // If seeking to beginning after video ended, reset segments for replay
        if (isVideoEnded && ct <= 2.0) {
            resetPlaybackStateForReplay();
        }

        // Check if user manually sought directly into any highlighted segment
        for (var i = 0; i < currentSegments.length; i++) {
            var seg = currentSegments[i];
            if (ct >= (seg.start - 0.05) && ct < (seg.end - 0.2)) {
                manualSeekedSegments[seg.uuid] = true;
                delete skippedMap[seg.uuid];
                delete unskippedMap[seg.uuid];
                delete shownManualToasts[seg.uuid];

                var act = getAction(seg);
                if (act !== 'disabled') {
                    // Even if category is set to autoskip, manual seeking displays skip option
                    shownManualToasts[seg.uuid] = true;
                    showSkipToast(seg, 'manual');
                }
            }
        }
        checkPlayback();
    }

    function attachVideoListeners(v) {
        if (!v) v = getVideo();
        if (v && !v.__wbSbAttached) {
            v.__wbSbAttached = true;
            v.addEventListener('durationchange', renderPreviewBar);
            v.addEventListener('loadedmetadata', renderPreviewBar);
            v.addEventListener('canplay', renderPreviewBar);
            v.addEventListener('timeupdate', checkPlayback);
            v.addEventListener('play', onVideoPlay);
            v.addEventListener('playing', onVideoPlay);
            v.addEventListener('pause', onVideoPause);
            v.addEventListener('seeked', onSeeked);
            v.addEventListener('ended', onVideoEnded);
        }
    }

    // Video transition & SPA navigation handler
    function onVideoChanged() {
        var vid = extractVideoId();
        if (vid !== currentVideoId) {
            currentVideoId = vid;
            hasStartedPlayback = false;
            isVideoEnded = false;
            currentSegments = [];
            skippedMap = {};
            unskippedMap = {};
            unmutedMap = {};
            shownManualToasts = {};
            manualSeekedSegments = {};
            cachedVideo = null;
            wasMutedBySb = false;
            dismissToast(true);
            renderPreviewBar();
            if (vid) {
                requestSegments(vid);
            }
        }
        updatePlayerControlsButtons();
        attachVideoListeners();
    }

    // Throttled periodic check (350ms) with zero lag overhead
    setInterval(function() {
        var vid = extractVideoId();
        if (vid !== currentVideoId) {
            onVideoChanged();
        } else {
            var v = getVideo();
            if (v && !v.__wbSbAttached) {
                attachVideoListeners(v);
            }
            if (isPlayerInAd()) {
                if (wasMutedBySb) {
                    wasMutedBySb = false;
                    if (v) v.muted = previousMutedState;
                }
                dismissToast(true);
            } else {
                updatePlayerControlsButtons();
                if (currentSegments.length > 0 && hasStartedPlayback && isPlaybackReady(false)) {
                    if (!document.getElementById('previewbar')) {
                        renderPreviewBar();
                    }
                    checkPlayback();
                }
            }
        }
    }, 350);

    // YouTube navigation events
    window.addEventListener('yt-navigate-finish', onVideoChanged);
    window.addEventListener('yt-page-data-updated', onVideoChanged);
    window.addEventListener('popstate', onVideoChanged);

    // Expose hook for C# async segment delivery
    window.__wbSponsorBlockReceiveSegments = function(vid, segs) {
        if (vid === currentVideoId) {
            processSegments(vid, segs);
        }
    };

    window.__wbSponsorBlockUpdateConfig = function(newCfg) {
        config = newCfg || {};
        isEnabled = config.IsEnabled !== false;
        showPreviewBar = config.ShowPreviewBar !== false;
        showNotice = config.ShowNotice !== false;
        noticeDuration = (config.NoticeDurationSeconds || 4) * 1000;
        categoryActions = normalizeCategoryActions(config.CategoryActions || config.categoryActions);
        whitelistedVideos = config.WhitelistedVideoIds || config.whitelistedVideoIds || [];
        renderPreviewBar();
        updatePlayerControlsButtons();
        checkPlayback();
    };

    // Initial run
    onVideoChanged();
})(" + configJson + ");";

            return script;
        }

        /// <summary>
        /// Formats JavaScript to dispatch retrieved SponsorBlock segments to the client-side YouTube shield.
        /// </summary>
        public static string FormatReceiveSegmentsScript(string videoId, string segmentsJson)
        {
            return string.Format(
                "if (window.__wbSponsorBlockReceiveSegments) {{ window.__wbSponsorBlockReceiveSegments('{0}', {1}); }}",
                JsHelper.EscapeJs(videoId),
                string.IsNullOrEmpty(segmentsJson) ? "[]" : segmentsJson);
        }

        private static string EscapeJs(string s)
        {
            return JsHelper.EscapeJs(s);
        }
    }
}
