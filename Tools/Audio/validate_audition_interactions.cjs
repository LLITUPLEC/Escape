// Exercise the page's actual inline program with DOM attributes parsed from HTML.
// Only DOM/media APIs are faked; selection/export/playback logic is not reimplemented.
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const fixture = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
const checks = {};
function element() {
  const handlers = {};
  const classes = new Set();
  return { handlers, dataset: {}, hidden: true, textContent: '', value: '',
    classList: { add: x => classes.add(x), remove: x => classes.delete(x), contains: x => classes.has(x) },
    addEventListener: (name, fn) => { (handlers[name] ||= []).push(fn); },
    fire(name, event = {}) { return Promise.all((handlers[name] || []).map(fn => fn(event))); },
    focus() { this.focused = true; }, select() { this.selected = true; }, remove() {}, click() { this.clicked = true; }
  };
}
async function run() {
  const nodes = {};
  for (const id of ['auditionPlayer', 'nowPlaying', 'choiceCount', 'exportStatus', 'storageNote', 'exportBox', 'exportText', 'stopButton', 'exportButton', 'downloadButton', 'copyButton', 'resetButton']) nodes[id] = element();
  const player = nodes.auditionPlayer;
  const pending = [];
  player.playCalls = 0; player.pauseCalls = 0; player.loadCalls = 0;
  player.play = () => { player.playCalls++; return new Promise((resolve, reject) => pending.push({ resolve, reject })); };
  player.pause = () => { player.pauseCalls++; };
  player.load = () => { player.loadCalls++; };
  player.removeAttribute = attr => { delete player[attr]; };
  const cards = fixture.cards.map(data => {
    const card = element(); card.dataset = data;
    card.radios = ['A', 'B', ''].map(value => {
      const radio = element(); radio.value = value;
      radio.matches = selector => selector === 'input[type="radio"]';
      radio.closest = selector => selector === '.effect' ? card : null;
      return radio;
    });
    card.querySelectorAll = () => card.radios;
    return card;
  });
  const buttons = fixture.buttons.map(data => {
    const button = element(); button.dataset = data;
    button.closest = selector => selector === 'button[data-audio]' ? button : null;
    return button;
  });
  const document = element();
  document.getElementById = id => nodes[id];
  document.querySelectorAll = () => cards;
  const createdLinks = [];
  document.createElement = () => { const link = element(); createdLinks.push(link); return link; };
  document.body = { appendChild() {} };
  const memory = {};
  const localStorage = { getItem: key => memory[key] || null, setItem: (key, value) => { memory[key] = value; } };
  const revokes = [];
  const urls = { createObjectURL: () => 'blob:fixture', revokeObjectURL: url => revokes.push(url) };
  vm.runInNewContext(fs.readFileSync(process.argv[2], 'utf8'), { document, localStorage, navigator: {}, Blob, URL: urls, setTimeout: fn => fn(), console });
  assert.equal(player.playCalls, 0); checks.no_autoplay_on_initialization = true;
  assert.equal(nodes.choiceCount.textContent, 'Выбрано 0 из 20');
  await document.fire('change', { target: cards[0].radios[0] });
  await document.fire('change', { target: cards[1].radios[1] });
  assert.equal(nodes.choiceCount.textContent, 'Выбрано 2 из 20');
  assert.equal(cards[0].radios[0].checked, true);
  assert.equal(cards[0].radios[1].checked, false);
  checks.per_effect_selection_and_exclusive_radios = true;
  assert.equal(Object.keys(JSON.parse(memory['escape-audition-0.3.5-choices'])).length, 2); checks.local_persistence = true;
  await nodes.exportButton.fire('click');
  assert.equal(nodes.exportBox.hidden, false);
  assert.match(nodes.exportText.value, /Ряд камней: A \[line_clear\]/);
  assert.match(nodes.exportText.value, /Каскад: падение камней: B \[cascade_fall\]/);
  assert.equal((nodes.exportText.value.match(/\[[a-z_]+\]/g) || []).length, 20); checks.export_contains_all_twenty_effects = true;
  await nodes.downloadButton.fire('click');
  assert.equal(createdLinks.at(-1).download, 'Escape-audio-choices-0.3.5.txt');
  assert.equal(createdLinks.at(-1).clicked, true); assert.deepEqual(revokes, ['blob:fixture']); checks.text_download_prepared = true;
  await nodes.copyButton.fire('click');
  assert.equal(nodes.exportText.selected, true); checks.clipboard_fallback_selects_text = true;
  await document.fire('click', { target: buttons[0] });
  assert.equal(player.src, buttons[0].dataset.audio);
  await document.fire('click', { target: buttons[1] });
  assert.equal(player.src, buttons[1].dataset.audio);
  assert.equal(player.playCalls, 2); assert.equal(player.pauseCalls, 2);
  assert.equal(buttons[0].classList.contains('is-playing'), false);
  assert.equal(buttons[1].classList.contains('is-playing'), true); checks.single_player_retarget_pauses_previous_sound = true;
  pending[0].reject(new Error('AbortError'));
  await Promise.resolve(); await Promise.resolve();
  assert.equal(nodes.nowPlaying.textContent, buttons[1].dataset.caption); checks.stale_play_rejection_cannot_overwrite_current_caption = true;
  pending[1].resolve();
  await nodes.stopButton.fire('click');
  assert.equal(player.src, undefined); assert.equal(player.loadCalls, 1); checks.stop_clears_player = true;
  await nodes.resetButton.fire('click');
  assert.equal(nodes.choiceCount.textContent, 'Выбрано 0 из 20');
  assert.equal(Object.keys(JSON.parse(memory['escape-audition-0.3.5-choices'])).length, 0); checks.reset_clears_selection = true;
  process.stdout.write(JSON.stringify({ passed: true, checks }));
}
run().catch(error => { console.error(error); process.exit(1); });
