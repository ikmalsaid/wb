using System;
using CefSharp;

namespace wb.Browser
{
    /// <summary>
    /// Core browser viewport and scroll-anchor manager.
    /// Preserves user reading position across window maximize/restore/resize events
    /// and prevents reel/carousel skipping on sites like YouTube Shorts with 0% CPU overhead.
    /// </summary>
    public static class ViewportScrollManager
    {
        private static readonly string ScrollAnchorScript = @"(function() {
    if (window.__wb_scroll_anchor_active) return;
    window.__wb_scroll_anchor_active = true;

    // 1. Fullscreen Carousel & Reel Anchor Engine (e.g. YouTube Shorts)
    function patchShortsProto(proto) {
        if (!proto || proto.__wb_resize_patched) return;
        proto.__wb_resize_patched = true;

        var origOnResize = proto.onResize;
        proto.onResize = function() {
            this.shouldUpdateItemHeight = true;
            if (typeof this.updateItemHeight === 'function') {
                try { this.updateItemHeight(); } catch(e) {}
            }
            var res = origOnResize ? origOnResize.apply(this, arguments) : undefined;
            if (this.shortsContainer && typeof this.index === 'number' && this.itemHeight) {
                try {
                    this.shortsContainer.scrollTop = this.index * this.itemHeight + 16;
                } catch(e) {}
            }
            return res;
        };
    }

    if (window.customElements) {
        var cls = customElements.get('ytd-shorts');
        if (cls && cls.prototype) {
            patchShortsProto(cls.prototype);
        }
        var origDefine = customElements.define;
        customElements.define = function(name, constructor) {
            if (name === 'ytd-shorts' && constructor && constructor.prototype) {
                patchShortsProto(constructor.prototype);
            }
            return origDefine.apply(this, arguments);
        };
    }

    // 2. Universal Viewport Scroll Anchoring on Window Resize
    var _activeAnchor = null;
    var _isResizing = false;
    var _resizeTimer = null;

    function recordCurrentAnchor() {
        if (_isResizing) return;
        if (window.scrollY === 0) {
            _activeAnchor = null;
            return;
        }
        var checkY = Math.min(120, Math.max(30, window.innerHeight * 0.15));
        var el = document.elementFromPoint(window.innerWidth / 2, checkY);
        while (el && el !== document.body && el !== document.documentElement) {
            var tag = el.tagName.toLowerCase();
            if (tag === 'p' || tag === 'h1' || tag === 'h2' || tag === 'h3' || tag === 'h4' || 
                tag === 'div' || tag === 'section' || tag === 'article' || tag === 'li') {
                if (el.offsetHeight > 15) break;
            }
            el = el.parentElement;
        }
        if (el && el !== document.body && el !== document.documentElement) {
            _activeAnchor = {
                el: el,
                top: el.getBoundingClientRect().top
            };
        }
    }

    var _scrollTimer = null;
    window.addEventListener('scroll', function() {
        if (_isResizing) return;
        clearTimeout(_scrollTimer);
        _scrollTimer = setTimeout(recordCurrentAnchor, 150);
    }, { passive: true });

    function applyAnchor() {
        if (_activeAnchor && _activeAnchor.el && _activeAnchor.el.isConnected) {
            var newTop = _activeAnchor.el.getBoundingClientRect().top;
            var delta = newTop - _activeAnchor.top;
            if (Math.abs(delta) > 1 && Math.abs(delta) < window.innerHeight * 5) {
                window.scrollBy(0, delta);
            }
        }
    }

    function onResize() {
        // Handle YouTube Shorts / Full-height carousel containers first
        var ytdShorts = document.querySelector('ytd-shorts');
        if (ytdShorts) {
            ytdShorts.shouldUpdateItemHeight = true;
            if (typeof ytdShorts.updateItemHeight === 'function') {
                try { ytdShorts.updateItemHeight(); } catch(e) {}
            }
            if (ytdShorts.shortsContainer && typeof ytdShorts.index === 'number' && ytdShorts.itemHeight) {
                try {
                    ytdShorts.shortsContainer.scrollTop = ytdShorts.index * ytdShorts.itemHeight + 16;
                } catch(e) {}
            }
            var active = document.querySelector('ytd-reel-video-renderer[is-active]');
            if (active && typeof active.scrollIntoView === 'function') {
                try { active.scrollIntoView({ behavior: 'instant', block: 'nearest' }); } catch(e) {}
            }
            return;
        }

        // For general responsive pages:
        if (window.scrollY === 0 && !_activeAnchor) return;

        _isResizing = true;
        applyAnchor();
        requestAnimationFrame(applyAnchor);

        clearTimeout(_resizeTimer);
        _resizeTimer = setTimeout(function() {
            applyAnchor();
            _isResizing = false;
            recordCurrentAnchor();
        }, 200);
    }

    window.addEventListener('resize', onResize, { passive: true });
})();";

        /// <summary>
        /// Injects the core viewport scroll-anchoring engine into the specified frame.
        /// Operates universally across all websites and tabs independently of ad blocking.
        /// </summary>
        public static void InjectViewportScrollAnchor(IFrame frame, string url)
        {
            if (frame == null || !frame.IsValid) return;
            if (string.IsNullOrEmpty(url)) return;
            if (url.StartsWith("wb://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                frame.ExecuteJavaScriptAsync(ScrollAnchorScript);
            }
            catch
            {
            }
        }
    }
}
