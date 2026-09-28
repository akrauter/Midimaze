// First-person renderer: DDA raycaster for the walls, billboard sprites for smileys and shots,
// plus a small top-down map. Inspired by the look of the original (flat shaded walls, floating smileys).

export const VIEW_W = 320;
export const VIEW_H = 200;
const FOV_PLANE = 0.72; // half-width of the camera plane relative to the direction vector

const spriteCache = new Map();

function hsl(h, s, l) {
  return `hsl(${h} ${s}% ${l}%)`;
}

/** kind: 'front' | 'back' | 'dead' */
function smileySprite(hue, kind) {
  const key = `${hue}:${kind}`;
  let c = spriteCache.get(key);
  if (c) return c;

  c = document.createElement('canvas');
  c.width = c.height = 64;
  const g = c.getContext('2d');

  const grad = g.createRadialGradient(24, 22, 4, 32, 32, 31);
  grad.addColorStop(0, hsl(hue, 85, 72));
  grad.addColorStop(1, hsl(hue, 80, 38));
  g.fillStyle = grad;
  g.beginPath();
  g.arc(32, 32, 30, 0, Math.PI * 2);
  g.fill();
  g.lineWidth = 2;
  g.strokeStyle = '#000';
  g.stroke();

  g.strokeStyle = '#000';
  g.fillStyle = '#000';
  g.lineWidth = 3;
  g.lineCap = 'round';

  if (kind === 'front') {
    g.beginPath(); g.ellipse(22, 24, 3.5, 6, 0, 0, Math.PI * 2); g.fill();
    g.beginPath(); g.ellipse(42, 24, 3.5, 6, 0, 0, Math.PI * 2); g.fill();
    g.beginPath(); g.arc(32, 34, 15, 0.15 * Math.PI, 0.85 * Math.PI); g.stroke();
  } else if (kind === 'dead') {
    for (const ex of [22, 42]) {
      g.beginPath(); g.moveTo(ex - 5, 19); g.lineTo(ex + 5, 29); g.moveTo(ex + 5, 19); g.lineTo(ex - 5, 29); g.stroke();
    }
    g.beginPath(); g.arc(32, 50, 12, 1.15 * Math.PI, 1.85 * Math.PI); g.stroke();
  } else {
    // seen from behind: just a parting line
    g.globalAlpha = 0.35;
    g.beginPath(); g.moveTo(32, 6); g.lineTo(32, 58); g.stroke();
  }

  spriteCache.set(key, c);
  return c;
}

export class Renderer {
  constructor(canvas, minimap) {
    this.canvas = canvas;
    canvas.width = VIEW_W;
    canvas.height = VIEW_H;
    this.g = canvas.getContext('2d');
    this.mini = minimap;
    this.mg = minimap.getContext('2d');
    this.zbuf = new Float32Array(VIEW_W);
  }

  /**
   * view: {x, y, a}  map: string[]
   * others: [{x, y, a, hue, alive}]   shots: [{x, y}]
   */
  draw(map, view, others, shots) {
    const g = this.g;

    // ceiling + floor
    const sky = g.createLinearGradient(0, 0, 0, VIEW_H / 2);
    sky.addColorStop(0, '#4d6f86'); sky.addColorStop(1, '#8fb0c2');
    g.fillStyle = sky; g.fillRect(0, 0, VIEW_W, VIEW_H / 2);
    const floor = g.createLinearGradient(0, VIEW_H / 2, 0, VIEW_H);
    floor.addColorStop(0, '#2c4a5c'); floor.addColorStop(1, '#152a36');
    g.fillStyle = floor; g.fillRect(0, VIEW_H / 2, VIEW_W, VIEW_H / 2);

    const dirX = Math.cos(view.a), dirY = Math.sin(view.a);
    const planeX = -dirY * FOV_PLANE, planeY = dirX * FOV_PLANE;

    this.#walls(map, view, dirX, dirY, planeX, planeY);
    this.#sprites(view, dirX, dirY, planeX, planeY, others, shots);
  }

  #walls(map, view, dirX, dirY, planeX, planeY) {
    const g = this.g;
    const mapW = map[0].length, mapH = map.length;

    for (let col = 0; col < VIEW_W; col++) {
      const camX = 2 * col / VIEW_W - 1;
      const rayX = dirX + planeX * camX;
      const rayY = dirY + planeY * camX;

      let mx = Math.floor(view.x), my = Math.floor(view.y);
      const deltaX = Math.abs(1 / (rayX || 1e-9));
      const deltaY = Math.abs(1 / (rayY || 1e-9));
      let stepX, stepY, sideX, sideY;

      if (rayX < 0) { stepX = -1; sideX = (view.x - mx) * deltaX; } else { stepX = 1; sideX = (mx + 1 - view.x) * deltaX; }
      if (rayY < 0) { stepY = -1; sideY = (view.y - my) * deltaY; } else { stepY = 1; sideY = (my + 1 - view.y) * deltaY; }

      let side = 0;
      let hit = false;
      for (let i = 0; i < 64 && !hit; i++) {
        if (sideX < sideY) { sideX += deltaX; mx += stepX; side = 0; } else { sideY += deltaY; my += stepY; side = 1; }
        if (mx < 0 || my < 0 || mx >= mapW || my >= mapH || map[my][mx] === '#') hit = true;
      }

      const dist = side === 0 ? sideX - deltaX : sideY - deltaY;
      const perp = Math.max(dist, 0.01);
      this.zbuf[col] = perp;

      const lineH = Math.min(VIEW_H * 4, Math.round(VIEW_H / perp));
      const top = Math.round((VIEW_H - lineH) / 2);

      // Where along the wall tile did we hit? Used to draw dark edges between tiles.
      let wallX = side === 0 ? view.y + perp * rayY : view.x + perp * rayX;
      wallX -= Math.floor(wallX);

      const shade = Math.max(0.28, 1 - perp / 11);
      const base = side === 0 ? [214, 196, 92] : [150, 168, 176];
      const edge = wallX < 0.04 || wallX > 0.96;
      const k = edge ? shade * 0.55 : shade;
      g.fillStyle = `rgb(${base[0] * k | 0} ${base[1] * k | 0} ${base[2] * k | 0})`;
      g.fillRect(col, top, 1, lineH);
    }
  }

  #sprites(view, dirX, dirY, planeX, planeY, others, shots) {
    const g = this.g;
    const invDet = 1 / (planeX * dirY - dirX * planeY);
    const list = [];

    for (const o of others) {
      list.push({ x: o.x, y: o.y, size: 0.62, lift: 0, kind: 'smiley', o });
    }
    for (const s of shots) {
      list.push({ x: s.x, y: s.y, size: 0.09, lift: 0.05, kind: 'shot' });
    }

    for (const s of list) {
      const sx = s.x - view.x, sy = s.y - view.y;
      const tx = invDet * (dirY * sx - dirX * sy);
      const ty = invDet * (-planeY * sx + planeX * sy);
      s.tx = tx; s.ty = ty;
    }

    list.filter(s => s.ty > 0.15).sort((a, b) => b.ty - a.ty).forEach(s => {
      const screenX = (VIEW_W / 2) * (1 + s.tx / s.ty);
      const unit = VIEW_H / s.ty;             // height of a wall at that distance
      const size = unit * s.size;
      const cy = VIEW_H / 2 - unit * s.lift;  // sprites float around eye height
      const left = Math.round(screenX - size / 2);
      const topY = Math.round(cy - size / 2);
      const w = Math.max(1, Math.round(size));

      if (s.kind === 'smiley') {
        const o = s.o;
        let kind = 'back';
        if (!o.alive) kind = 'dead';
        else {
          // Does the other player face us? Compare their heading with the vector towards us.
          const toUsX = view.x - o.x, toUsY = view.y - o.y;
          const facing = Math.cos(o.a) * toUsX + Math.sin(o.a) * toUsY;
          if (facing > 0.25 * Math.hypot(toUsX, toUsY)) kind = 'front';
        }
        const img = smileySprite(o.hue, kind);

        // shadow on the floor
        const shadowY = VIEW_H / 2 + unit * 0.42;
        g.fillStyle = 'rgba(0,0,0,0.35)';
        g.beginPath();
        g.ellipse(screenX, shadowY, size * 0.38, Math.max(1, size * 0.07), 0, 0, Math.PI * 2);
        g.fill();

        for (let x = 0; x < w; x++) {
          const col = left + x;
          if (col < 0 || col >= VIEW_W || s.ty >= this.zbuf[col]) continue;
          g.drawImage(img, Math.floor(x * 64 / w), 0, 1, 64, col, topY, 1, Math.max(1, Math.round(size)));
        }
      } else {
        g.fillStyle = '#ffe94d';
        for (let x = 0; x < w; x++) {
          const col = left + x;
          if (col < 0 || col >= VIEW_W || s.ty >= this.zbuf[col]) continue;
          g.fillRect(col, topY, 1, w);
        }
      }
    });
  }

  drawMinimap(map, me, others) {
    const g = this.mg;
    const cw = this.mini.width / map[0].length;
    const ch = this.mini.height / map.length;

    g.fillStyle = 'rgba(8,16,22,0.85)';
    g.fillRect(0, 0, this.mini.width, this.mini.height);
    g.fillStyle = '#6f8792';
    for (let y = 0; y < map.length; y++)
      for (let x = 0; x < map[0].length; x++)
        if (map[y][x] === '#') g.fillRect(x * cw, y * ch, Math.ceil(cw), Math.ceil(ch));

    for (const o of others) {
      if (!o.alive) continue;
      g.fillStyle = hsl(o.hue, 85, 55);
      g.beginPath(); g.arc(o.x * cw, o.y * ch, 2.5, 0, Math.PI * 2); g.fill();
    }
    g.fillStyle = '#fff';
    g.beginPath(); g.arc(me.x * cw, me.y * ch, 3, 0, Math.PI * 2); g.fill();
    g.strokeStyle = '#fff'; g.lineWidth = 1;
    g.beginPath(); g.moveTo(me.x * cw, me.y * ch);
    g.lineTo((me.x + Math.cos(me.a) * 0.9) * cw, (me.y + Math.sin(me.a) * 0.9) * ch); g.stroke();
  }
}
