// Points the "Download for Windows" buttons straight at the installer of the latest
// published GitHub release. Without a published installer they keep linking to
// download.html, which explains the situation honestly.
(function () {
  'use strict';

  var repo = (window.FLUXIO && window.FLUXIO.repo) || '';
  var buttons = document.querySelectorAll('[data-download-latest]');
  if (!repo || buttons.length === 0) return;

  function formatBytes(bytes) {
    var units = ['B', 'KB', 'MB', 'GB'];
    var value = bytes;
    var unit = 0;
    while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
    return value.toFixed(unit === 0 ? 0 : 1) + ' ' + units[unit];
  }

  fetch('https://api.github.com/repos/' + repo + '/releases/latest', { headers: { Accept: 'application/vnd.github+json' } })
    .then(function (response) { return response.ok ? response.json() : null; })
    .then(function (release) {
      if (!release || !release.assets) return;
      var setup = release.assets.filter(function (a) { return a.state === 'uploaded' && /setup.*\.exe$/i.test(a.name); })[0];
      if (!setup) return;

      buttons.forEach(function (button) {
        button.href = setup.browser_download_url;
        button.rel = 'noopener';
      });

      var info = document.querySelector('[data-release-info]');
      var text = document.querySelector('[data-release-text]');
      if (info && text) {
        text.textContent = (release.tag_name || '') + ' · Windows 10/11 · ' + formatBytes(setup.size);
        info.hidden = false;
      }
    })
    .catch(function () { /* keep the links to download.html */ });
})();
