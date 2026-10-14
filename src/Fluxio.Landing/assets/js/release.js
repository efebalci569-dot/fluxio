// Shows the latest GitHub release (version, date, real file sizes, links, checksums).
// Nothing is offered for download unless a published release with files exists.
(function () {
  'use strict';

  var repo = (window.FLUXIO && window.FLUXIO.repo) || '';
  var $ = function (id) { return document.getElementById(id); };

  function show(id) {
    ['release-loading', 'release-none', 'release-error', 'release-ok'].forEach(function (name) {
      $(name).hidden = name !== id;
    });
  }

  function formatBytes(bytes) {
    var units = ['B', 'KB', 'MB', 'GB'];
    var value = bytes;
    var unit = 0;
    while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
    return value.toFixed(unit === 0 ? 0 : 1) + ' ' + units[unit];
  }

  function el(tag, className, text) {
    var node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  function describe(name) {
    var tr = document.documentElement.lang === 'tr';
    if (/setup.*\.exe$/i.test(name)) return tr ? 'Kurulum dosyası (önerilen)' : 'Installer (recommended)';
    if (/\.zip$/i.test(name)) return tr ? 'Taşınabilir sürüm (zip)' : 'Portable version (zip)';
    if (/sha256/i.test(name)) return tr ? 'SHA-256 sağlama toplamları' : 'SHA-256 checksums';
    return '';
  }

  function render(release) {
    var assets = (release.assets || []).filter(function (a) { return a.state === 'uploaded'; });
    if (assets.length === 0) { show('release-none'); return; }

    $('r-version').textContent = release.tag_name || release.name || '';
    var date = new Date(release.published_at || release.created_at);
    $('r-date').textContent = isNaN(date) ? '—' : date.toLocaleDateString(document.documentElement.lang || 'en', { year: 'numeric', month: 'long', day: 'numeric' });

    var list = $('r-assets');
    list.textContent = '';
    assets
      .sort(function (a, b) { return /setup/i.test(b.name) - /setup/i.test(a.name); })
      .forEach(function (asset) {
        var row = el('div', 'asset');
        var info = el('div');
        info.appendChild(el('b', '', asset.name));
        info.appendChild(el('small', '', [describe(asset.name), formatBytes(asset.size)].filter(Boolean).join(' · ')));
        var link = el('a', /sha256/i.test(asset.name) ? 'btn btn--secondary btn--small' : 'btn btn--primary btn--small', document.documentElement.lang === 'tr' ? 'İndir' : 'Download');
        link.href = asset.browser_download_url;
        link.rel = 'noopener';
        row.appendChild(info);
        row.appendChild(link);
        list.appendChild(row);
      });

    $('r-checksums-note').hidden = !assets.some(function (a) { return /sha256/i.test(a.name); });
    // Release notes are shown as plain text (never as HTML).
    $('r-notes').textContent = (release.body || '').trim() || '—';
    show('release-ok');
  }

  if (!repo) { show('release-none'); return; }

  fetch('https://api.github.com/repos/' + repo + '/releases/latest', { headers: { Accept: 'application/vnd.github+json' } })
    .then(function (response) {
      if (response.status === 404) { show('release-none'); return null; }
      if (!response.ok) { throw new Error('HTTP ' + response.status); }
      return response.json();
    })
    .then(function (release) { if (release) render(release); })
    .catch(function () { show('release-error'); });
})();
