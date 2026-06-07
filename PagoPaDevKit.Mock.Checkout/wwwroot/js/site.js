// ===== Sweep icone -> Bootstrap Italia (sprite it-*) =====
// Converte ogni <i class="bi bi-X"> nello sprite BI. Copre il markup statico Razor e quello
// generato a runtime (builder Crea) via MutationObserver. Le classi extra (text-*, fs-*) e lo
// stile inline vengono preservati. Icone non mappate -> fallback it-help-circle (o restano bi).
(function () {
  "use strict";
  var SPRITE = "/lib/bootstrap-italia/svg/sprites.svg#";
  var MAP = {
    "trash": "it-delete", "receipt": "it-note", "printer": "it-print",
    "exclamation-triangle": "it-warning-circle", "exclamation-triangle-fill": "it-warning-circle",
    "exclamation-octagon-fill": "it-error", "exclamation-circle": "it-warning-circle",
    "info-circle": "it-info-circle", "credit-card": "it-card", "credit-card-2-front": "it-card",
    "clipboard": "it-copy", "clipboard-check": "it-check-circle",
    "x-circle": "it-close-circle", "x-circle-fill": "it-close-circle",
    "check-circle": "it-check-circle", "check-circle-fill": "it-check-circle", "check2-circle": "it-check-circle",
    "check2": "it-check", "check": "it-check",
    "search": "it-search", "list-task": "it-list", "list": "it-list", "list-check": "it-list",
    "house-door": "it-pa", "chevron-down": "it-expand", "chevron-right": "it-chevron-right", "chevron-left": "it-chevron-left",
    "arrow-left": "it-arrow-left", "arrow-repeat": "it-refresh", "upc-scan": "it-card", "postage": "it-file-signed",
    "plus": "it-plus", "plus-lg": "it-plus", "plus-circle": "it-plus-circle",
    "play": "it-arrow-right-circle", "play-circle": "it-arrow-right-circle",
    "magic": "it-tool", "lock": "it-lock", "hdd-network": "it-software", "hand-index": "it-pin",
    "file-earmark-plus": "it-file", "file-text": "it-file", "eye": "it-password-visible",
    "diagram-2": "it-piattaforme", "diagram-3": "it-piattaforme", "grid-3x3-gap": "it-piattaforme",
    "clock-history": "it-clock", "hourglass": "it-clock", "bank": "it-pa", "bank2": "it-pa", "building": "it-pa",
    "sliders": "it-settings", "shuffle": "it-exchange-circle", "send": "it-mail",
    "person": "it-user", "person-vcard": "it-user", "megaphone": "it-horn", "lightning-charge": "it-tool",
    "inbox": "it-inbox", "funnel": "it-funnel", "folder2-open": "it-folder", "database": "it-software",
    "cone-striped": "it-warning", "cash-coin": "it-card", "broadcast": "it-rss", "broadcast-pin": "it-rss",
    "braces": "it-code-circle", "box-arrow-up-right": "it-external-link", "journal-text": "it-file",
    "speedometer2": "it-chart-line", "tools": "it-tool", "tool": "it-tool",
    "1-circle": "it-check-circle", "2-circle": "it-check-circle", "3-circle": "it-check-circle", "4-circle": "it-check-circle",
    "star-full": "it-star-full"
  };
  var SVGNS = "http://www.w3.org/2000/svg";

  function swap(i) {
    var biName = null, extra = [];
    Array.prototype.forEach.call(i.classList, function (c) {
      if (c === "bi") return;
      if (/^bi-/.test(c)) { if (!biName) biName = c.replace(/^bi-/, ""); }
      else extra.push(c);
    });
    if (!biName) return;
    var it = MAP[biName] || "it-help-circle";
    var svg = document.createElementNS(SVGNS, "svg");
    svg.setAttribute("class", ("ppa-ic " + extra.join(" ")).trim());
    if (i.getAttribute("style")) svg.setAttribute("style", i.getAttribute("style"));
    if (i.getAttribute("title")) {
      svg.setAttribute("aria-label", i.getAttribute("title"));
      var t = document.createElementNS(SVGNS, "title"); t.textContent = i.getAttribute("title"); svg.appendChild(t);
    } else { svg.setAttribute("aria-hidden", "true"); }
    var use = document.createElementNS(SVGNS, "use");
    use.setAttribute("href", SPRITE + it);
    svg.appendChild(use);
    if (i.parentNode) i.parentNode.replaceChild(svg, i);
  }

  var obs = null;
  function swapAll() {
    var list = document.querySelectorAll("i.bi");
    for (var k = 0; k < list.length; k++) swap(list[k]);
  }
  function start() {
    swapAll();
    obs = new MutationObserver(function () {
      if (obs) obs.disconnect();
      swapAll();
      if (obs) obs.observe(document.body, { childList: true, subtree: true });
    });
    obs.observe(document.body, { childList: true, subtree: true });
  }
  if (document.readyState !== "loading") start();
  else document.addEventListener("DOMContentLoaded", start);
})();
