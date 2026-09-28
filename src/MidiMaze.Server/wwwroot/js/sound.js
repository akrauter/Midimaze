// Tiny synthesized sound effects (Web Audio, no asset files). The AudioContext can only start after a
// user gesture, so call unlock() from a click/keypress handler.

let ctx = null;
let muted = false;

try { muted = localStorage.getItem('midimaze.muted') === '1'; } catch { /* private mode */ }

export function unlock() {
  if (!ctx) {
    try { ctx = new (window.AudioContext || window.webkitAudioContext)(); } catch { ctx = null; }
  }
  if (ctx && ctx.state === 'suspended') ctx.resume();
}

export function isMuted() { return muted; }

export function setMuted(value) {
  muted = value;
  try { localStorage.setItem('midimaze.muted', value ? '1' : '0'); } catch { /* private mode */ }
}

function tone({ from, to, dur, type = 'square', vol = 0.15, delay = 0 }) {
  if (!ctx || muted) return;
  const t = ctx.currentTime + delay;
  const osc = ctx.createOscillator();
  const gain = ctx.createGain();
  osc.type = type;
  osc.frequency.setValueAtTime(from, t);
  if (to) osc.frequency.exponentialRampToValueAtTime(to, t + dur);
  gain.gain.setValueAtTime(vol, t);
  gain.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  osc.connect(gain).connect(ctx.destination);
  osc.start(t);
  osc.stop(t + dur + 0.02);
}

function noise({ dur, vol = 0.2, delay = 0 }) {
  if (!ctx || muted) return;
  const t = ctx.currentTime + delay;
  const len = Math.max(1, Math.floor(ctx.sampleRate * dur));
  const buf = ctx.createBuffer(1, len, ctx.sampleRate);
  const data = buf.getChannelData(0);
  for (let i = 0; i < len; i++) data[i] = (Math.random() * 2 - 1) * (1 - i / len);
  const src = ctx.createBufferSource();
  const gain = ctx.createGain();
  gain.gain.setValueAtTime(vol, t);
  src.buffer = buf;
  src.connect(gain).connect(ctx.destination);
  src.start(t);
}

// `v` is a 0..1 loudness (shots far away are quieter)
export const sfx = {
  shoot(v = 1) { tone({ from: 900, to: 220, dur: 0.12, vol: 0.10 * v }); },
  hit() { tone({ from: 1300, to: 1900, dur: 0.07, type: 'sine', vol: 0.18 }); },
  hurt() { noise({ dur: 0.18, vol: 0.25 }); tone({ from: 220, to: 90, dur: 0.18, type: 'sawtooth', vol: 0.14 }); },
  kill(v = 1) { tone({ from: 500, to: 60, dur: 0.35, type: 'sawtooth', vol: 0.12 * v }); noise({ dur: 0.25, vol: 0.15 * v }); },
  death() { tone({ from: 420, to: 50, dur: 0.7, type: 'sawtooth', vol: 0.2 }); noise({ dur: 0.4, vol: 0.2 }); },
  respawn() { tone({ from: 300, dur: 0.09, vol: 0.12 }); tone({ from: 450, dur: 0.09, vol: 0.12, delay: 0.09 }); tone({ from: 600, dur: 0.14, vol: 0.12, delay: 0.18 }); },
  roundStart() { tone({ from: 440, dur: 0.12, type: 'triangle', vol: 0.2 }); tone({ from: 660, dur: 0.2, type: 'triangle', vol: 0.2, delay: 0.14 }); },
  roundEnd() {
    [523, 659, 784, 1047].forEach((f, i) => tone({ from: f, dur: 0.22, type: 'triangle', vol: 0.2, delay: i * 0.16 }));
  },
};
