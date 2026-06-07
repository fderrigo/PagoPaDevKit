// PagoPaDevKit console — JS vanilla minimo. Bootstrap JS gestisce toast/modal/offcanvas.
(function () {
  "use strict";

  // ---- Copy to clipboard (elementi con [data-copy]) ----
  document.addEventListener("click", function (e) {
    var el = e.target.closest("[data-copy]");
    if (!el) return;
    var text = el.getAttribute("data-copy");
    navigator.clipboard.writeText(text).then(function () {
      showToast("Copiato negli appunti: " + text, "success");
      var old = el.innerHTML;
      el.innerHTML = '<i class="bi bi-check2"></i>';
      setTimeout(function () { el.innerHTML = old; }, 1200);
    });
  });

  // ---- Loading state su bottoni che chiamano API ----
  document.addEventListener("submit", function (e) {
    var form = e.target;
    if (!form.matches("form[data-loading]")) return;
    var btn = form.querySelector('button[type="submit"]');
    if (btn) {
      btn.disabled = true;
      btn.dataset.old = btn.innerHTML;
      btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>' + (btn.dataset.loadingText || "Attendere...");
    }
  });

  // ---- Toast helper ----
  window.showToast = function (msg, kind) {
    var area = document.getElementById("toastArea");
    if (!area) return;
    var bg = kind === "danger" ? "text-bg-danger" : kind === "warning" ? "text-bg-warning" : "text-bg-success";
    var wrap = document.createElement("div");
    wrap.className = "toast align-items-center border-0 " + bg;
    wrap.setAttribute("role", "alert");
    wrap.innerHTML = '<div class="d-flex"><div class="toast-body">' + msg + '</div>' +
      '<button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button></div>';
    area.appendChild(wrap);
    var t = new bootstrap.Toast(wrap, { delay: 3500 });
    t.show();
    wrap.addEventListener("hidden.bs.toast", function () { wrap.remove(); });
  };

  // Mostra i toast generati server-side (TempData) al load
  document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll("#toastArea .toast").forEach(function (el) {
      new bootstrap.Toast(el, { delay: 4000 }).show();
    });
  });

  // ---- Euro -> anteprima centesimi nel form crea dovuto ----
  document.addEventListener("input", function (e) {
    if (!e.target.matches("[data-euro-preview]")) return;
    var out = document.getElementById(e.target.getAttribute("data-euro-preview"));
    if (!out) return;
    var v = parseFloat((e.target.value || "").replace(",", "."));
    out.textContent = isNaN(v) ? "—" : Math.round(v * 100) + " centesimi";
  });

  // ---- Health polling (Infrastruttura / Dashboard) ----
  window.startHealthPolling = function (url, intervalMs) {
    var render = function (data) {
      (data.services || []).forEach(function (s) {
        var cell = document.querySelector('[data-health="' + s.name + '"]');
        if (!cell) return;
        var ok = s.healthy;
        cell.querySelector("[data-health-dot]").className =
          "badge rounded-pill " + (ok ? "text-bg-success" : "text-bg-danger");
        cell.querySelector("[data-health-dot]").textContent = ok ? "online" : "offline";
        var lat = cell.querySelector("[data-health-latency]");
        if (lat) lat.textContent = s.latencyMs != null ? s.latencyMs + " ms" : "—";
      });
    };
    var tick = function () {
      fetch(url).then(function (r) { return r.json(); }).then(render).catch(function () {});
    };
    tick();
    if (intervalMs) setInterval(tick, intervalMs);
  };

  // ---- Scenari di test: esecuzione via fetch ----
  window.runScenario = function (key, btn) {
    var card = document.querySelector('[data-scenario="' + key + '"]');
    if (!card) return;
    var statusEl = card.querySelector("[data-scenario-status]");
    var stepsEl = card.querySelector("[data-scenario-steps]");
    var durEl = card.querySelector("[data-scenario-duration]");
    if (btn) { btn.disabled = true; }
    statusEl.innerHTML = '<span class="spinner-border spinner-border-sm"></span> in corso...';
    stepsEl.innerHTML = "";
    return fetch("/infrastruttura/scenari/run?key=" + encodeURIComponent(key), { method: "POST" })
      .then(function (r) { return r.json(); })
      .then(function (res) {
        statusEl.innerHTML = res.ok
          ? '<span class="badge text-bg-success"><i class="bi bi-check-circle"></i> PASSATO</span>'
          : '<span class="badge text-bg-danger"><i class="bi bi-x-circle"></i> FALLITO</span>';
        if (durEl) durEl.textContent = res.durationMs + " ms";
        (res.steps || []).forEach(function (s, i) {
          var icon = s.ok ? '<i class="bi bi-check-circle-fill text-success"></i>'
                          : '<i class="bi bi-x-circle-fill text-danger"></i>';
          var det = (s.request || s.response)
            ? '<div class="collapse" id="' + key + '-s' + i + '"><pre class="ppa-json bg-body-tertiary p-2 mt-1 rounded">'
              + escapeHtml((s.request || "") + (s.response ? "\n--- risposta ---\n" + s.response : "")) + '</pre></div>'
              + '<a class="small" data-bs-toggle="collapse" href="#' + key + '-s' + i + '">dettagli</a>'
            : "";
          stepsEl.insertAdjacentHTML("beforeend",
            '<div class="ppa-scenario-step d-flex flex-column border-bottom py-2">' +
            '<div class="d-flex align-items-center gap-2">' + icon +
            '<span class="fw-medium">' + escapeHtml(s.name) + '</span>' +
            '<span class="text-secondary small ms-auto">' + (s.detail ? escapeHtml(s.detail) : "") + '</span></div>' +
            det + '</div>');
        });
      })
      .catch(function (err) {
        statusEl.innerHTML = '<span class="badge text-bg-danger">errore: ' + escapeHtml(String(err)) + '</span>';
      })
      .finally(function () { if (btn) btn.disabled = false; });
  };

  window.runAllScenarios = function () {
    var cards = Array.prototype.slice.call(document.querySelectorAll("[data-scenario]"));
    var chain = Promise.resolve();
    cards.forEach(function (c) {
      var key = c.getAttribute("data-scenario");
      chain = chain.then(function () { return window.runScenario(key, null); });
    });
    return chain;
  };

  function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }
})();

// ===== Sidebar collassabile (desktop) — stato persistito su <html> =====
window.ppaToggleNav = function () {
  var h = document.documentElement;
  h.classList.toggle("ppa-collapsed");
  try { localStorage.setItem("ppaNav", h.classList.contains("ppa-collapsed") ? "collapsed" : "open"); } catch (e) {}
};

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
    "search": "it-search", "list-task": "it-list", "list": "it-list", "list-check": "it-list", "list-ol": "it-list",
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
