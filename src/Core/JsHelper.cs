using System;
using System.Text;

namespace wb.Core
{
    /// <summary>
    /// Centralized JavaScript and JSON string formatting and client-side observer scriptlets.
    /// Provides escaping, message delivery primitives and runtime DOM event observers.
    /// </summary>
    public static class JsHelper
    {
        private static readonly char[] JsCharsToEscape = new char[] { '\\', '\'', '"', '\r', '\n' };

        /// <summary>
        /// Escapes a string for safe embedding inside a single- or double-quoted JavaScript string literal.
        /// Zero allocation for strings that do not require escaping. Single pass when escaping is needed.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static string EscapeJs(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.IndexOfAny(JsCharsToEscape) < 0) return s;

            StringBuilder sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\'': sb.Append("\\'"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': break;
                    case '\n': sb.Append("\\n"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Escapes a string according to JSON string escaping rules for use in CDP DevTools JSON messages.
        /// </summary>
        public static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;
            StringBuilder sb = new StringBuilder(str.Length + 64);
            foreach (char c in str)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 32)
                        {
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Scriptlet that detects real-time audio playback across HTML5 audio/video elements,
        /// Shadow DOM trees, dynamic Audio objects, and Web Audio API (AudioContext) instances,
        /// and reports changes to C# host via PostMessage and console action protocols.
        /// </summary>
        public const string AudioObserverScript = @"(function() {
    try {
        var loc = '';
        try { loc = (window && window.location) ? window.location.href : ''; } catch(e) {}
        if (loc.indexOf('wb:') === 0 || loc.indexOf('chrome:') === 0 || loc.indexOf('about:') === 0) return;

        if (window.__wbAudioObserverActive) return;
        window.__wbAudioObserverActive = true;

        var frameId = window.__wbAudioFrameId || (window.__wbAudioFrameId = 'f_' + Math.random().toString(36).substring(2, 9) + '_' + Date.now());
        var isPlaying = false;
        var activeMedia = [];
        var activeContexts = [];
        var trackedShadowRoots = [];
        var rawLog = (typeof console !== 'undefined' && console && console.log) ? console.log.bind(console) : null;

        function sendAction(msg) {
            try {
                if (window.CefSharp && typeof window.CefSharp.PostMessage === 'function') {
                    window.CefSharp.PostMessage(msg);
                }
            } catch(e) {}
            try {
                if (rawLog) {
                    rawLog(msg);
                } else if (typeof console !== 'undefined' && console && console.log) {
                    console.log(msg);
                }
            } catch(e) {}
        }

        function notifyState(playState) {
            if (isPlaying === playState) return;
            isPlaying = playState;
            var audioMsg = 'wb-action:audio-state?playing=' + (playState ? '1' : '0') + '&source=' + frameId;
            sendAction(audioMsg);
        }

        function isMediaPlaying(m) {
            try {
                if (!m) return false;
                if (m.paused || m.ended) return false;
                if (m.muted) return false;
                if (m.volume !== undefined && m.volume <= 0) return false;
                if (m.playbackRate !== undefined && m.playbackRate <= 0) return false;
                if (m.readyState >= 1 || m.currentTime > 0 || m.duration === Infinity || m.srcObject != null || (m.buffered && m.buffered.length > 0)) {
                    return true;
                }
                if (m.currentSrc || m.src) {
                    return true;
                }
            } catch(e) {}
            return false;
        }

        function trackMedia(el) {
            if (!el || activeMedia.indexOf(el) !== -1) return;
            activeMedia.push(el);
            var evts = ['play', 'playing', 'pause', 'ended', 'volumechange', 'emptied', 'loadedmetadata', 'ratechange', 'suspend'];
            for (var k = 0; k < evts.length; k++) {
                try {
                    el.addEventListener(evts[k], checkMedia, true);
                } catch(e) {}
            }
        }

        function trackContext(ctx) {
            if (!ctx || activeContexts.indexOf(ctx) !== -1) return;
            activeContexts.push(ctx);
            try {
                ctx.addEventListener('statechange', function() {
                    setTimeout(checkMedia, 20);
                });
            } catch(e) {}
        }

        function trackShadowRoot(sr) {
            if (!sr || trackedShadowRoots.indexOf(sr) !== -1) return;
            trackedShadowRoots.push(sr);
            try {
                var medias = sr.querySelectorAll('audio, video');
                for (var i = 0; i < medias.length; i++) {
                    trackMedia(medias[i]);
                }
            } catch(e) {}
            try {
                if (typeof MutationObserver !== 'undefined') {
                    var obs = new MutationObserver(function(mutations) {
                        for (var m = 0; m < mutations.length; m++) {
                            var added = mutations[m].addedNodes;
                            for (var a = 0; a < added.length; a++) {
                                var n = added[a];
                                if (n && n.nodeType === 1) {
                                    if (n.tagName === 'AUDIO' || n.tagName === 'VIDEO') {
                                        trackMedia(n);
                                    } else if (n.querySelectorAll) {
                                        var nested = n.querySelectorAll('audio, video');
                                        for (var k = 0; k < nested.length; k++) {
                                            trackMedia(nested[k]);
                                        }
                                    }
                                }
                            }
                        }
                        checkMedia();
                    });
                    obs.observe(sr, { childList: true, subtree: true });
                }
            } catch(e) {}
        }

        function scanShadowRoots(root) {
            if (!root || !root.querySelectorAll) return;
            try {
                var hosts = root.querySelectorAll('*');
                for (var i = 0; i < hosts.length; i++) {
                    var sr = hosts[i].shadowRoot;
                    if (sr) {
                        trackShadowRoot(sr);
                        scanShadowRoots(sr);
                    }
                }
            } catch(e) {}
        }

        function checkMedia() {
            for (var i = 0; i < activeMedia.length; i++) {
                if (isMediaPlaying(activeMedia[i])) {
                    notifyState(true);
                    return;
                }
            }

            try {
                if (typeof document !== 'undefined' && document.querySelectorAll) {
                    var docMedia = document.querySelectorAll('audio, video');
                    for (var j = 0; j < docMedia.length; j++) {
                        var el = docMedia[j];
                        trackMedia(el);
                        if (isMediaPlaying(el)) {
                            notifyState(true);
                            return;
                        }
                    }
                }
            } catch(e) {}

            for (var s = 0; s < trackedShadowRoots.length; s++) {
                try {
                    var srMedia = trackedShadowRoots[s].querySelectorAll('audio, video');
                    for (var sm = 0; sm < srMedia.length; sm++) {
                        var sel = srMedia[sm];
                        trackMedia(sel);
                        if (isMediaPlaying(sel)) {
                            notifyState(true);
                            return;
                        }
                    }
                } catch(e) {}
            }

            for (var k = 0; k < activeContexts.length; k++) {
                try {
                    if (activeContexts[k].state === 'running') {
                        notifyState(true);
                        return;
                    }
                } catch(e) {}
            }

            notifyState(false);
        }

        window.__wbCheckAudio = function(force) {
            if (force) {
                isPlaying = null;
            }
            checkMedia();
        };

        function hookConstructor(ctorName, onCreated) {
            try {
                if (typeof window[ctorName] === 'function') {
                    var Orig = window[ctorName];
                    var Proxied = new Proxy(Orig, {
                        construct: function(target, args, newTarget) {
                            var inst = Reflect.construct(target, args, newTarget);
                            try { onCreated(inst); } catch(e) {}
                            return inst;
                        }
                    });
                    try {
                        Object.defineProperty(window, ctorName, {
                            value: Proxied,
                            writable: true,
                            configurable: true
                        });
                    } catch(e) {
                        window[ctorName] = Proxied;
                    }
                }
            } catch(e) {}
        }

        hookConstructor('AudioContext', trackContext);
        hookConstructor('webkitAudioContext', trackContext);
        hookConstructor('Audio', trackMedia);

        try {
            var acProto = (window.AudioContext && window.AudioContext.prototype) || (window.webkitAudioContext && window.webkitAudioContext.prototype);
            if (acProto) {
                if (acProto.resume) {
                    var origResume = acProto.resume;
                    acProto.resume = function() {
                        trackContext(this);
                        setTimeout(checkMedia, 50);
                        return origResume.apply(this, arguments);
                    };
                }
                if (acProto.suspend) {
                    var origSuspend = acProto.suspend;
                    acProto.suspend = function() {
                        setTimeout(checkMedia, 50);
                        return origSuspend.apply(this, arguments);
                    };
                }
                if (acProto.close) {
                    var origClose = acProto.close;
                    acProto.close = function() {
                        setTimeout(checkMedia, 50);
                        return origClose.apply(this, arguments);
                    };
                }
            }
        } catch(e) {}

        try {
            if (window.HTMLMediaElement && window.HTMLMediaElement.prototype) {
                var origPlay = window.HTMLMediaElement.prototype.play;
                window.HTMLMediaElement.prototype.play = function() {
                    trackMedia(this);
                    setTimeout(checkMedia, 50);
                    return origPlay.apply(this, arguments);
                };
                var origPause = window.HTMLMediaElement.prototype.pause;
                window.HTMLMediaElement.prototype.pause = function() {
                    setTimeout(checkMedia, 50);
                    return origPause.apply(this, arguments);
                };
            }
        } catch(e) {}

        try {
            if (typeof Document !== 'undefined' && Document.prototype && Document.prototype.createElement) {
                var origCreateElement = Document.prototype.createElement;
                Document.prototype.createElement = function(tagName) {
                    var el = origCreateElement.apply(this, arguments);
                    try {
                        if (el && typeof tagName === 'string') {
                            var lower = tagName.toLowerCase();
                            if (lower === 'audio' || lower === 'video') {
                                trackMedia(el);
                            }
                        }
                    } catch(e) {}
                    return el;
                };
            }
        } catch(e) {}

        try {
            if (typeof Element !== 'undefined' && Element.prototype && Element.prototype.attachShadow) {
                var origAttachShadow = Element.prototype.attachShadow;
                Element.prototype.attachShadow = function(init) {
                    var shadow = origAttachShadow.apply(this, arguments);
                    try {
                        trackShadowRoot(shadow);
                    } catch(e) {}
                    return shadow;
                };
            }
        } catch(e) {}

        var evts = ['play', 'playing', 'pause', 'ended', 'volumechange', 'emptied', 'ratechange'];
        for (var j = 0; j < evts.length; j++) {
            window.addEventListener(evts[j], function(e) {
                if (e && e.target && (e.target.tagName === 'AUDIO' || e.target.tagName === 'VIDEO')) {
                    trackMedia(e.target);
                    checkMedia();
                }
            }, true);
        }

        try {
            if (typeof MutationObserver !== 'undefined' && typeof document !== 'undefined') {
                var docObserver = new MutationObserver(function(mutations) {
                    var found = false;
                    for (var i = 0; i < mutations.length; i++) {
                        var added = mutations[i].addedNodes;
                        for (var a = 0; a < added.length; a++) {
                            var node = added[a];
                            if (node && node.nodeType === 1) {
                                if (node.tagName === 'AUDIO' || node.tagName === 'VIDEO') {
                                    trackMedia(node);
                                    found = true;
                                } else if (node.querySelectorAll) {
                                    var nested = node.querySelectorAll('audio, video');
                                    for (var n = 0; n < nested.length; n++) {
                                        trackMedia(nested[n]);
                                        found = true;
                                    }
                                }
                            }
                        }
                    }
                    if (found) checkMedia();
                });
                var targetNode = document.documentElement || document.body || document;
                if (targetNode) {
                    docObserver.observe(targetNode, { childList: true, subtree: true });
                }
            }
        } catch(e) {}

        window.addEventListener('popstate', function() {
            setTimeout(checkMedia, 300);
        });
        window.addEventListener('hashchange', function() {
            setTimeout(checkMedia, 300);
        });
        window.addEventListener('pagehide', function() {
            if (isPlaying) {
                notifyState(false);
            }
        });
        window.addEventListener('beforeunload', function() {
            if (isPlaying) {
                notifyState(false);
            }
        });

        setInterval(function() {
            checkMedia();
        }, 750);

        checkMedia();
    } catch(err) {}
})();";

        /// <summary>
        /// Scriptlet that detects active camera or microphone media capture streams (WebRTC / getUserMedia)
        /// and reports state to C# host for toolbar badge rendering.
        /// </summary>
        public const string MediaCaptureObserverScript = @"(function() {
    try {
        if (window.__wbMediaCaptureObserverActive) return;
        window.__wbMediaCaptureObserverActive = true;

        var activeTracks = [];
        var lastActive = false;
        var lastType = '';

        function sendAction(msg) {
            var sent = false;
            try {
                if (window.CefSharp && typeof window.CefSharp.PostMessage === 'function') {
                    window.CefSharp.PostMessage(msg);
                    sent = true;
                }
            } catch(e) {}
            if (!sent) {
                try { console.log(msg); } catch(e) {}
                try {
                    var iframe = document.createElement('iframe');
                    iframe.style.display = 'none';
                    iframe.src = msg;
                    (document.body || document.documentElement).appendChild(iframe);
                    setTimeout(function() { try { iframe.remove(); } catch(e){} }, 500);
                } catch(e) {}
            }
        }

        function notifyCaptureState() {
            var liveTracks = [];
            for (var i = 0; i < activeTracks.length; i++) {
                var t = activeTracks[i];
                if (t && t.readyState === 'live') {
                    liveTracks.push(t);
                }
            }
            activeTracks = liveTracks;

            var hasCam = false;
            var hasMic = false;
            for (var j = 0; j < liveTracks.length; j++) {
                if (liveTracks[j].kind === 'video') hasCam = true;
                if (liveTracks[j].kind === 'audio') hasMic = true;
            }

            var isActive = (hasCam || hasMic);
            var capType = (hasCam && hasMic) ? 'all' : (hasCam ? 'cam' : (hasMic ? 'mic' : ''));

            if (lastActive === isActive && lastType === capType) {
                return;
            }
            lastActive = isActive;
            lastType = capType;

            var msg = 'wb-action:media-capture-state?active=' + (isActive ? '1' : '0') + '&type=' + capType;
            sendAction(msg);
        }

        if (navigator.mediaDevices && navigator.mediaDevices.getUserMedia) {
            var origGUM = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
            navigator.mediaDevices.getUserMedia = function(constraints) {
                return origGUM(constraints).then(function(stream) {
                    try {
                        var tracks = stream.getTracks();
                        for (var i = 0; i < tracks.length; i++) {
                            var tr = tracks[i];
                            activeTracks.push(tr);
                            tr.addEventListener('ended', notifyCaptureState);
                            tr.addEventListener('mute', notifyCaptureState);
                            tr.addEventListener('unmute', notifyCaptureState);
                        }
                        notifyCaptureState();
                    } catch(e) {}
                    return stream;
                });
            };
        }

        if (navigator.mediaDevices && navigator.mediaDevices.getDisplayMedia) {
            var origGDM = navigator.mediaDevices.getDisplayMedia.bind(navigator.mediaDevices);
            navigator.mediaDevices.getDisplayMedia = function(constraints) {
                return origGDM(constraints).then(function(stream) {
                    try {
                        var tracks = stream.getTracks();
                        for (var i = 0; i < tracks.length; i++) {
                            var tr = tracks[i];
                            activeTracks.push(tr);
                            tr.addEventListener('ended', notifyCaptureState);
                        }
                        notifyCaptureState();
                    } catch(e) {}
                    return stream;
                });
            };
        }

        setInterval(function() {
            if (activeTracks.length > 0 || lastActive) {
                notifyCaptureState();
            }
        }, 2000);
    } catch(err) {}
})();";
    }
}
