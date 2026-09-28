import { step } from './movement.js';
import { Renderer } from './render.js';
import { sfx, unlock, isMuted, setMuted } from './sound.js';

const $ = id => document.getElementById(id);

const INTERP_DELAY_MS = 100; // render other players this far in the past so we can interpolate

const state = {
  connection: null,
  name: '',
  roomId: null,
  roomName: '',
  mode: 'ffa',
  cfg: null,
  map: null,
  myId: 0,
  roster: new Map(),   // id -> {name, hue, team, bot}
  me: { x: 0, y: 0, a: 0 },
  server: null,        // my last PlayerState from the server
  pending: [],         // commands sent but not yet acknowledged
  seq: 0,
  snaps: [],           // recent snapshots for interpolation
  phase: 'play',
  time: 0,
  teamScores: [0, 0],
  joining: false,
  joinQueue: [],
  playing: false,
  showMap: true,
};

const keys = new Set();
const renderer = new Renderer($('view'), $('minimap'));

// ---- screens -------------------------------------------------------------------------------

function showLobby() {
  state.playing = false;
  $('game').hidden = true;
  $('lobby').hidden = false;
  keys.clear();
}

function showGame() {
  $('lobby').hidden = true;
  $('game').hidden = false;
}

function lobbyMessage(text) { $('lobby-msg').textContent = text ?? ''; }

function banner(text) {
  const el = $('banner');
  el.hidden = !text;
  el.textContent = text ?? '';
}

// ---- connection ----------------------------------------------------------------------------

async function connect() {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl('hub') // relative on purpose: works behind a reverse proxy sub-path
    .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
    .build();
  state.connection = connection;

  connection.on('Snapshot', onSnapshot);
  connection.on('Rooms', renderRooms);

  connection.onreconnecting(() => banner('Verbindung unterbrochen ...'));
  connection.onreconnected(async () => {
    banner('');
    // A new connection id means the server forgot us: go back to the room we were in.
    if (state.playing && state.roomId) {
      state.playing = false;
      if (!await joinRoom(state.roomId)) showLobby();
    }
    await refreshRooms();
  });
  connection.onclose(() => {
    banner('Verbindung verloren. Seite neu laden.');
    state.playing = false;
  });

  await connection.start();
  await refreshRooms();
}

async function refreshRooms() {
  try { renderRooms(await state.connection.invoke('GetRooms')); } catch { /* next push fixes it */ }
}

// ---- lobby ---------------------------------------------------------------------------------

function renderRooms(rooms) {
  const ul = $('rooms');
  ul.replaceChildren();

  for (const r of rooms) {
    const li = document.createElement('li');

    const info = document.createElement('div');
    info.className = 'room-info';
    const name = document.createElement('strong');
    name.textContent = r.name;
    const meta = document.createElement('span');
    const modeText = r.mode === 'teams' ? 'Teams' : 'Jeder gegen jeden';
    const bots = r.bots ? ` · ${r.bots} Bot${r.bots === 1 ? '' : 's'}` : '';
    const round = r.phase === 'over' ? ' · Ergebnis' : '';
    meta.textContent = `${modeText} · ${r.players}/${r.maxPlayers} Spieler${bots} · ${r.roundSeconds / 60} min${round}`;
    info.append(name, meta);

    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'primary';
    btn.textContent = 'Beitreten';
    btn.addEventListener('click', () => enter(r.id));

    li.append(info, btn);
    ul.append(li);
  }
}

function readName() {
  const name = $('name').value.trim();
  try { localStorage.setItem('midimaze.name', name); } catch { /* private mode */ }
  return name;
}

async function enter(roomId) {
  unlock();
  state.name = readName();
  lobbyMessage('');
  if (!await joinRoom(roomId)) await refreshRooms();
}

async function joinRoom(roomId) {
  state.joining = true;
  state.joinQueue = [];
  try {
    const res = await state.connection.invoke('Join', roomId, state.name);
    if (!res.welcome) {
      lobbyMessage(res.error ?? 'Beitritt nicht möglich.');
      return false;
    }

    const w = res.welcome;
    state.cfg = w.config;
    state.map = w.map;
    state.myId = w.id;
    state.roomId = w.roomId;
    state.roomName = w.roomName;
    state.mode = w.mode;
    state.roster = rosterMap(w.roster);
    state.pending = [];
    state.snaps = [];
    state.server = null;
    state.phase = 'play';
    state.teamScores = [0, 0];
    state.playing = true;

    $('room-title').textContent = w.roomName;
    $('teams').hidden = w.mode !== 'teams';
    $('over').hidden = true;
    $('feed').replaceChildren();
    $('sound').textContent = isMuted() ? '🔇' : '🔊';
    showGame();

    // snapshots that arrived while the join call was still in flight
    for (const snap of state.joinQueue) applySnapshot(snap);
    return true;
  } catch (err) {
    console.error(err);
    lobbyMessage('Beitritt fehlgeschlagen.');
    return false;
  } finally {
    state.joining = false;
    state.joinQueue = [];
  }
}

async function leaveRoom() {
  state.playing = false;
  try { await state.connection.invoke('Leave'); } catch { /* connection may be gone */ }
  showLobby();
  await refreshRooms();
}

function rosterMap(list) {
  return new Map(list.map(p => [p.id, p]));
}

// ---- snapshots -----------------------------------------------------------------------------

function onSnapshot(snap) {
  if (state.joining) { state.joinQueue.push(snap); return; }
  if (state.playing) applySnapshot(snap);
}

function applySnapshot(snap) {
  const prev = state.snaps[state.snaps.length - 1];
  const wasHp = state.server?.hp;

  if (snap.roster) state.roster = rosterMap(snap.roster);
  if (snap.map) {
    // new round, new maze
    state.map = snap.map;
    state.pending = [];
    state.snaps = [];
    $('feed').replaceChildren();
  }

  if (snap.phase !== state.phase) {
    if (snap.phase === 'over') sfx.roundEnd(); else if (state.server) sfx.roundStart();
  }
  state.phase = snap.phase;
  state.time = snap.time;
  state.teamScores = snap.teamScores;

  state.snaps.push({
    t: performance.now(),
    players: new Map(snap.players.map(p => [p.id, p])),
    shots: new Map(snap.shots.map(s => [s.id, s])),
  });
  if (state.snaps.length > 12) state.snaps.shift();

  const mine = snap.players.find(p => p.id === state.myId);
  if (mine) {
    state.server = mine;
    while (state.pending.length && state.pending[0].seq <= mine.ack) state.pending.shift();

    // Reconciliation: start from the server's state and replay what it has not processed yet.
    const s = { x: mine.x, y: mine.y, a: mine.a };
    if (mine.hp > 0 && snap.phase === 'play') {
      for (const c of state.pending) step(state.map, state.cfg, s, c.f, c.s, c.t);
    } else {
      state.pending = [];
    }
    state.me = s;

    if (wasHp === 0 && mine.hp > 0 && !snap.map) sfx.respawn();
  }

  // sounds for shots that appeared since the last snapshot
  if (prev) {
    for (const s of snap.shots) {
      if (prev.shots.has(s.id)) continue;
      const d = Math.hypot(s.x - state.me.x, s.y - state.me.y);
      sfx.shoot(Math.max(0.08, 1 - d / 14));
    }
  }

  for (const ev of snap.events) onHit(ev, snap);
}

function nameOf(id) {
  return state.roster.get(id)?.name ?? '?';
}

function onHit(ev, snap) {
  if (ev.victimId === state.myId) {
    flash('hurt');
    ev.killed ? sfx.death() : sfx.hurt();
  } else if (ev.shooterId === state.myId) {
    flash('hit');
    sfx.hit();
  } else if (ev.killed) {
    const v = snap.players.find(p => p.id === ev.victimId);
    if (v) sfx.kill(Math.max(0.1, 1 - Math.hypot(v.x - state.me.x, v.y - state.me.y) / 14));
  }

  if (ev.killed) {
    const li = document.createElement('li');
    li.textContent = ev.shooterId === state.myId ? `Du hast ${nameOf(ev.victimId)} erwischt`
      : ev.victimId === state.myId ? `${nameOf(ev.shooterId)} hat dich erwischt`
        : `${nameOf(ev.shooterId)} → ${nameOf(ev.victimId)}`;
    const feed = $('feed');
    feed.prepend(li);
    while (feed.children.length > 5) feed.lastChild.remove();
    setTimeout(() => li.remove(), 6000);
  }
}

function flash(kind) {
  const el = $('flash');
  el.className = '';
  void el.offsetWidth; // restart the CSS animation
  el.className = kind;
}

// ---- interpolation -------------------------------------------------------------------------

function lerp(a, b, t) { return a + (b - a) * t; }

function lerpAngle(a, b, t) {
  let d = b - a;
  while (d > Math.PI) d -= 2 * Math.PI;
  while (d < -Math.PI) d += 2 * Math.PI;
  return a + d * t;
}

function interpolated() {
  const snaps = state.snaps;
  if (snaps.length === 0) return { players: [], shots: [] };

  const rt = performance.now() - INTERP_DELAY_MS;
  let a = snaps[0], b = snaps[0];
  for (let i = 0; i < snaps.length; i++) {
    b = snaps[i];
    if (snaps[i].t >= rt) { a = snaps[Math.max(0, i - 1)]; break; }
    a = b;
  }
  const t = b.t > a.t ? Math.min(1, Math.max(0, (rt - a.t) / (b.t - a.t))) : 1;

  const players = [];
  for (const [id, pb] of b.players) {
    if (id === state.myId) continue;
    const pa = a.players.get(id) ?? pb;
    // don't glide across the map when someone respawned between the two snapshots
    const jumped = Math.hypot(pb.x - pa.x, pb.y - pa.y) > 2;
    const from = jumped ? pb : pa;
    const info = state.roster.get(id);
    players.push({
      id, hue: info?.hue ?? 0,
      x: lerp(from.x, pb.x, t), y: lerp(from.y, pb.y, t), a: lerpAngle(from.a, pb.a, t),
      alive: pb.hp > 0,
    });
  }

  const shots = [];
  for (const [id, sb] of b.shots) {
    const sa = a.shots.get(id) ?? sb;
    shots.push({ x: lerp(sa.x, sb.x, t), y: lerp(sa.y, sb.y, t) });
  }

  return { players, shots };
}

// ---- input + fixed-step loop ---------------------------------------------------------------

const KEY_MAP = {
  ArrowUp: 'fwd', KeyW: 'fwd', ArrowDown: 'back', KeyS: 'back',
  ArrowLeft: 'left', KeyQ: 'left', ArrowRight: 'right', KeyE: 'right',
  KeyA: 'sleft', KeyD: 'sright',
  Space: 'fire', ControlLeft: 'fire', ControlRight: 'fire',
};

function toggleMute() {
  setMuted(!isMuted());
  $('sound').textContent = isMuted() ? '🔇' : '🔊';
}

window.addEventListener('keydown', e => {
  if (e.target instanceof HTMLInputElement || e.target instanceof HTMLSelectElement) return;
  unlock();
  if (!state.playing) return;

  if (e.repeat && (e.code === 'KeyM' || e.code === 'KeyN' || e.code === 'Escape')) return;
  if (e.code === 'KeyM') { state.showMap = !state.showMap; $('minimap').hidden = !state.showMap; }
  else if (e.code === 'KeyN') toggleMute();
  else if (e.code === 'Escape') { leaveRoom(); return; }

  const action = KEY_MAP[e.code];
  if (!action) return;
  keys.add(action);
  e.preventDefault();
});
window.addEventListener('keyup', e => { const a = KEY_MAP[e.code]; if (a) keys.delete(a); });
window.addEventListener('blur', () => keys.clear());

// on-screen buttons for touch devices; they feed the same key set as the keyboard
if (matchMedia('(pointer: coarse)').matches || 'ontouchstart' in window) {
  $('touch').hidden = false;
  for (const btn of $('touch').querySelectorAll('button')) {
    const key = btn.dataset.key;
    const release = () => { keys.delete(key); btn.classList.remove('down'); };
    btn.addEventListener('pointerdown', e => {
      unlock();
      keys.add(key);
      btn.classList.add('down');
      try { btn.setPointerCapture(e.pointerId); } catch { /* synthetic pointer */ }
      e.preventDefault();
    });
    btn.addEventListener('pointerup', release);
    btn.addEventListener('pointercancel', release);
    btn.addEventListener('lostpointercapture', release);
    btn.addEventListener('contextmenu', e => e.preventDefault());
  }
}

function readInput() {
  return {
    f: (keys.has('fwd') ? 1 : 0) - (keys.has('back') ? 1 : 0),
    s: (keys.has('sright') ? 1 : 0) - (keys.has('sleft') ? 1 : 0),
    t: (keys.has('right') ? 1 : 0) - (keys.has('left') ? 1 : 0),
    fire: keys.has('fire'),
  };
}

function fixedStep() {
  if (state.phase !== 'play') return; // results screen: the server ignores input anyway

  const input = readInput();
  const cmd = { seq: ++state.seq, f: input.f, s: input.s, t: input.t };

  const alive = !state.server || state.server.hp > 0;
  if (alive) {
    step(state.map, state.cfg, state.me, cmd.f, cmd.s, cmd.t);
    state.pending.push(cmd);
    if (state.pending.length > 60) state.pending.shift();
  }

  state.connection.send('Input', {
    seq: cmd.seq, forward: cmd.f, strafe: cmd.s, turn: cmd.t, fire: input.fire,
  }).catch(() => { /* reconnect handler takes over */ });
}

let last = performance.now();
let acc = 0;

function frame(now) {
  requestAnimationFrame(frame);
  if (!state.playing || !state.cfg) { last = now; return; }

  const dt = 1 / state.cfg.tickRate;
  acc = Math.min(acc + (now - last) / 1000, 0.25);
  last = now;
  while (acc >= dt) { fixedStep(); acc -= dt; }

  if (!state.server) return; // wait for the first snapshot (our spawn position)

  const { players, shots } = interpolated();
  renderer.draw(state.map, state.me, players, shots);
  if (state.showMap) renderer.drawMinimap(state.map, state.me, players);
  updateHud(now);
}

// ---- HUD -----------------------------------------------------------------------------------

let lastHud = 0;

function formatTime(seconds) {
  const s = Math.max(0, Math.ceil(seconds));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

function updateHud(now) {
  const sv = state.server;
  const over = state.phase === 'over';

  drawHealthFace(sv.hp / state.cfg.maxHealth);
  $('hpbar').firstElementChild.style.width = `${Math.max(0, sv.hp) / state.cfg.maxHealth * 100}%`;

  const respawn = $('respawn');
  respawn.hidden = sv.hp > 0 || over;
  if (!respawn.hidden) respawn.textContent = `Respawn in ${Math.ceil(sv.respawn)} ...`;

  $('timer').textContent = over ? 'Ergebnis' : formatTime(state.time);
  $('timer').classList.toggle('low', !over && state.time <= 10);

  if (now - lastHud < 200) return;
  lastHud = now;

  $('st-kills').textContent = sv.kills;
  $('st-hits').textContent = sv.hits;
  $('st-score').textContent = sv.score;

  renderScoreboard();
  renderTeams();
  renderResults();
}

function renderTeams() {
  const el = $('teams');
  if (state.mode !== 'teams') return;
  el.replaceChildren();
  const red = document.createElement('b');
  red.className = 'red';
  red.textContent = `ROT ${state.teamScores[0]}`;
  const blue = document.createElement('b');
  blue.className = 'blue';
  blue.textContent = `${state.teamScores[1]} BLAU`;
  el.append(red, ' : ', blue);
}

function sortedPlayers() {
  const latest = state.snaps[state.snaps.length - 1];
  if (!latest) return [];
  return [...latest.players.values()].sort((a, b) => b.score - a.score || b.kills - a.kills || a.deaths - b.deaths);
}

function renderScoreboard() {
  const latest = state.snaps[state.snaps.length - 1];
  const bar = $('scorebar');
  bar.replaceChildren();

  for (const [id, info] of state.roster) {
    const p = latest?.players.get(id);
    const cell = document.createElement('div');
    cell.className = 'cell' + (id === state.myId ? ' me' : '');
    cell.style.setProperty('--hue', info.hue);
    const name = document.createElement('span');
    name.className = 'name';
    name.textContent = (info.bot ? '🤖 ' : '') + info.name;
    const score = document.createElement('span');
    score.className = 'score';
    score.textContent = String(p?.score ?? 0).padStart(3, '0');
    cell.append(name, score);
    bar.append(cell);
  }
}

function renderResults() {
  const box = $('over');
  const over = state.phase === 'over';
  box.hidden = !over;
  if (!over) return;

  const players = sortedPlayers();
  let title;
  if (state.mode === 'teams') {
    const [red, blue] = state.teamScores;
    title = red === blue ? `Unentschieden ${red} : ${blue}`
      : `Team ${red > blue ? 'Rot' : 'Blau'} gewinnt ${Math.max(red, blue)} : ${Math.min(red, blue)}`;
  } else if (players.length > 1 && players[0].score === players[1].score) {
    title = 'Unentschieden';
  } else {
    title = players.length ? `Sieger: ${nameOf(players[0].id)}` : 'Runde beendet';
  }
  $('over-title').textContent = title;

  const table = $('over-table');
  table.replaceChildren();
  const head = table.insertRow();
  for (const h of ['#', 'Name', 'Kills', 'Hits', 'Tode', 'Score']) {
    const th = document.createElement('th');
    th.textContent = h;
    head.append(th);
  }
  players.slice(0, 8).forEach((p, i) => {
    const info = state.roster.get(p.id);
    const row = table.insertRow();
    row.style.setProperty('--hue', info?.hue ?? 0);
    if (p.id === state.myId) row.className = 'me';
    const cells = [i + 1, (info?.bot ? '🤖 ' : '') + (info?.name ?? '?'), p.kills, p.hits, p.deaths, p.score];
    for (const c of cells) row.insertCell().textContent = c;
  });

  $('over-next').textContent = `Nächste Runde in ${Math.ceil(state.time)} s`;
}

const face = $('health').getContext('2d');

function drawHealthFace(frac) {
  const g = face;
  const w = 48;
  g.clearRect(0, 0, w, w);
  g.fillStyle = frac > 0 ? `hsl(${Math.round(15 + 45 * frac)} 95% 55%)` : '#666';
  g.beginPath(); g.arc(24, 24, 22, 0, Math.PI * 2); g.fill();
  g.lineWidth = 2; g.strokeStyle = '#000'; g.stroke();

  g.fillStyle = '#000'; g.strokeStyle = '#000'; g.lineWidth = 3; g.lineCap = 'round';
  if (frac <= 0) {
    for (const ex of [16, 32]) {
      g.beginPath(); g.moveTo(ex - 4, 14); g.lineTo(ex + 4, 22); g.moveTo(ex + 4, 14); g.lineTo(ex - 4, 22); g.stroke();
    }
    g.beginPath(); g.moveTo(15, 35); g.lineTo(33, 35); g.stroke();
    return;
  }
  g.beginPath(); g.ellipse(16, 17, 2.5, 4.5, 0, 0, Math.PI * 2); g.fill();
  g.beginPath(); g.ellipse(32, 17, 2.5, 4.5, 0, 0, Math.PI * 2); g.fill();
  // mouth curve: happy at full health, flat in the middle, frown when nearly dead
  const curve = (frac - 0.45) * 2 * 9; // px of sag (+ smile / - frown)
  g.beginPath();
  g.moveTo(13, 31);
  g.quadraticCurveTo(24, 31 + curve * 1.6, 35, 31);
  g.stroke();
}

// ---- boot ----------------------------------------------------------------------------------

$('create-form').addEventListener('submit', async e => {
  e.preventDefault();
  unlock();
  state.name = readName();
  try {
    const room = await state.connection.invoke('CreateRoom', {
      name: $('room-name').value,
      mode: $('room-mode').value,
      roundSeconds: Number($('room-round').value),
      bots: Number($('room-bots').value),
    });
    if (!room) { lobbyMessage('Es gibt bereits zu viele Räume.'); return; }
    $('room-name').value = '';
    await enter(room.id);
  } catch (err) {
    console.error(err);
    lobbyMessage('Raum konnte nicht erstellt werden.');
  }
});

$('leave').addEventListener('click', leaveRoom);
$('sound').addEventListener('click', () => { unlock(); toggleMute(); });

try { $('name').value = localStorage.getItem('midimaze.name') ?? ''; } catch { /* private mode */ }

connect().catch(err => {
  console.error(err);
  lobbyMessage('Server nicht erreichbar.');
});
requestAnimationFrame(frame);

// Small debug hook for automated checks in the browser pane.
window.__midimaze = state;
