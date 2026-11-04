// The game's own page tweaks (ticket 57). CrazyGames serves only Build/ and StreamingAssets/, so the page's CSS cannot live in a
// template: the game adds it itself at startup, on every device. Also the audio resume (touch devices suspend the AudioContext when
// the app goes to the background) and the page's safe-area insets (notches, rounded corners).
mergeInto(LibraryManager.library, {
  // Runs when the module loads, before the engine creates its AudioContext: remembers every context so it can be resumed later.
  $SdfPageBoot: function () {
    if (typeof window === 'undefined' || window.SdfPage) {
      return;
    }
    var contexts = [];
    var page = (window.SdfPage = {
      contexts: contexts,
      styled: false,
      probe: null,
      resume: function () {
        var list = contexts.slice();
        try {
          if (typeof WEBAudio !== 'undefined' && WEBAudio.audioContext) {
            list.push(WEBAudio.audioContext);
          }
        } catch (e) {}
        for (var i = 0; i < list.length; i++) {
          var c = list[i];
          if (c && c.state !== 'running' && c.state !== 'closed' && c.resume) {
            try {
              var p = c.resume();
              if (p && p.catch) {
                p.catch(function () {});
              }
            } catch (e) {}
          }
        }
      },
      style: function () {
        if (page.styled || !document.head) {
          return;
        }
        page.styled = true;
        var css =
          'html,body{margin:0;padding:0;overscroll-behavior:none;' +
          '-webkit-user-select:none;user-select:none;-webkit-touch-callout:none;-webkit-tap-highlight-color:transparent;' +
          '-webkit-text-size-adjust:none;}' +
          'canvas{touch-action:none;-webkit-user-select:none;user-select:none;-webkit-touch-callout:none;outline:none;' +
          '-webkit-tap-highlight-color:transparent;}';
        var el = document.createElement('style');
        el.id = 'sdf-page-style';
        el.textContent = css;
        document.head.appendChild(el);
        // Lets the canvas reach under a notch, so the safe-area insets below are real
        var meta = document.querySelector('meta[name=viewport]');
        if (meta && !/viewport-fit/.test(meta.content)) {
          meta.content = meta.content + ', viewport-fit=cover';
        }
        // A long press on the canvas must not open the browser's menu
        document.addEventListener('contextmenu', function (e) {
          if (e.target && e.target.tagName === 'CANVAS') {
            e.preventDefault();
          }
        });
      },
      inset: function (side) {
        if (!document.body) {
          return 0;
        }
        if (!page.probe) {
          var d = document.createElement('div');
          d.style.cssText =
            'position:fixed;left:0;top:0;width:0;height:0;visibility:hidden;pointer-events:none;' +
            'padding-top:env(safe-area-inset-top,0px);padding-right:env(safe-area-inset-right,0px);' +
            'padding-bottom:env(safe-area-inset-bottom,0px);padding-left:env(safe-area-inset-left,0px);';
          document.body.appendChild(d);
          page.probe = d;
        }
        var s = getComputedStyle(page.probe);
        var v = [s.paddingLeft, s.paddingTop, s.paddingRight, s.paddingBottom][side];
        return parseFloat(v) || 0;
      }
    });
    // A new AudioContext is remembered
    ['AudioContext', 'webkitAudioContext'].forEach(function (name) {
      var Original = window[name];
      if (!Original || Original.sdfWrapped) {
        return;
      }
      var Wrapped = function () {
        var context = new (Function.prototype.bind.apply(Original, [null].concat(Array.prototype.slice.call(arguments))))();
        contexts.push(context);
        return context;
      };
      Wrapped.prototype = Original.prototype;
      Wrapped.sdfWrapped = true;
      window[name] = Wrapped;
    });
    // The first touch, a click or a key, and coming back to the page, resume it
    ['touchstart', 'touchend', 'pointerup', 'click', 'keydown'].forEach(function (type) {
      window.addEventListener(type, page.resume, true);
    });
    document.addEventListener('visibilitychange', function () {
      if (!document.hidden) {
        page.resume();
      }
    });
    window.addEventListener('pageshow', page.resume);
    window.addEventListener('focus', page.resume);
  },
  $SdfPageBoot__postset: 'SdfPageBoot();',

  // Adds the CSS (no text selection, no long-press menu, touch-action none on the canvas) and the hooks; safe to call twice
  SdfPageInit__deps: ['$SdfPageBoot'],
  SdfPageInit: function () {
    SdfPageBoot();
    if (window.SdfPage) {
      window.SdfPage.style();
    }
  },

  // One inset of the page's safe area in CSS pixels: 0 left, 1 top, 2 right, 3 bottom
  SdfPageSafeInset__deps: ['$SdfPageBoot'],
  SdfPageSafeInset: function (side) {
    return window.SdfPage ? window.SdfPage.inset(side) : 0;
  },

  // The size of the page's viewport in CSS pixels: 0 width, 1 height
  SdfPageViewport__deps: ['$SdfPageBoot'],
  SdfPageViewport: function (axis) {
    return axis === 0 ? window.innerWidth : window.innerHeight;
  },

  // Resumes the audio now (a gesture in the game's own code, for instance)
  SdfPageResumeAudio__deps: ['$SdfPageBoot'],
  SdfPageResumeAudio: function () {
    if (window.SdfPage) {
      window.SdfPage.resume();
    }
  }
});
