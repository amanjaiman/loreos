// build-tray-icons.mjs — generate the tray icons from the Lore mark (spec v2-006 D6).
//
//   node app/tools/build-tray-icons.mjs
//
// Why a generator instead of committed art: Electron's nativeImage cannot rasterise SVG,
// so the tray needs real bitmaps, and the repo has no icon pipeline (no design tool in the
// loop, no raster dependency in app/package.json). The Lore mark happens to be exactly
// expressible as geometry — a filled disc plus two stroked circular arcs — so it can be
// rendered analytically with 4x4 supersampling and written out with nothing but Node's
// own zlib. Re-running this reproduces the committed bytes byte-for-byte.
//
// Output: app/src/lifecycle/trayIcons.ts (base64 PNGs, ~4 KB total). Embedding beats
// shipping files under resources/: one less path that can be wrong in a packaged build.

import { deflateSync } from 'node:zlib';
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

// ---- the mark, in the design system's 32-unit space -------------------------------
// Mirrors app/src/renderer/design-system/assets/lore-glyph.svg. The SVG's arcs are given
// as endpoints + radius; the centres and sweeps below are those arcs solved for centre.

const DOT = { kind: 'disc', cx: 10.5, cy: 16, r: 3.1 };
// <path d="M16.5 9.4a8.6 8.6 0 0 1 0 13.2" stroke-width="2.4">
const NEAR_ARC = {
    kind: 'arc',
    cx: 10.9864,
    cy: 16,
    r: 8.6,
    halfSweepDeg: 50.13,
    w: 2.4,
};
// <path d="M21 5.2a15 15 0 0 1 0 21.6" stroke-width="2.4">
const FAR_ARC = {
    kind: 'arc',
    cx: 10.5904,
    cy: 16,
    r: 15,
    halfSweepDeg: 46.05,
    w: 2.4,
};

// ---- the three states (spec R3) ---------------------------------------------------
//
// State is carried by *shape* as much as by colour, so it survives a colour-blind viewer
// and a monochrome-ish taskbar: the mark speaks with two arcs when running, one when
// paused, and none — hollow — when stopped.
//
// Colours deviate from the raw design tokens where the taskbar demands it: --secondary
// (#EABA6B) is a light amber that vanishes on a light taskbar, so paused is deepened.
// All three hold contrast on both taskbar themes.
//
// The stopped ring is drawn larger than the mark's dot on purpose. With no arcs beside
// it, a 3.1-unit ring is a speck at 16 px; at 5.4 it reads as a deliberate "off" glyph
// and keeps the icon's optical weight in line with the other two.

const STATES = {
    running: {
        color: [0x00, 0x81, 0xaf], // --primary Cerulean
        elements: [DOT, NEAR_ARC, FAR_ARC],
    },
    paused: {
        color: [0xd9, 0x97, 0x3a], // deepened --secondary
        elements: [DOT, NEAR_ARC],
    },
    stopped: {
        color: [0x7d, 0x75, 0x65], // --n-5
        elements: [{ kind: 'ring', cx: 10.5, cy: 16, r: 5.4, w: 1.9 }],
    },
};

/** Tray sizes Windows asks for across DPI scalings (100/125/150/200%). */
const SIZES = [16, 20, 24, 32];

// ---- rasteriser -------------------------------------------------------------------

const SUPERSAMPLE = 4; // 4x4 samples per pixel — plenty for shapes this small

/** Is this point inside the given element? */
function inElement(x, y, el) {
    const d = Math.hypot(x - el.cx, y - el.cy);
    if (el.kind === 'disc') return d <= el.r;
    if (el.kind === 'ring') return Math.abs(d - el.r) <= el.w / 2;

    // arc: on the stroked band of the circle, within the swept angle (the arcs open to
    // the right, centred on 0°), plus round caps to match stroke-linecap="round".
    const half = el.w / 2;
    if (Math.abs(d - el.r) <= half) {
        const deg = Math.abs(
            (Math.atan2(y - el.cy, x - el.cx) * 180) / Math.PI,
        );
        if (deg <= el.halfSweepDeg) return true;
    }
    const rad = (el.halfSweepDeg * Math.PI) / 180;
    for (const sign of [-1, 1]) {
        const ex = el.cx + el.r * Math.cos(sign * rad);
        const ey = el.cy + el.r * Math.sin(sign * rad);
        if (Math.hypot(x - ex, y - ey) <= half) return true;
    }
    return false;
}

/** Is this point inside the mark, for the given state (after centring)? */
function covered(x, y, state, dx) {
    return state.elements.some((el) => inElement(x - dx, y, el));
}

/**
 * Horizontal offset that centres a state's artwork in the icon box.
 *
 * Dropping the arcs shifts the mark's centre of mass hard to the left — the full mark
 * spans roughly x 7.4-25.4, the bare dot sits at 10.5 — so an uncentred paused/stopped
 * icon reads as a speck stuck to the left edge of its tray slot. Measured from a
 * high-resolution scan rather than derived, so it stays correct if the geometry changes.
 */
function centringOffset(state) {
    const STEPS = 256;
    let min = Infinity;
    let max = -Infinity;
    for (let i = 0; i < STEPS; i++) {
        const x = ((i + 0.5) / STEPS) * 32;
        for (let j = 0; j < STEPS; j++) {
            const y = ((j + 0.5) / STEPS) * 32;
            if (state.elements.some((el) => inElement(x, y, el))) {
                if (x < min) min = x;
                if (x > max) max = x;
                break;
            }
        }
    }
    return 16 - (min + max) / 2;
}

/** Render one state at one size into a raw RGBA buffer. */
function render(size, state) {
    const scale = 32 / size;
    const dx = centringOffset(state);
    const rgba = Buffer.alloc(size * size * 4);
    const [r, g, b] = state.color;
    for (let py = 0; py < size; py++) {
        for (let px = 0; px < size; px++) {
            let hits = 0;
            for (let sy = 0; sy < SUPERSAMPLE; sy++) {
                for (let sx = 0; sx < SUPERSAMPLE; sx++) {
                    const x = (px + (sx + 0.5) / SUPERSAMPLE) * scale;
                    const y = (py + (sy + 0.5) / SUPERSAMPLE) * scale;
                    if (covered(x, y, state, dx)) hits++;
                }
            }
            const offset = (py * size + px) * 4;
            // Premultiplication is not wanted here: PNG is straight alpha.
            rgba[offset] = r;
            rgba[offset + 1] = g;
            rgba[offset + 2] = b;
            rgba[offset + 3] = Math.round(
                (hits / (SUPERSAMPLE * SUPERSAMPLE)) * 255,
            );
        }
    }
    return rgba;
}

// ---- minimal PNG writer -----------------------------------------------------------

const CRC_TABLE = (() => {
    const table = new Int32Array(256);
    for (let n = 0; n < 256; n++) {
        let c = n;
        for (let k = 0; k < 8; k++)
            c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        table[n] = c;
    }
    return table;
})();

function crc32(buf) {
    let c = 0xffffffff;
    for (const byte of buf) c = CRC_TABLE[(c ^ byte) & 0xff] ^ (c >>> 8);
    return (c ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const body = Buffer.concat([Buffer.from(type, 'latin1'), data]);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(body));
    return Buffer.concat([length, body, crc]);
}

function toPng(size, rgba) {
    const ihdr = Buffer.alloc(13);
    ihdr.writeUInt32BE(size, 0);
    ihdr.writeUInt32BE(size, 4);
    ihdr[8] = 8; // bit depth
    ihdr[9] = 6; // colour type: RGBA
    // 10-12: deflate / adaptive filtering / no interlace — all zero.

    // One scanline per row, each prefixed with filter type 0 (None). These images are tiny
    // and mostly transparent; smarter filters would not pay for the complexity.
    const stride = size * 4;
    const raw = Buffer.alloc((stride + 1) * size);
    for (let y = 0; y < size; y++) {
        rgba.copy(raw, y * (stride + 1) + 1, y * stride, (y + 1) * stride);
    }

    return Buffer.concat([
        Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        chunk('IHDR', ihdr),
        chunk('IDAT', deflateSync(raw, { level: 9 })),
        chunk('IEND', Buffer.alloc(0)),
    ]);
}

// ---- emit -------------------------------------------------------------------------

const entries = Object.entries(STATES).map(([name, state]) => {
    const perSize = SIZES.map(
        (size) =>
            `    ${size}: '${toPng(size, render(size, state)).toString('base64')}',`,
    );
    return `  ${name}: {\n${perSize.join('\n')}\n  },`;
});

const out = `// trayIcons.ts — GENERATED by app/tools/build-tray-icons.mjs. Do not edit by hand.
//
// The Lore mark rendered for the tray in each lifecycle state (spec v2-006 R3/D6), as
// base64 PNGs at the four sizes Windows asks for across DPI scalings. Regenerate with:
//
//   node app/tools/build-tray-icons.mjs

/** Base64 PNGs keyed by state, then by pixel size. */
export const TRAY_ICONS: Record<string, Record<number, string>> = {
${entries.join('\n')}
};

/** The sizes present for every state, smallest first. */
export const TRAY_ICON_SIZES = [${SIZES.join(', ')}] as const;
`;

const target = join(
    dirname(fileURLToPath(import.meta.url)),
    '..',
    'src',
    'lifecycle',
    'trayIcons.ts',
);
writeFileSync(target, out, 'utf8');
console.log(`wrote ${target}`);
