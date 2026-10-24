
    (() => {
      'use strict';
      const player = document.getElementById('auditionPlayer');
      const now = document.getElementById('nowPlaying');
      const counter = document.getElementById('choiceCount');
      const cards = [...document.querySelectorAll('.effect')];
      const status = document.getElementById('exportStatus');
      const storageKey = 'escape-audition-0.3.5-choices';
      let choices = {};
      let playingButton = null;
      let playVersion = 0;
      let storageAvailable = true;
      try {
        const saved = JSON.parse(localStorage.getItem(storageKey) || '{}');
        if (saved && typeof saved === 'object' && !Array.isArray(saved)) {
          for (const card of cards) if (saved[card.dataset.id] === 'A' || saved[card.dataset.id] === 'B') choices[card.dataset.id] = saved[card.dataset.id];
        }
      } catch (_) { storageAvailable = false; }
      if (!storageAvailable) document.getElementById('storageNote').textContent = 'Браузер не дал сохранить выбор на диске. Он останется здесь до закрытия страницы; экспортируйте список текстом.';
      function updateChoices() {
        let count = 0;
        for (const card of cards) {
          const value = choices[card.dataset.id] || '';
          if (value) count++;
          for (const input of card.querySelectorAll('input[type="radio"]')) input.checked = input.value === value;
        }
        counter.textContent = 'Выбрано ' + count + ' из ' + cards.length;
        if (storageAvailable) {
          try { localStorage.setItem(storageKey, JSON.stringify(choices)); }
          catch (_) { storageAvailable = false; document.getElementById('storageNote').textContent = 'Автосохранение недоступно. Экспортируйте выбор перед закрытием страницы.'; }
        }
      }
      function exportText() {
        return ['Escape · звуки A/B · 0.3.5', 'A — Минерал и металл', 'B — Резонанс и пар', '', ...cards.map((card, i) => String(i + 1).padStart(2, '0') + '. ' + card.dataset.title + ': ' + (choices[card.dataset.id] || 'не выбран') + ' [' + card.dataset.id + ']'), '', 'Без изменений: Fury/Bloodlust, Shield/Heroism, критический удар.', 'Этот список — выбор для обсуждения, не автоматическая интеграция.'].join('\n');
      }
      function showExport() {
        document.getElementById('exportBox').hidden = false;
        document.getElementById('exportText').value = exportText();
      }
      document.addEventListener('change', event => {
        const input = event.target;
        if (!input.matches('input[type="radio"]')) return;
        const card = input.closest('.effect');
        if (!card) return;
        if (input.value === 'A' || input.value === 'B') choices[card.dataset.id] = input.value;
        else delete choices[card.dataset.id];
        updateChoices();
        if (!document.getElementById('exportBox').hidden) showExport();
        status.textContent = '';
      });
      function clearPlaying() {
        if (playingButton) playingButton.classList.remove('is-playing');
        playingButton = null;
      }
      document.addEventListener('click', event => {
        const button = event.target.closest('button[data-audio]');
        if (!button) return;
        const version = ++playVersion;
        player.pause();
        clearPlaying();
        playingButton = button;
        button.classList.add('is-playing');
        player.src = button.dataset.audio;
        now.textContent = button.dataset.caption;
        // Only a direct user gesture reaches play(): no autoplay, timers, or fake progress.
        const started = player.play();
        if (started && typeof started.catch === 'function') started.catch(() => { if (version !== playVersion) return; clearPlaying(); now.textContent = 'Нажмите воспроизведение на плеере или проверьте папку WAV.'; });
      });
      player.addEventListener('ended', () => { clearPlaying(); now.textContent = 'Прослушано · выберите следующий звук'; });
      player.addEventListener('error', () => { clearPlaying(); now.textContent = 'Файл не открылся. Сохраните HTML вместе с папками WAV.'; });
      document.getElementById('stopButton').addEventListener('click', () => { playVersion++; player.pause(); player.removeAttribute('src'); player.load(); clearPlaying(); now.textContent = 'Остановлено'; });
      document.getElementById('exportButton').addEventListener('click', () => { showExport(); status.textContent = 'Список готов — можно скопировать или скачать.'; });
      document.getElementById('downloadButton').addEventListener('click', () => {
        const blob = new Blob(['\ufeff' + exportText()], { type: 'text/plain;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url; link.download = 'Escape-audio-choices-0.3.5.txt';
        document.body.appendChild(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        status.textContent = 'Текстовый файл подготовлен для сохранения.';
      });
      document.getElementById('copyButton').addEventListener('click', async () => {
        showExport();
        try { if (!navigator.clipboard) throw new Error('clipboard unavailable'); await navigator.clipboard.writeText(exportText()); status.textContent = 'Выбор скопирован.'; }
        catch (_) { const text = document.getElementById('exportText'); text.focus(); text.select(); status.textContent = 'Текст выделен. Скопируйте его обычной командой браузера.'; }
      });
      document.getElementById('resetButton').addEventListener('click', () => { choices = {}; updateChoices(); if (!document.getElementById('exportBox').hidden) showExport(); status.textContent = 'Выбор сброшен. Можно отметить варианты заново.'; });
      updateChoices();
    })();
  