/* Vighnesh Payroll Systems Continuous Local Music Player v10.6.3
   - User chooses a local audio file once.
   - Audio file is stored locally in browser IndexedDB (never uploaded).
   - Playback position, volume and play/pause intent persist across Vighnesh Payroll Systems pages.
   - On a normal multi-page website, the browser destroys the audio element during navigation,
     so this script resumes from the saved position immediately on the next page.
*/
(function () {
  'use strict';

  var KEY = 'vighnesh-payroll-systems_music_v1063';
  var DB_NAME = 'Vighnesh Payroll SystemsLocalMusic';
  var DB_VERSION = 1;
  var STORE = 'music';
  var RECORD_KEY = 'selected';

  var audio = null;
  var objectUrl = '';
  var selectedFileName = '';
  var playBtn, chooseBtn, fileInput, volumeInput, label, stopBtn;
  var unloading = false;
  var hideTimer = null;
  var widgetToggle = null;
  var restoring = false;
  var lastSave = 0;

  function lsGet(name, fallback) {
    try {
      var v = localStorage.getItem(KEY + '_' + name);
      return v === null ? fallback : v;
    } catch (e) { return fallback; }
  }

  function lsSet(name, value) {
    try { localStorage.setItem(KEY + '_' + name, String(value)); } catch (e) {}
  }

  function getSavedVolume() {
    var v = parseFloat(lsGet('volume', '0.25'));
    return isNaN(v) ? 0.25 : Math.max(0, Math.min(1, v));
  }

  function getSavedTime() {
    var t = parseFloat(lsGet('time', '0'));
    return isNaN(t) || t < 0 ? 0 : t;
  }

  function shouldPlay() {
    return lsGet('playing', '0') === '1';
  }

  function niceName(name) {
    if (!name) return 'No music selected';
    return name.replace(/\.[^/.]+$/, '');
  }

  function setLabel(text) {
    if (!label) return;
    label.textContent = text;
    label.title = text;
  }

  function syncPlayButton() {
    if (!playBtn || !audio) return;
    var paused = audio.paused;
    playBtn.textContent = paused ? '▶' : '❚❚';
    playBtn.setAttribute('aria-label', paused ? 'Play music' : 'Pause music');
    playBtn.title = paused ? 'Play music' : 'Pause music';
  }

  function savePosition(force) {
    if (!audio || !audio.src) return;
    var now = Date.now();
    if (!force && now - lastSave < 250) return;
    lastSave = now;
    if (isFinite(audio.currentTime)) lsSet('time', audio.currentTime);
  }

  function openDb() {
    return new Promise(function (resolve, reject) {
      if (!window.indexedDB) { reject(new Error('IndexedDB unavailable')); return; }
      var req = indexedDB.open(DB_NAME, DB_VERSION);
      req.onupgradeneeded = function () {
        var db = req.result;
        if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE);
      };
      req.onsuccess = function () { resolve(req.result); };
      req.onerror = function () { reject(req.error || new Error('Unable to open local music storage')); };
    });
  }

  function storeFile(file) {
    return openDb().then(function (db) {
      return new Promise(function (resolve, reject) {
        var tx = db.transaction(STORE, 'readwrite');
        tx.objectStore(STORE).put({
          blob: file,
          name: file.name || 'Selected music',
          type: file.type || 'audio/mpeg',
          savedAt: Date.now()
        }, RECORD_KEY);
        tx.oncomplete = function () { db.close(); resolve(); };
        tx.onerror = function () { var err = tx.error; db.close(); reject(err || new Error('Unable to save music locally')); };
      });
    });
  }

  function loadStoredFile() {
    return openDb().then(function (db) {
      return new Promise(function (resolve, reject) {
        var tx = db.transaction(STORE, 'readonly');
        var req = tx.objectStore(STORE).get(RECORD_KEY);
        req.onsuccess = function () { var value = req.result || null; db.close(); resolve(value); };
        req.onerror = function () { var err = req.error; db.close(); reject(err || new Error('Unable to read saved music')); };
      });
    });
  }

  function clearStoredFile() {
    return openDb().then(function (db) {
      return new Promise(function (resolve) {
        var tx = db.transaction(STORE, 'readwrite');
        tx.objectStore(STORE).delete(RECORD_KEY);
        tx.oncomplete = tx.onerror = function () { db.close(); resolve(); };
      });
    }).catch(function () {});
  }

  function setAudioBlob(blob, name, resume) {
    if (!blob) return;
    if (objectUrl) {
      try { URL.revokeObjectURL(objectUrl); } catch (e) {}
    }
    objectUrl = URL.createObjectURL(blob);
    selectedFileName = name || 'Selected music';
    restoring = !!resume;
    audio.src = objectUrl;
    audio.load();
    setLabel(niceName(selectedFileName));

    var restoreTime = resume ? getSavedTime() : 0;
    var startPlayback = resume ? shouldPlay() : true;

    function ready() {
      audio.removeEventListener('loadedmetadata', ready);
      try {
        if (restoreTime > 0 && isFinite(audio.duration) && audio.duration > 0) {
          audio.currentTime = Math.min(restoreTime, Math.max(0, audio.duration - 0.05));
        }
      } catch (e) {}
      restoring = false;
      if (startPlayback) playSelected(true);
      else syncPlayButton();
    }

    audio.addEventListener('loadedmetadata', ready);
  }

  function openPicker() {
    if (fileInput) fileInput.click();
  }

  function playSelected(isAutoResume) {
    if (!audio || !audio.src) { openPicker(); return; }
    lsSet('playing', '1');
    var p;
    try { p = audio.play(); } catch (e) { setLabel('Unable to play this file'); return; }
    if (p && typeof p.catch === 'function') {
      p.catch(function () {
        if (isAutoResume) setLabel(niceName(selectedFileName) + ' · Tap ▶ to continue');
        else setLabel('Tap ▶ to start music');
        syncPlayButton();
      });
    }
  }

  function pauseManually() {
    if (!audio) return;
    lsSet('playing', '0');
    savePosition(true);
    audio.pause();
  }

  function stopManually() {
    if (!audio) return;
    lsSet('playing', '0');
    lsSet('time', '0');
    try { audio.pause(); audio.currentTime = 0; } catch (e) {}
    syncPlayButton();
  }

  function useFile(file) {
    if (!file) return;
    var name = file.name || '';
    var valid = (file.type && file.type.indexOf('audio/') === 0) || /\.(mp3|wav|m4a|aac|ogg|flac|opus)$/i.test(name);
    if (!valid) { setLabel('Please choose an audio file'); return; }

    setLabel('Saving music…');
    storeFile(file).then(function () {
      lsSet('time', '0');
      lsSet('playing', '1');
      setAudioBlob(file, name, false);
    }).catch(function () {
      // Still allow current-session playback if browser storage fails.
      lsSet('time', '0');
      lsSet('playing', '1');
      setAudioBlob(file, name, false);
      setLabel(niceName(name) + ' · current page only');
    });
  }

  function buildPlayer() {
    var style = document.createElement('style');
    style.textContent = [
      '#vighnesh-payroll-systems-local-music{position:fixed;right:12px;bottom:12px;z-index:2147483000;display:flex;align-items:center;justify-content:flex-start;gap:6px;width:max-content;max-width:calc(100vw - 24px);min-height:48px;padding:6px 7px;background:#0b1f3a;color:#fff;border:1px solid rgba(255,255,255,.12);border-radius:12px;box-shadow:0 6px 20px rgba(0,0,0,.25);font:600 12px/1 Inter,Segoe UI,Arial,sans-serif;transition:width .2s ease,transform .2s ease,opacity .2s ease}',
      '#vighnesh-payroll-systems-local-music *{box-sizing:border-box}',
      '#vighnesh-payroll-systems-local-music button{border:0;cursor:pointer;font-family:inherit;font-weight:800;display:inline-flex;align-items:center;justify-content:center;flex:0 0 auto}',
      '#vighnesh-payroll-systems-local-music .pm-toggle{width:36px;height:36px;border-radius:50%;background:#2563eb;color:#fff;font-size:16px;box-shadow:0 2px 8px rgba(0,0,0,.2)}',
      '#vighnesh-payroll-systems-local-music .pm-play{width:34px;height:34px;border-radius:50%;background:#2563eb;color:#fff;font-size:15px}',
      '#vighnesh-payroll-systems-local-music .pm-choose{height:34px;padding:0 10px;border-radius:8px;background:#fff;color:#0b1f3a;white-space:nowrap;font-size:11px}',
      '#vighnesh-payroll-systems-local-music .pm-stop{height:34px;padding:0 8px;border-radius:8px;background:#17345d;color:#fff;white-space:nowrap;font-size:11px}',
      '#vighnesh-payroll-systems-local-music .pm-name{display:block;min-width:72px;width:clamp(72px,18vw,145px);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:#dbeafe;font-weight:600;line-height:34px}',
      '#vighnesh-payroll-systems-local-music .pm-volume{width:64px;height:28px;margin:0;accent-color:#60a5fa;flex:0 0 auto}',
      '#vighnesh-payroll-systems-local-music .pm-file{position:absolute;width:1px;height:1px;opacity:0;pointer-events:none}',
      '#vighnesh-payroll-systems-local-music .pm-collapsible{display:none;align-items:center;gap:6px}',
      '#vighnesh-payroll-systems-local-music.is-open .pm-collapsible{display:flex}',
      '#vighnesh-payroll-systems-local-music.is-open .pm-toggle{background:#17345d}',
      '@media(hover:hover){#vighnesh-payroll-systems-local-music:hover .pm-collapsible,#vighnesh-payroll-systems-local-music:focus-within .pm-collapsible{display:flex}#vighnesh-payroll-systems-local-music:hover .pm-toggle{background:#17345d}}',
      '@media(max-width:680px){#vighnesh-payroll-systems-local-music{right:10px;bottom:10px;min-height:46px;padding:5px;gap:5px;border-radius:11px}#vighnesh-payroll-systems-local-music .pm-name{flex:1;width:auto;min-width:0}#vighnesh-payroll-systems-local-music .pm-choose{padding:0 9px}}',
      '@media(max-width:470px){#vighnesh-payroll-systems-local-music .pm-volume{display:none}#vighnesh-payroll-systems-local-music .pm-name{font-size:11px}#vighnesh-payroll-systems-local-music .pm-choose{width:34px;padding:0;font-size:0}#vighnesh-payroll-systems-local-music .pm-choose::before{content:"♪";font-size:15px}#vighnesh-payroll-systems-local-music .pm-stop{width:34px;padding:0;font-size:0}#vighnesh-payroll-systems-local-music .pm-stop::before{content:"■";font-size:12px}}',
      '@media(max-width:340px){#vighnesh-payroll-systems-local-music{gap:4px;padding:4px}#vighnesh-payroll-systems-local-music .pm-toggle,#vighnesh-payroll-systems-local-music .pm-play,#vighnesh-payroll-systems-local-music .pm-choose,#vighnesh-payroll-systems-local-music .pm-stop{width:32px;height:32px}#vighnesh-payroll-systems-local-music .pm-name{line-height:32px}}'
    ].join('');
    document.head.appendChild(style);

    var bar = document.createElement('div');
    bar.id = 'vighnesh-payroll-systems-local-music';
    bar.setAttribute('role', 'group');
    bar.setAttribute('aria-label', 'Continuous local music player');

    widgetToggle = document.createElement('button');
    widgetToggle.type = 'button';
    widgetToggle.className = 'pm-toggle';
    widgetToggle.textContent = '♫';
    widgetToggle.title = 'Show music controls';
    widgetToggle.setAttribute('aria-label', 'Show music controls');

    playBtn = document.createElement('button');
    playBtn.type = 'button';
    playBtn.className = 'pm-play';
    playBtn.textContent = '▶';

    chooseBtn = document.createElement('button');
    chooseBtn.type = 'button';
    chooseBtn.className = 'pm-choose';
    chooseBtn.textContent = '📁 Choose Music';
    chooseBtn.title = 'Choose music from this phone or computer';

    label = document.createElement('span');
    label.className = 'pm-name';
    label.textContent = 'Loading music…';

    stopBtn = document.createElement('button');
    stopBtn.type = 'button';
    stopBtn.className = 'pm-stop';
    stopBtn.textContent = '■ Stop';
    stopBtn.title = 'Stop music';

    fileInput = document.createElement('input');
    fileInput.type = 'file';
    fileInput.className = 'pm-file';
    fileInput.accept = 'audio/*,.mp3,.wav,.m4a,.aac,.ogg,.flac,.opus';

    volumeInput = document.createElement('input');
    volumeInput.type = 'range';
    volumeInput.className = 'pm-volume';
    volumeInput.min = '0';
    volumeInput.max = '1';
    volumeInput.step = '0.05';
    volumeInput.value = String(audio.volume);
    volumeInput.title = 'Volume';
    volumeInput.setAttribute('aria-label', 'Music volume');

    var collapsible = document.createElement('div');
    collapsible.className = 'pm-collapsible';
    collapsible.appendChild(playBtn);
    collapsible.appendChild(chooseBtn);
    collapsible.appendChild(label);
    collapsible.appendChild(stopBtn);
    collapsible.appendChild(volumeInput);

    bar.appendChild(widgetToggle);
    bar.appendChild(collapsible);
    bar.appendChild(fileInput);
    document.body.appendChild(bar);

    function openWidget() {
      bar.classList.add('is-open');
      widgetToggle.setAttribute('aria-label', 'Hide music controls');
      widgetToggle.title = 'Hide music controls';
      clearTimeout(hideTimer);
      hideTimer = setTimeout(function () {
        if (!bar.matches(':hover') && document.activeElement !== widgetToggle && !bar.contains(document.activeElement)) {
          bar.classList.remove('is-open');
          widgetToggle.setAttribute('aria-label', 'Show music controls');
          widgetToggle.title = 'Show music controls';
        }
      }, 4000);
    }

    widgetToggle.addEventListener('click', function (e) {
      e.preventDefault(); e.stopPropagation();
      if (bar.classList.contains('is-open')) {
        bar.classList.remove('is-open');
        widgetToggle.setAttribute('aria-label', 'Show music controls');
        widgetToggle.title = 'Show music controls';
        clearTimeout(hideTimer);
      } else openWidget();
    });
    bar.addEventListener('mouseenter', openWidget);
    bar.addEventListener('focusin', openWidget);
    chooseBtn.addEventListener('click', function (e) { e.preventDefault(); e.stopPropagation(); openPicker(); });
    fileInput.addEventListener('change', function () { useFile(fileInput.files && fileInput.files[0]); });
    playBtn.addEventListener('click', function (e) {
      e.preventDefault(); e.stopPropagation();
      if (!audio.src) { openPicker(); return; }
      if (audio.paused) playSelected(false); else pauseManually();
    });
    stopBtn.addEventListener('click', function (e) { e.preventDefault(); e.stopPropagation(); stopManually(); });
    volumeInput.addEventListener('input', function () { audio.volume = Number(volumeInput.value); lsSet('volume', audio.volume); });
  }

  function restoreStoredMusic() {
    loadStoredFile().then(function (record) {
      if (record && record.blob) setAudioBlob(record.blob, record.name, true);
      else setLabel('No music selected');
    }).catch(function () { setLabel('No music selected'); });
  }

  function init() {
    if (document.getElementById('vighnesh-payroll-systems-local-music')) return;

    audio = document.createElement('audio');
    audio.preload = 'auto';
    audio.loop = true;
    audio.volume = getSavedVolume();
    audio.setAttribute('playsinline', '');

    buildPlayer();

    audio.addEventListener('play', function () { if (!restoring) lsSet('playing', '1'); syncPlayButton(); });
    audio.addEventListener('pause', function () { syncPlayButton(); });
    audio.addEventListener('timeupdate', function () { savePosition(false); });
    audio.addEventListener('ended', function () { savePosition(true); syncPlayButton(); });
    audio.addEventListener('error', function () { setLabel('Cannot play this audio file'); syncPlayButton(); });

    window.addEventListener('pagehide', function () { unloading = true; savePosition(true); });
    window.addEventListener('beforeunload', function () { unloading = true; savePosition(true); });
    document.addEventListener('visibilitychange', function () { if (document.hidden) savePosition(true); });

    restoreStoredMusic();
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
}());
