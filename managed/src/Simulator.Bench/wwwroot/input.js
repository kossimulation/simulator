// Joystick / RC transmitter input (Gamepad API).
//
// An EdgeTX/OpenTX radio (RadioMaster TX12, Zorro, Boxer, Jumper T-series...) in
// "USB Joystick (HID)" mode shows up as a gamepad whose axes are the radio's
// channels CH1..CH8. Each bench axis picks a source axis, with invert, expo and
// calibration; switches (axis high/low or buttons) can trigger commands.
// Settings are stored per device in localStorage.

const STORE_KEY = 'bench.input.v1';
const AXES = ['throttle', 'pitch', 'roll', 'yaw'];

/** Presets: src = gamepad axis index. Mode: centered (-1..1), absolute (0..1 from full travel), rate (stick moves value). */
export const PRESETS = {
  'rc-aetr': {
    label: 'RC пульт, AETR (TX12 / EdgeTX за замовч.)',
    axes: {
      roll: { src: 0, invert: false, expo: 0.2, dead: 0.02, mode: 'centered' },
      pitch: { src: 1, invert: false, expo: 0.2, dead: 0.02, mode: 'centered' },
      throttle: { src: 2, invert: false, expo: 0, dead: 0, mode: 'absolute' },
      yaw: { src: 3, invert: false, expo: 0.2, dead: 0.02, mode: 'centered' },
    },
  },
  'rc-taer': {
    label: 'RC пульт, TAER (Spektrum / FrSky)',
    axes: {
      throttle: { src: 0, invert: false, expo: 0, dead: 0, mode: 'absolute' },
      roll: { src: 1, invert: false, expo: 0.2, dead: 0.02, mode: 'centered' },
      pitch: { src: 2, invert: false, expo: 0.2, dead: 0.02, mode: 'centered' },
      yaw: { src: 3, invert: false, expo: 0.2, dead: 0.02, mode: 'centered' },
    },
  },
  gamepad: {
    label: 'Геймпад (Xbox / PlayStation)',
    axes: {
      roll: { src: 0, invert: false, expo: 0.3, dead: 0.08, mode: 'centered' },
      pitch: { src: 1, invert: true, expo: 0.3, dead: 0.08, mode: 'centered' },
      yaw: { src: 2, invert: false, expo: 0.3, dead: 0.08, mode: 'centered' },
      throttle: { src: 3, invert: true, expo: 0, dead: 0.08, mode: 'rate' },
    },
  },
};

function defaultDeviceSettings(id) {
  const rcLike = /edgetx|opentx|radiomaster|tx12|tx16|zorro|boxer|jumper|frsky|taranis|horus|betafpv|joystick/i.test(id);
  return makeFromPreset(rcLike ? 'rc-aetr' : 'gamepad');
}

function makeFromPreset(name) {
  return {
    preset: name,
    axes: structuredClone(PRESETS[name].axes),
    cal: {},        // axis index -> {min, max, center}
    binds: [],      // {source: 'b3' | 'a5+' | 'a5-', command: 'pause' | 'reset' | 'action:<id>'}
  };
}

function loadStore() {
  try { return JSON.parse(localStorage.getItem(STORE_KEY)) ?? {}; } catch { return {}; }
}

function saveStore(store) {
  try { localStorage.setItem(STORE_KEY, JSON.stringify(store)); } catch { /* private mode */ }
}

export class InputMapper {
  /** @param {(cmd: string) => void} onCommand */
  constructor(onCommand) {
    this.onCommand = onCommand;
    this.store = loadStore();
    this.deviceId = this.store.lastDevice ?? null;
    this.calibrating = null;   // {until, min[], max[], center[]}
    this.lastSwitch = new Map();
    this.active = false;       // true once a mapped stick moved; then the device owns the axes
    this.rest = null;          // axis snapshot when the device was first seen
    this.listeners = [];
  }

  /** Connected gamepads (null slots removed). */
  devices() {
    const pads = navigator.getGamepads ? [...navigator.getGamepads()] : [];
    return pads.filter((p) => p && p.connected);
  }

  device() {
    const list = this.devices();
    return list.find((p) => p.id === this.deviceId) ?? list[0] ?? null;
  }

  settings(id = this.device()?.id) {
    if (!id) return null;
    this.store.devices ??= {};
    this.store.devices[id] ??= defaultDeviceSettings(id);
    return this.store.devices[id];
  }

  select(id) {
    this.deviceId = id;
    this.store.lastDevice = id;
    this.active = false;
    this.rest = null;
    this.save();
  }

  applyPreset(name) {
    const s = this.settings();
    if (!s) return;
    const fresh = makeFromPreset(name);
    s.preset = name;
    s.axes = fresh.axes;
    this.save();
  }

  save() {
    saveStore(this.store);
    this.listeners.forEach((f) => f());
  }

  onChange(f) { this.listeners.push(f); }

  /** Starts an 8 s calibration: sticks centred at start, then move every stick to its limits. */
  startCalibration() {
    const gp = this.device();
    if (!gp) return;
    this.calibrating = {
      until: performance.now() + 8000,
      center: [...gp.axes],
      min: [...gp.axes],
      max: [...gp.axes],
    };
  }

  resetCalibration() {
    const s = this.settings();
    if (s) { s.cal = {}; this.save(); }
  }

  /** Raw value of axis i normalised by calibration to -1..1 (centre -> 0). */
  centered(gp, i, s) {
    const v = gp.axes[i] ?? 0;
    const c = s.cal[i];
    if (!c) return Math.max(-1, Math.min(1, v));
    if (v >= c.center) return c.max - c.center > 1e-3 ? Math.min(1, (v - c.center) / (c.max - c.center)) : 0;
    return c.center - c.min > 1e-3 ? Math.max(-1, (v - c.center) / (c.center - c.min)) : 0;
  }

  /** Raw value of axis i mapped from full travel to 0..1. */
  absolute(gp, i, s) {
    const v = gp.axes[i] ?? 0;
    const c = s.cal[i] ?? { min: -1, max: 1 };
    return c.max - c.min > 1e-3 ? Math.max(0, Math.min(1, (v - c.min) / (c.max - c.min))) : 0;
  }

  /**
   * Reads the device and updates `controls` in place.
   * @returns {boolean} true when controls changed
   */
  poll(controls, dt) {
    const gp = this.device();
    if (!gp) { this.active = false; return false; }
    if (this.deviceId !== gp.id) this.select(gp.id);
    const s = this.settings(gp.id);

    if (this.calibrating) {
      const cal = this.calibrating;
      gp.axes.forEach((v, i) => { cal.min[i] = Math.min(cal.min[i], v); cal.max[i] = Math.max(cal.max[i], v); });
      if (performance.now() > cal.until) {
        gp.axes.forEach((_, i) => {
          if (cal.max[i] - cal.min[i] > 0.2) s.cal[i] = { min: cal.min[i], max: cal.max[i], center: cal.center[i] };
        });
        this.calibrating = null;
        this.save();
      }
    }

    this.pollSwitches(gp, s);

    // Until a mapped stick moves away from where it rested when connected, leave the
    // axes to the sliders/keyboard: an RC throttle at idle must not zero a trimmed hover.
    this.rest ??= [...gp.axes];
    if (!this.active) {
      const moved = AXES.some((a) => {
        const m = s.axes[a];
        return m && m.src >= 0 && Math.abs((gp.axes[m.src] ?? 0) - (this.rest[m.src] ?? 0)) > 0.08;
      });
      if (!moved) return false;
      this.active = true;
    }

    let changed = false;
    for (const axis of AXES) {
      const m = s.axes[axis];
      if (!m || m.src < 0 || m.src >= gp.axes.length) continue;
      let value;
      if (m.mode === 'absolute') {
        value = this.absolute(gp, m.src, s);
        if (m.invert) value = 1 - value;
        if (value < m.dead) value = 0;
      } else {
        let v = this.centered(gp, m.src, s);
        if (m.invert) v = -v;
        v = Math.abs(v) < m.dead ? 0 : Math.sign(v) * (Math.abs(v) - m.dead) / (1 - m.dead);
        v = (1 - m.expo) * v + m.expo * v * v * v;
        if (m.mode === 'rate') {
          value = controls[axis] + v * 0.4 * dt;
        } else {
          value = v;
        }
      }
      const lo = axis === 'throttle' ? 0 : -1;
      value = Math.max(lo, Math.min(1, value));
      if (Math.abs(value - controls[axis]) > 1e-4) { controls[axis] = value; changed = true; }
    }
    return changed;
  }

  pollSwitches(gp, s) {
    for (const b of s.binds) {
      if (!b.source || !b.command) continue;
      let on = false;
      if (b.source[0] === 'b') {
        on = !!gp.buttons[+b.source.slice(1)]?.pressed;
      } else {
        const i = parseInt(b.source.slice(1), 10);
        const v = gp.axes[i] ?? 0;
        on = b.source.endsWith('+') ? v > 0.5 : v < -0.5;
      }
      const key = `${b.source}|${b.command}`;
      const was = this.lastSwitch.get(key);
      this.lastSwitch.set(key, on);
      if (on && was === false) this.onCommand(b.command);  // rising edge only; ignore state at connect
    }
  }
}

// ---------------------------------------------------------------- settings panel

/** Builds the "Пульт / джойстик" panel inside `root`. */
export function mountInputPanel(root, mapper, getActions) {
  const el = (tag, props = {}, ...kids) => {
    const n = Object.assign(document.createElement(tag), props);
    n.append(...kids);
    return n;
  };
  const labels = { throttle: 'Газ / крок', pitch: 'Тангаж', roll: 'Крен', yaw: 'Педалі' };

  const status = el('span', { className: 'muted' });
  const deviceSel = el('select');
  const presetSel = el('select');
  for (const [k, p] of Object.entries(PRESETS)) presetSel.append(new Option(p.label, k));
  const calBtn = el('button', { textContent: 'Калібрувати (8 с)' });
  const calReset = el('button', { textContent: 'Скинути калібрування' });
  const monitor = el('div', { className: 'axis-monitor' });
  const table = el('table', { className: 'map-table' });
  const binds = el('div', { className: 'binds' });
  const addBind = el('button', { textContent: '+ тумблер / кнопка' });

  root.append(
    el('div', { className: 'input-head' },
      el('label', {}, 'Пристрій ', deviceSel),
      el('label', {}, 'Схема ', presetSel),
      calBtn, calReset, status),
    el('p', { className: 'hint' },
      'Пульт (TX12 тощо) — по USB у режимі «USB Joystick (HID)». Браузер показує пристрій ' +
      'після першого руху стіка чи натискання кнопки. Канали CH1…CH8 = осі 0…7 нижче: ' +
      'поворушіть стік і подивіться, яка смуга рухається. Калібрування: стіки в центр, газ униз, ' +
      'натиснути «Калібрувати» і за 8 с пройти всі стіки до упорів.'),
    monitor, table,
    el('div', { className: 'binds-head' }, el('strong', {}, 'Тумблери → команди'), addBind),
    binds);

  const sourceOptions = (n) => {
    const o = [new Option('—', '-1')];
    for (let i = 0; i < n; i++) o.push(new Option(`вісь ${i} (CH${i + 1})`, String(i)));
    return o;
  };

  function renderTable() {
    const gp = mapper.device();
    const s = mapper.settings();
    table.replaceChildren(el('tr', {},
      ...['Вісь стенда', 'Джерело', 'Інверсія', 'Режим', 'Експо', 'Мертва зона', 'Значення'].map((h) => el('th', { textContent: h }))));
    if (!gp || !s) return;
    presetSel.value = s.preset;
    for (const a of AXES) {
      const m = s.axes[a];
      const src = el('select');
      src.append(...sourceOptions(gp.axes.length));
      src.value = String(m.src);
      src.onchange = () => { m.src = +src.value; mapper.save(); };
      const inv = el('input', { type: 'checkbox', checked: m.invert });
      inv.onchange = () => { m.invert = inv.checked; mapper.save(); };
      const mode = el('select');
      mode.append(new Option('центр ±1', 'centered'), new Option('абсолютна 0…1', 'absolute'), new Option('швидкість', 'rate'));
      mode.value = m.mode;
      mode.onchange = () => { m.mode = mode.value; mapper.save(); };
      const expo = el('input', { type: 'range', min: 0, max: 0.9, step: 0.05, value: m.expo });
      expo.oninput = () => { m.expo = +expo.value; mapper.save(); };
      const dead = el('input', { type: 'range', min: 0, max: 0.2, step: 0.01, value: m.dead });
      dead.oninput = () => { m.dead = +dead.value; mapper.save(); };
      const out = el('output', { id: `map-out-${a}` });
      table.append(el('tr', {}, el('td', { textContent: labels[a] }), el('td', {}, src), el('td', {}, inv),
        el('td', {}, mode), el('td', {}, expo), el('td', {}, dead), el('td', {}, out)));
    }
  }

  function renderBinds() {
    const gp = mapper.device();
    const s = mapper.settings();
    binds.replaceChildren();
    if (!gp || !s) return;
    s.binds.forEach((b, idx) => {
      const src = el('select');
      src.append(new Option('—', ''));
      for (let i = 0; i < gp.axes.length; i++) {
        src.append(new Option(`вісь ${i} (CH${i + 1}) вгору`, `a${i}+`), new Option(`вісь ${i} (CH${i + 1}) вниз`, `a${i}-`));
      }
      for (let i = 0; i < gp.buttons.length; i++) src.append(new Option(`кнопка ${i}`, `b${i}`));
      src.value = b.source;
      src.onchange = () => { b.source = src.value; mapper.save(); };
      const cmd = el('select');
      cmd.append(new Option('—', ''), new Option('Пауза / пуск', 'pause'), new Option('Скинути', 'reset'));
      for (const a of getActions()) cmd.append(new Option(a.label, `action:${a.id}`));
      cmd.value = b.command;
      cmd.onchange = () => { b.command = cmd.value; mapper.save(); };
      const del = el('button', { textContent: '✕', title: 'Видалити' });
      del.onclick = () => { s.binds.splice(idx, 1); mapper.save(); renderBinds(); };
      binds.append(el('div', { className: 'bind' }, src, el('span', { textContent: '→' }), cmd, del));
    });
  }

  function renderDevices() {
    const list = mapper.devices();
    const cur = mapper.device();
    deviceSel.replaceChildren(...(list.length ? list.map((p) => new Option(p.id, p.id)) : [new Option('не знайдено — поворушіть стік', '')]));
    if (cur) deviceSel.value = cur.id;
    renderTable();
    renderBinds();
  }

  deviceSel.onchange = () => { mapper.select(deviceSel.value); renderDevices(); };
  presetSel.onchange = () => { mapper.applyPreset(presetSel.value); renderTable(); };
  calBtn.onclick = () => mapper.startCalibration();
  calReset.onclick = () => mapper.resetCalibration();
  addBind.onclick = () => {
    const s = mapper.settings();
    if (!s) return;
    s.binds.push({ source: '', command: '' });
    mapper.save();
    renderBinds();
  };
  window.addEventListener('gamepadconnected', renderDevices);
  window.addEventListener('gamepaddisconnected', renderDevices);

  let shown = '';
  return {
    /** Per-frame refresh of the live monitor; rebuilds the panel when the device set changes. */
    update(controls) {
      const list = mapper.devices();
      const sig = list.map((p) => `${p.id}/${p.axes.length}/${p.buttons.length}`).join('|');
      if (sig !== shown) { shown = sig; renderDevices(); }
      const gp = mapper.device();
      if (!gp) { status.textContent = ''; monitor.replaceChildren(); return; }
      status.textContent = mapper.calibrating
        ? `калібрування… ${Math.ceil((mapper.calibrating.until - performance.now()) / 1000)} с`
        : (mapper.active ? 'керує пульт' : 'очікує руху стіка');
      if (monitor.childElementCount !== gp.axes.length) {
        monitor.replaceChildren(...gp.axes.map((_, i) => el('div', { className: 'mon' },
          el('span', { textContent: `${i}` }), el('div', { className: 'bar' }, el('i')), el('b'))));
      }
      gp.axes.forEach((v, i) => {
        const row = monitor.children[i];
        row.querySelector('i').style.left = `${(Math.max(-1, Math.min(1, v)) + 1) * 50}%`;
        row.querySelector('b').textContent = v.toFixed(2);
      });
      for (const a of AXES) {
        const o = document.getElementById(`map-out-${a}`);
        if (o) o.textContent = controls[a].toFixed(2);
      }
    },
  };
}
