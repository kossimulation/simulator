// Bench front-end: WebSocket telemetry -> 3D scene, PFD, map, charts; controls -> server.
import * as THREE from './vendor/three.module.min.js';
import { InputMapper, mountInputPanel } from './input.js';

const $ = (id) => document.getElementById(id);
const DEG = Math.PI / 180;

// ---------------------------------------------------------------- state
const state = {
  info: null,
  snap: null,
  revision: -1,
  controls: { throttle: 0, pitch: 0, roll: 0, yaw: 0 },
  trail: [],          // [north, east, up]
  history: { t: [], series: new Map() },
  lastTime: -1,
};
const HISTORY_SECONDS = 60;
const TRAIL_MAX = 6000;

// ---------------------------------------------------------------- connection
let ws = null;

function connect() {
  const url = `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws`;
  ws = new WebSocket(url);
  ws.onopen = () => setConn(true);
  ws.onclose = () => { setConn(false); setTimeout(connect, 1000); };
  ws.onmessage = (e) => onSnapshot(JSON.parse(e.data));
}

function send(msg) {
  if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(msg));
}

function setConn(ok) {
  const el = $('conn');
  el.textContent = ok ? 'на звʼязку' : 'немає звʼязку';
  el.className = `pill ${ok ? 'ok' : 'bad'}`;
}

async function loadInfo() {
  const info = await (await fetch('/api/info')).json();
  state.info = info;
  $('model-name').textContent = info.name;
  const box = $('actions');
  box.replaceChildren();
  for (const a of info.actions) {
    const b = document.createElement('button');
    b.textContent = a.label;
    b.onclick = () => send({ type: 'action', id: a.id });
    box.append(b);
  }
  buildVehicle(info.kind);
}

// ---------------------------------------------------------------- snapshot handling
function onSnapshot(snap) {
  state.snap = snap;
  const f = snap.frame;

  if (snap.revision !== state.revision) {
    // Reset or action on the server: take its controls, clear history.
    state.revision = snap.revision;
    Object.assign(state.controls, snap.controls);
    syncSliders();
    state.trail = [];
    state.history = { t: [], series: new Map() };
    state.lastTime = -1;
  }

  if (f.time < state.lastTime) {
    state.trail = [];
    state.history = { t: [], series: new Map() };
  }
  if (f.time !== state.lastTime) {
    state.lastTime = f.time;
    pushTrail(f);
    pushHistory(f);
  }

  $('sim-time').textContent = f.time.toFixed(1);
  $('rtf').textContent = snap.realTimeFactor.toFixed(2);
  $('btn-pause').textContent = snap.paused ? 'Пуск' : 'Пауза';
  $('btn-pause').classList.toggle('on', snap.paused);
  const err = $('error');
  err.hidden = !snap.error;
  err.textContent = snap.error ? `Помилка моделі: ${snap.error}` : '';
}

function pushTrail(f) {
  const last = state.trail[state.trail.length - 1];
  if (!last || Math.hypot(f.north - last[0], f.east - last[1], f.up - last[2]) > 0.5) {
    state.trail.push([f.north, f.east, f.up]);
    if (state.trail.length > TRAIL_MAX) state.trail.shift();
  }
}

function standardChannels(f) {
  return {
    'висота, м': f.up,
    'повітряна швидкість, м/с': f.airspeed,
    'вертикальна швидкість, м/с': f.verticalSpeed,
    'крен, °': f.roll,
    'тангаж, °': f.pitch,
    'курс, °': f.yaw,
    'північ, м': f.north,
    'схід, м': f.east,
  };
}

function pushHistory(f) {
  const h = state.history;
  h.t.push(f.time);
  const all = { ...standardChannels(f), ...f.channels };
  for (const [k, v] of Object.entries(all)) {
    let s = h.series.get(k);
    if (!s) { s = new Array(h.t.length - 1).fill(NaN); h.series.set(k, s); }
    s.push(v);
  }
  for (const [k, s] of h.series) if (s.length < h.t.length) s.push(NaN);
  while (h.t.length > 2 && h.t[h.t.length - 1] - h.t[0] > HISTORY_SECONDS) {
    h.t.shift();
    for (const s of h.series.values()) s.shift();
  }
  refreshChannelLists();
}

// ---------------------------------------------------------------- controls
const axes = ['throttle', 'pitch', 'roll', 'yaw'];

function syncSliders() {
  for (const a of axes) {
    $(`ax-${a}`).value = state.controls[a];
    $(`out-${a}`).textContent = state.controls[a].toFixed(3);
  }
}

function setControl(axis, value) {
  const min = axis === 'throttle' ? 0 : -1;
  state.controls[axis] = Math.min(1, Math.max(min, value));
  $(`ax-${axis}`).value = state.controls[axis];
  $(`out-${axis}`).textContent = state.controls[axis].toFixed(3);
  sendControls();
}

function sendControls() {
  send({ type: 'controls', ...state.controls });
}

for (const a of axes) {
  $(`ax-${a}`).addEventListener('input', (e) => setControl(a, parseFloat(e.target.value)));
}

$('btn-pause').onclick = () => send({ type: state.snap?.paused ? 'resume' : 'pause' });
$('btn-step').onclick = () => send({ type: 'step', value: 1 });
$('btn-reset').onclick = () => send({ type: 'reset' });
$('speed').onchange = (e) => send({ type: 'speed', value: parseFloat(e.target.value) });

// Keyboard: held keys move the axes at a fixed rate (like trimming), C centres.
const held = new Set();
const keyAxis = {
  ArrowUp: ['pitch', +1], ArrowDown: ['pitch', -1],
  ArrowRight: ['roll', +1], ArrowLeft: ['roll', -1],
  KeyE: ['yaw', +1], KeyQ: ['yaw', -1],
  KeyW: ['throttle', +1], KeyS: ['throttle', -1],
};
window.addEventListener('keydown', (e) => {
  if (e.target.tagName === 'SELECT' || e.target.tagName === 'INPUT' && e.target.type !== 'range') return;
  if (keyAxis[e.code]) { held.add(e.code); e.preventDefault(); }
  if (e.code === 'Space') { $('btn-pause').click(); e.preventDefault(); }
  if (e.code === 'KeyC') { for (const a of ['pitch', 'roll', 'yaw']) setControl(a, 0); }
});
window.addEventListener('keyup', (e) => held.delete(e.code));
window.addEventListener('blur', () => held.clear());

function updateKeyboard(dt) {
  for (const code of held) {
    const [axis, dir] = keyAxis[code];
    const rate = axis === 'throttle' ? 0.25 : 0.6;  // per second
    setControl(axis, state.controls[axis] + dir * rate * dt);
  }
}

// Joystick / RC transmitter (TX12 etc.): mapping, calibration and switches live in input.js.
const input = new InputMapper((cmd) => {
  if (cmd === 'pause') $('btn-pause').click();
  else if (cmd === 'reset') send({ type: 'reset' });
  else if (cmd.startsWith('action:')) send({ type: 'action', id: cmd.slice(7) });
});
const inputPanel = mountInputPanel($('input-panel'), input, () => state.info?.actions ?? []);

function updateInput(dt) {
  if (input.poll(state.controls, dt)) {
    syncSliders();
    sendControls();
  }
  inputPanel.update(state.controls);
}

setInterval(sendControls, 200);  // heartbeat keeps the server in sync after reconnects

// ---------------------------------------------------------------- 3D scene
const canvas = $('scene');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x9cc6e8);
scene.fog = new THREE.Fog(0x9cc6e8, 400, 4000);

const camera = new THREE.PerspectiveCamera(55, 1, 0.1, 10000);
scene.add(new THREE.HemisphereLight(0xdfefff, 0x4a5a3a, 1.1));
const sun = new THREE.DirectionalLight(0xffffff, 1.6);
sun.position.set(300, 600, 200);
scene.add(sun);

// Ground: a large plane plus a grid that follows the vehicle (infinite look).
const ground = new THREE.Mesh(
  new THREE.PlaneGeometry(20000, 20000),
  new THREE.MeshLambertMaterial({ color: 0x6f8f4e }));
ground.rotation.x = -Math.PI / 2;
scene.add(ground);
const grid = new THREE.GridHelper(2000, 100, 0x3f5530, 0x587340);
grid.position.y = 0.02;
scene.add(grid);

// Start point: helipad / runway marking at the origin.
const pad = new THREE.Group();
const padRing = new THREE.Mesh(new THREE.RingGeometry(6, 7, 48), new THREE.MeshBasicMaterial({ color: 0xffffff }));
padRing.rotation.x = -Math.PI / 2;
pad.add(padRing);
const runway = new THREE.Mesh(new THREE.PlaneGeometry(20, 600), new THREE.MeshLambertMaterial({ color: 0x444a50 }));
runway.rotation.x = -Math.PI / 2;
runway.position.set(0, 0.01, -280);
pad.add(runway);
pad.position.y = 0.03;
scene.add(pad);

// Trail.
const trailGeom = new THREE.BufferGeometry();
trailGeom.setAttribute('position', new THREE.BufferAttribute(new Float32Array(TRAIL_MAX * 3), 3));
const trailLine = new THREE.Line(trailGeom, new THREE.LineBasicMaterial({ color: 0xff3366 }));
trailLine.frustumCulled = false;
scene.add(trailLine);

// Shadow dot under the vehicle.
const shadow = new THREE.Mesh(new THREE.CircleGeometry(3, 24),
  new THREE.MeshBasicMaterial({ color: 0x000000, transparent: true, opacity: 0.25 }));
shadow.rotation.x = -Math.PI / 2;
scene.add(shadow);

let vehicle = null;
let rotor = null;
let tailRotor = null;

// Vehicle meshes, nose along -Z, right along +X, up along +Y; sizes in metres.
function buildVehicle(kind) {
  if (vehicle) scene.remove(vehicle);
  vehicle = new THREE.Group();
  const body = new THREE.MeshStandardMaterial({ color: 0xe8edf0, roughness: 0.6 });
  const dark = new THREE.MeshStandardMaterial({ color: 0x2c3e50, roughness: 0.5 });
  const accent = new THREE.MeshStandardMaterial({ color: 0xff7a1a, roughness: 0.5 });

  if (kind === 'helicopter') {
    const cabin = new THREE.Mesh(new THREE.SphereGeometry(1, 24, 16), body);
    cabin.scale.set(1.2, 1.2, 2.4);
    vehicle.add(cabin);
    const glass = new THREE.Mesh(new THREE.SphereGeometry(0.9, 20, 12, 0, Math.PI * 2, 0, Math.PI / 2), dark);
    glass.scale.set(1.15, 0.9, 1.4);
    glass.rotation.x = -Math.PI / 2.4;
    glass.position.set(0, 0.2, -1.2);
    vehicle.add(glass);
    const boom = new THREE.Mesh(new THREE.CylinderGeometry(0.18, 0.35, 5, 12), body);
    boom.rotation.x = Math.PI / 2;
    boom.position.set(0, 0.3, 4.2);
    vehicle.add(boom);
    const fin = new THREE.Mesh(new THREE.BoxGeometry(0.1, 1.4, 0.8), accent);
    fin.position.set(0, 0.9, 6.4);
    vehicle.add(fin);
    for (const x of [-0.9, 0.9]) {
      const skid = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.06, 3.6, 8), dark);
      skid.rotation.x = Math.PI / 2;
      skid.position.set(x, -1.0, 0);
      vehicle.add(skid);
    }
    const mast = new THREE.Mesh(new THREE.CylinderGeometry(0.1, 0.1, 0.8, 8), dark);
    mast.position.y = 1.4;
    vehicle.add(mast);
    rotor = new THREE.Group();
    for (let i = 0; i < 4; i++) {
      const blade = new THREE.Mesh(new THREE.BoxGeometry(0.28, 0.04, 4.9), dark);
      blade.position.z = 2.45;
      const arm = new THREE.Group();
      arm.rotation.y = (i * Math.PI) / 2;
      arm.add(blade);
      rotor.add(arm);
    }
    rotor.position.y = 1.8;
    vehicle.add(rotor);
    tailRotor = new THREE.Group();
    for (let i = 0; i < 2; i++) {
      const b = new THREE.Mesh(new THREE.BoxGeometry(0.05, 0.12, 1.9), dark);
      b.rotation.x = (i * Math.PI) / 2;
      tailRotor.add(b);
    }
    tailRotor.position.set(0.25, 1.0, 6.6);
    vehicle.add(tailRotor);
  } else {
    const fuse = new THREE.Mesh(new THREE.CylinderGeometry(0.45, 0.25, 7.5, 16), body);
    fuse.rotation.x = Math.PI / 2;
    vehicle.add(fuse);
    const nose = new THREE.Mesh(new THREE.ConeGeometry(0.45, 0.8, 16), dark);
    nose.rotation.x = -Math.PI / 2;
    nose.position.z = -4.1;
    vehicle.add(nose);
    const wing = new THREE.Mesh(new THREE.BoxGeometry(10.9, 0.12, 1.5), body);
    wing.position.set(0, 0.45, -0.8);
    vehicle.add(wing);
    for (const x of [-5.3, 5.3]) {
      const tip = new THREE.Mesh(new THREE.BoxGeometry(0.3, 0.13, 1.5), accent);
      tip.position.set(x, 0.45, -0.8);
      vehicle.add(tip);
    }
    const stab = new THREE.Mesh(new THREE.BoxGeometry(3.4, 0.08, 0.9), body);
    stab.position.set(0, 0.1, 3.4);
    vehicle.add(stab);
    const fin = new THREE.Mesh(new THREE.BoxGeometry(0.08, 1.4, 1.0), accent);
    fin.position.set(0, 0.8, 3.4);
    vehicle.add(fin);
    rotor = new THREE.Group();
    const prop = new THREE.Mesh(new THREE.BoxGeometry(1.9, 0.12, 0.05), dark);
    rotor.add(prop);
    rotor.position.z = -4.55;
    vehicle.add(rotor);
    tailRotor = null;
  }
  scene.add(vehicle);
}

// NED-ish frame -> three.js: x = east, y = up, z = -north.
const toScene = (north, east, up) => new THREE.Vector3(east, up, -north);

// Camera modes.
const orbit = { az: 200 * DEG, el: 15 * DEG, dist: 40, dragging: false, x: 0, y: 0 };
canvas.addEventListener('pointerdown', (e) => { orbit.dragging = true; orbit.x = e.clientX; orbit.y = e.clientY; canvas.setPointerCapture(e.pointerId); });
canvas.addEventListener('pointerup', () => { orbit.dragging = false; });
canvas.addEventListener('pointermove', (e) => {
  if (!orbit.dragging) return;
  orbit.az -= (e.clientX - orbit.x) * 0.006;
  orbit.el = Math.min(1.45, Math.max(-0.1, orbit.el + (e.clientY - orbit.y) * 0.006));
  orbit.x = e.clientX; orbit.y = e.clientY;
  if ($('camera').value !== 'orbit') $('camera').value = 'orbit';
});
canvas.addEventListener('wheel', (e) => {
  orbit.dist = Math.min(1500, Math.max(8, orbit.dist * (e.deltaY > 0 ? 1.1 : 0.9)));
  e.preventDefault();
}, { passive: false });

const camPos = new THREE.Vector3(0, 10, 40);

function updateScene(dt) {
  const f = state.snap?.frame;
  if (!f || !vehicle) return;
  const p = toScene(f.north, f.east, f.up);
  vehicle.position.copy(p);
  // Aerospace Z-Y-X (yaw, pitch, roll) == three.js intrinsic 'YXZ' with these signs.
  vehicle.rotation.set(f.pitch * DEG, -f.yaw * DEG, -f.roll * DEG, 'YXZ');

  const spin = state.snap.paused ? 0 : (state.info?.kind === 'helicopter' ? 28 : 120) * (0.3 + state.controls.throttle);
  if (rotor) rotor.rotation[state.info?.kind === 'helicopter' ? 'y' : 'z'] += spin * dt;
  if (tailRotor) tailRotor.rotation.x += spin * 5 * dt;

  shadow.position.set(p.x, 0.05, p.z);
  shadow.scale.setScalar(Math.max(0.3, 1 - f.up / 300));

  // Infinite-grid illusion: move the grid in whole cells under the vehicle.
  grid.position.x = Math.round(p.x / 20) * 20;
  grid.position.z = Math.round(p.z / 20) * 20;

  const n = state.trail.length;
  const arr = trailGeom.attributes.position.array;
  for (let i = 0; i < n; i++) {
    const [tn, te, tu] = state.trail[i];
    arr[i * 3] = te; arr[i * 3 + 1] = tu; arr[i * 3 + 2] = -tn;
  }
  trailGeom.setDrawRange(0, n);
  trailGeom.attributes.position.needsUpdate = true;

  const mode = $('camera').value;
  let target;
  if (mode === 'chase') {
    const back = new THREE.Vector3(-Math.sin(f.yaw * DEG), 0, Math.cos(f.yaw * DEG));  // opposite to the nose
    target = p.clone().addScaledVector(back, state.info?.kind === 'helicopter' ? 28 : 32).add(new THREE.Vector3(0, 8, 0));
  } else if (mode === 'orbit') {
    target = p.clone().add(new THREE.Vector3(
      orbit.dist * Math.cos(orbit.el) * Math.sin(orbit.az),
      orbit.dist * Math.sin(orbit.el),
      orbit.dist * Math.cos(orbit.el) * Math.cos(orbit.az)));
  } else {
    target = new THREE.Vector3(25, 2, 35);
  }
  if (target.y < 1) target.y = 1;
  camPos.lerp(target, mode === 'ground' ? 1 : Math.min(1, dt * 4));
  camera.position.copy(camPos);
  camera.lookAt(p);

  $('readout').innerHTML =
    `Висота <b>${f.up.toFixed(1)}</b> м<br>` +
    `Швидкість <b>${(f.airspeed * 3.6).toFixed(1)}</b> км/год (${f.airspeed.toFixed(1)} м/с)<br>` +
    `Вертикальна <b>${f.verticalSpeed.toFixed(2)}</b> м/с<br>` +
    `Курс <b>${(((f.yaw % 360) + 360) % 360).toFixed(0)}</b>°  Крен <b>${f.roll.toFixed(1)}</b>°  Тангаж <b>${f.pitch.toFixed(1)}</b>°<br>` +
    `${f.onGround ? '<span style="color:var(--accent-2)">на землі</span>' : 'у повітрі'}`;
}

function resize() {
  const r = canvas.parentElement.getBoundingClientRect();
  renderer.setSize(r.width, r.height, false);
  camera.aspect = r.width / Math.max(1, r.height);
  camera.updateProjectionMatrix();
}
new ResizeObserver(resize).observe(canvas.parentElement);

// ---------------------------------------------------------------- PFD
const pfd = $('pfd');
const pctx = pfd.getContext('2d');

function drawPfd() {
  const f = state.snap?.frame;
  const W = pfd.width, H = pfd.height;
  const c = pctx;
  c.save();
  c.clearRect(0, 0, W, H);
  if (!f) { c.restore(); return; }
  const cx = W / 2, cy = H / 2 - 10;
  const pxPerDeg = 4;

  // Attitude ball.
  c.save();
  c.beginPath();
  c.rect(60, 10, W - 120, H - 60);
  c.clip();
  c.translate(cx, cy);
  c.rotate(-f.roll * DEG);
  c.translate(0, f.pitch * pxPerDeg);
  c.fillStyle = '#3d7fc4'; c.fillRect(-600, -1200, 1200, 1200);
  c.fillStyle = '#8a5a2b'; c.fillRect(-600, 0, 1200, 1200);
  c.strokeStyle = '#fff'; c.lineWidth = 2;
  c.beginPath(); c.moveTo(-600, 0); c.lineTo(600, 0); c.stroke();
  c.lineWidth = 1.5; c.fillStyle = '#fff'; c.font = '11px system-ui'; c.textAlign = 'left';
  for (let d = -40; d <= 40; d += 5) {
    if (d === 0) continue;
    const y = -d * pxPerDeg, w = d % 10 === 0 ? 40 : 20;
    c.beginPath(); c.moveTo(-w, y); c.lineTo(w, y); c.stroke();
    if (d % 10 === 0) { c.fillText(String(d), w + 4, y + 4); }
  }
  c.restore();

  // Fixed aircraft symbol.
  c.strokeStyle = '#ffb547'; c.lineWidth = 4;
  c.beginPath(); c.moveTo(cx - 60, cy); c.lineTo(cx - 20, cy); c.lineTo(cx - 10, cy + 10);
  c.moveTo(cx + 60, cy); c.lineTo(cx + 20, cy); c.lineTo(cx + 10, cy + 10); c.stroke();
  c.fillStyle = '#ffb547'; c.fillRect(cx - 3, cy - 3, 6, 6);

  // Roll scale.
  c.save();
  c.translate(cx, cy);
  c.strokeStyle = '#fff'; c.lineWidth = 1.5;
  for (const a of [-60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60]) {
    c.save(); c.rotate(a * DEG);
    c.beginPath(); c.moveTo(0, -110); c.lineTo(0, a % 30 === 0 ? -120 : -115); c.stroke();
    c.restore();
  }
  c.rotate(-f.roll * DEG);
  c.fillStyle = '#ffb547';
  c.beginPath(); c.moveTo(0, -108); c.lineTo(-6, -98); c.lineTo(6, -98); c.closePath(); c.fill();
  c.restore();

  // Speed and altitude boxes.
  const box = (x, label, value, unit) => {
    c.fillStyle = '#0f1418'; c.fillRect(x, cy - 22, 56, 44);
    c.strokeStyle = '#4fb3ff'; c.lineWidth = 1.5; c.strokeRect(x, cy - 22, 56, 44);
    c.fillStyle = '#8a9aa6'; c.font = '10px system-ui'; c.textAlign = 'center';
    c.fillText(label, x + 28, cy - 9);
    c.fillStyle = '#fff'; c.font = 'bold 15px system-ui';
    c.fillText(value, x + 28, cy + 8);
    c.fillStyle = '#8a9aa6'; c.font = '10px system-ui';
    c.fillText(unit, x + 28, cy + 19);
  };
  box(2, 'ШВИДК', (f.airspeed * 3.6).toFixed(0), 'км/год');
  box(W - 58, 'ВИСОТА', f.up.toFixed(0), 'м');

  // Vertical speed bar.
  const vs = Math.max(-10, Math.min(10, f.verticalSpeed));
  c.fillStyle = vs >= 0 ? '#4cc38a' : '#ff6b6b';
  c.fillRect(W - 14, cy, 8, -vs * 8);
  c.strokeStyle = '#2b3843'; c.strokeRect(W - 14, cy - 80, 8, 160);

  // Heading tape.
  const hy = H - 38;
  c.fillStyle = '#0f1418'; c.fillRect(60, hy, W - 120, 34);
  c.save();
  c.beginPath(); c.rect(60, hy, W - 120, 34); c.clip();
  c.strokeStyle = '#fff'; c.fillStyle = '#fff'; c.font = '11px system-ui'; c.textAlign = 'center';
  const hdg = ((f.yaw % 360) + 360) % 360;
  for (let d = Math.floor(hdg / 10) * 10 - 60; d <= hdg + 60; d += 10) {
    const x = cx + (d - hdg) * 3;
    c.beginPath(); c.moveTo(x, hy); c.lineTo(x, hy + (d % 30 === 0 ? 10 : 6)); c.stroke();
    if (d % 30 === 0) {
      const v = ((d % 360) + 360) % 360;
      c.fillText({ 0: 'N', 90: 'E', 180: 'S', 270: 'W' }[v] ?? String(v / 10), x, hy + 24);
    }
  }
  c.restore();
  c.fillStyle = '#ffb547';
  c.beginPath(); c.moveTo(cx, hy + 2); c.lineTo(cx - 6, hy - 6); c.lineTo(cx + 6, hy - 6); c.closePath(); c.fill();
  c.restore();
}

// ---------------------------------------------------------------- map
const map = $('map');
const mctx = map.getContext('2d');

function drawMap() {
  const W = map.width, H = map.height, c = mctx;
  c.fillStyle = '#171e24'; c.fillRect(0, 0, W, H);
  const f = state.snap?.frame;
  if (!f) return;
  const pts = state.trail;
  let minN = Math.min(0, f.north), maxN = Math.max(0, f.north), minE = Math.min(0, f.east), maxE = Math.max(0, f.east);
  for (const [n, e] of pts) { minN = Math.min(minN, n); maxN = Math.max(maxN, n); minE = Math.min(minE, e); maxE = Math.max(maxE, e); }
  const span = Math.max(100, (maxN - minN) * 1.2, (maxE - minE) * 1.2 * H / W);
  const scale = (H - 30) / span;
  const cN = (minN + maxN) / 2, cE = (minE + maxE) / 2;
  const X = (e) => W / 2 + (e - cE) * scale;
  const Y = (n) => H / 2 - (n - cN) * scale;

  // Grid with a "nice" step.
  const raw = span / 5, mag = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 5, 10].map((k) => k * mag).find((s) => s >= raw);
  c.strokeStyle = '#243039'; c.lineWidth = 1;
  for (let e = Math.floor((cE - span) / step) * step; e < cE + span; e += step) { c.beginPath(); c.moveTo(X(e), 0); c.lineTo(X(e), H); c.stroke(); }
  for (let n = Math.floor((cN - span) / step) * step; n < cN + span; n += step) { c.beginPath(); c.moveTo(0, Y(n)); c.lineTo(W, Y(n)); c.stroke(); }

  c.fillStyle = '#fff'; c.beginPath(); c.arc(X(0), Y(0), 4, 0, Math.PI * 2); c.fill();
  c.strokeStyle = '#ff3366'; c.lineWidth = 2; c.beginPath();
  pts.forEach(([n, e], i) => (i ? c.lineTo(X(e), Y(n)) : c.moveTo(X(e), Y(n))));
  c.stroke();

  c.save();
  c.translate(X(f.east), Y(f.north));
  c.rotate(f.yaw * DEG);
  c.fillStyle = '#ffb547';
  c.beginPath(); c.moveTo(0, -10); c.lineTo(-6, 7); c.lineTo(0, 3); c.lineTo(6, 7); c.closePath(); c.fill();
  c.restore();

  c.fillStyle = '#8a9aa6'; c.font = '11px system-ui'; c.textAlign = 'left';
  c.fillText(`клітинка ${step} м · N ↑`, 8, H - 8);
}

// ---------------------------------------------------------------- charts
const chartDefs = [
  { canvas: $('chart1'), a: $('ch1'), b: $('ch1b'), defA: 'висота, м', defB: 'вертикальна швидкість, м/с' },
  { canvas: $('chart2'), a: $('ch2'), b: $('ch2b'), defA: 'крен, °', defB: 'тангаж, °' },
];
let knownChannels = '';

function refreshChannelLists() {
  const names = [...state.history.series.keys()];
  const sig = names.join('|');
  if (sig === knownChannels) return;
  knownChannels = sig;
  for (const d of chartDefs) {
    for (const [sel, def, allowNone] of [[d.a, d.defA, false], [d.b, d.defB, true]]) {
      const cur = sel.value || def;
      sel.replaceChildren();
      if (allowNone) sel.append(new Option('—', ''));
      for (const n of names) sel.append(new Option(n, n));
      sel.value = names.includes(cur) ? cur : (allowNone ? '' : names[0]);
    }
  }
}

function drawChart(d) {
  const cv = d.canvas;
  const dpr = window.devicePixelRatio || 1;
  const w = cv.clientWidth, h = cv.clientHeight;
  if (cv.width !== Math.round(w * dpr)) { cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr); }
  const c = cv.getContext('2d');
  c.setTransform(dpr, 0, 0, dpr, 0, 0);
  c.fillStyle = '#171e24'; c.fillRect(0, 0, w, h);
  const t = state.history.t;
  if (t.length < 2) return;
  const series = [[d.a.value, '#4fb3ff'], [d.b.value, '#ffb547']].filter(([n]) => n && state.history.series.has(n));
  let lo = Infinity, hi = -Infinity;
  for (const [n] of series) for (const v of state.history.series.get(n)) if (Number.isFinite(v)) { lo = Math.min(lo, v); hi = Math.max(hi, v); }
  if (!Number.isFinite(lo)) return;
  if (hi - lo < 1e-6) { lo -= 1; hi += 1; }
  const pad = (hi - lo) * 0.1; lo -= pad; hi += pad;
  const L = 50, R = 8, T = 6, B = 18;
  const t0 = Math.max(0, t[t.length - 1] - HISTORY_SECONDS), t1 = t0 + HISTORY_SECONDS;
  const X = (x) => L + ((x - t0) / (t1 - t0)) * (w - L - R);
  const Y = (y) => T + (1 - (y - lo) / (hi - lo)) * (h - T - B);

  c.strokeStyle = '#243039'; c.fillStyle = '#8a9aa6'; c.font = '10px system-ui'; c.lineWidth = 1;
  c.textAlign = 'right';
  for (let i = 0; i <= 4; i++) {
    const v = lo + ((hi - lo) * i) / 4;
    c.beginPath(); c.moveTo(L, Y(v)); c.lineTo(w - R, Y(v)); c.stroke();
    c.fillText(Math.abs(v) >= 1000 ? v.toFixed(0) : v.toPrecision(3), L - 4, Y(v) + 3);
  }
  c.textAlign = 'center';
  for (let s = Math.ceil(t0 / 10) * 10; s <= t1; s += 10) c.fillText(`${s.toFixed(0)} с`, X(s), h - 4);

  for (const [n, color] of series) {
    const v = state.history.series.get(n);
    c.strokeStyle = color; c.lineWidth = 1.6; c.beginPath();
    let pen = false;
    for (let i = 0; i < t.length; i++) {
      if (!Number.isFinite(v[i])) { pen = false; continue; }
      if (pen) c.lineTo(X(t[i]), Y(v[i])); else { c.moveTo(X(t[i]), Y(v[i])); pen = true; }
    }
    c.stroke();
  }
}

// ---------------------------------------------------------------- main loop
let lastFrame = performance.now();
function frame(now) {
  const dt = Math.min(0.1, (now - lastFrame) / 1000);
  lastFrame = now;
  updateKeyboard(dt);
  updateInput(dt);
  updateScene(dt);
  renderer.render(scene, camera);
  drawPfd();
  drawMap();
  chartDefs.forEach(drawChart);
  requestAnimationFrame(frame);
}

syncSliders();
resize();
await loadInfo();
connect();
requestAnimationFrame(frame);
