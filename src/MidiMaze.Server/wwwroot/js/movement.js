// Line-for-line port of Game/Movement.cs. Keep both in sync: the client predicts its own
// movement with this and the server's snapshots correct any drift.

export function isWall(map, x, y) {
  return y < 0 || y >= map.length || x < 0 || x >= map[0].length || map[y][x] === '#';
}

export function collides(map, px, py, radius) {
  const r = radius;
  return isWall(map, Math.floor(px - r), Math.floor(py - r))
    || isWall(map, Math.floor(px + r), Math.floor(py - r))
    || isWall(map, Math.floor(px - r), Math.floor(py + r))
    || isWall(map, Math.floor(px + r), Math.floor(py + r));
}

/** Applies one fixed step to `s` ({x, y, a}) in place. */
export function step(map, cfg, s, forward, strafe, turn) {
  const dt = 1 / cfg.tickRate;

  s.a += turn * cfg.turnSpeed * dt;
  if (s.a > Math.PI) s.a -= 2 * Math.PI;
  else if (s.a <= -Math.PI) s.a += 2 * Math.PI;

  let mx = Math.cos(s.a) * forward - Math.sin(s.a) * strafe;
  let my = Math.sin(s.a) * forward + Math.cos(s.a) * strafe;
  const len = Math.sqrt(mx * mx + my * my);
  if (len < 1e-9) return;
  if (len > 1) { mx /= len; my /= len; }

  const dist = cfg.moveSpeed * dt;

  const nx = s.x + mx * dist;
  if (!collides(map, nx, s.y, cfg.playerRadius)) s.x = nx;
  const ny = s.y + my * dist;
  if (!collides(map, s.x, ny, cfg.playerRadius)) s.y = ny;
}
