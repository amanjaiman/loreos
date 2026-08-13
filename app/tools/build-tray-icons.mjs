// Generate the stateful tray bitmaps and Windows app icon from the Lore mark.
//
//   node app/tools/build-tray-icons.mjs
//
// The source geometry below mirrors the committed transparent SVG. A tiny deterministic
// rasterizer keeps the icon pipeline dependency-free and makes every binary reproducible.

import { deflateSync } from 'node:zlib';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const LAYERS = [
    {
        x: 408,
        y: 408,
        width: 520,
        height: 520,
        radius: 130,
        color: [0xe9, 0xc0, 0x7a],
    },
    {
        x: 252,
        y: 252,
        width: 520,
        height: 520,
        radius: 130,
        color: [0x9a, 0x70, 0x4e],
    },
    {
        x: 96,
        y: 96,
        width: 520,
        height: 520,
        radius: 130,
        color: [0x00, 0x7f, 0xa8],
    },
];
const TRAY_SIZES = [16, 20, 24, 32];
const ICO_SIZES = [16, 24, 32, 48, 64, 128, 256];

function inRoundedRect(x, y, rect) {
    const right = rect.x + rect.width;
    const bottom = rect.y + rect.height;
    const cx = Math.max(rect.x + rect.radius, Math.min(x, right - rect.radius));
    const cy = Math.max(
        rect.y + rect.radius,
        Math.min(y, bottom - rect.radius),
    );
    return (x - cx) ** 2 + (y - cy) ** 2 <= rect.radius ** 2;
}

function badgeColor(x, y, state) {
    if (state === 'running') return undefined;
    const dx = x - 820;
    const dy = y - 820;
    if (dx * dx + dy * dy > 162 ** 2) return undefined;
    if (state === 'paused') {
        if ((x >= 758 && x <= 795) || (x >= 845 && x <= 882)) {
            if (y >= 735 && y <= 905) return null;
        }
        return [0xd9, 0x97, 0x3a];
    }
    if (dx * dx + dy * dy < 92 ** 2) return null;
    return [0x7d, 0x75, 0x65];
}

function colorAt(x, y, state) {
    let color = null;
    for (const layer of LAYERS) {
        if (inRoundedRect(x, y, layer)) color = layer.color;
    }
    const badge = badgeColor(x, y, state);
    return badge === undefined ? color : badge;
}

function render(size, state = 'running') {
    const samples = size <= 32 ? 4 : size <= 64 ? 2 : 1;
    const rgba = Buffer.alloc(size * size * 4);
    for (let py = 0; py < size; py++) {
        for (let px = 0; px < size; px++) {
            const sums = [0, 0, 0, 0];
            for (let sy = 0; sy < samples; sy++) {
                for (let sx = 0; sx < samples; sx++) {
                    const color = colorAt(
                        ((px + (sx + 0.5) / samples) * 1024) / size,
                        ((py + (sy + 0.5) / samples) * 1024) / size,
                        state,
                    );
                    if (color) {
                        sums[0] += color[0];
                        sums[1] += color[1];
                        sums[2] += color[2];
                        sums[3]++;
                    }
                }
            }
            const count = samples * samples;
            const offset = (py * size + px) * 4;
            if (sums[3] > 0) {
                rgba[offset] = Math.round(sums[0] / sums[3]);
                rgba[offset + 1] = Math.round(sums[1] / sums[3]);
                rgba[offset + 2] = Math.round(sums[2] / sums[3]);
            }
            rgba[offset + 3] = Math.round((sums[3] / count) * 255);
        }
    }
    return rgba;
}

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

function crc32(buffer) {
    let c = 0xffffffff;
    for (const byte of buffer) c = CRC_TABLE[(c ^ byte) & 0xff] ^ (c >>> 8);
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
    const header = Buffer.alloc(13);
    header.writeUInt32BE(size, 0);
    header.writeUInt32BE(size, 4);
    header[8] = 8;
    header[9] = 6;
    const stride = size * 4;
    const raw = Buffer.alloc((stride + 1) * size);
    for (let y = 0; y < size; y++) {
        rgba.copy(raw, y * (stride + 1) + 1, y * stride, (y + 1) * stride);
    }
    return Buffer.concat([
        Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        chunk('IHDR', header),
        chunk('IDAT', deflateSync(raw, { level: 9 })),
        chunk('IEND', Buffer.alloc(0)),
    ]);
}

function toIco(images) {
    const header = Buffer.alloc(6);
    header.writeUInt16LE(1, 2);
    header.writeUInt16LE(images.length, 4);
    const directory = Buffer.alloc(images.length * 16);
    let offset = header.length + directory.length;
    images.forEach(({ size, png }, index) => {
        const entry = index * 16;
        directory[entry] = size === 256 ? 0 : size;
        directory[entry + 1] = size === 256 ? 0 : size;
        directory.writeUInt16LE(1, entry + 4);
        directory.writeUInt16LE(32, entry + 6);
        directory.writeUInt32LE(png.length, entry + 8);
        directory.writeUInt32LE(offset, entry + 12);
        offset += png.length;
    });
    return Buffer.concat([header, directory, ...images.map(({ png }) => png)]);
}

const scriptDir = dirname(fileURLToPath(import.meta.url));
const trayStates = ['running', 'paused', 'stopped'].map((state) => {
    const sizes = TRAY_SIZES.map(
        (size) =>
            `    ${size}: '${toPng(size, render(size, state)).toString('base64')}',`,
    );
    return `  ${state}: {\n${sizes.join('\n')}\n  },`;
});
const trayOutput = `// trayIcons.ts — GENERATED by app/tools/build-tray-icons.mjs. Do not edit by hand.\n\nexport const TRAY_ICONS: Record<string, Record<number, string>> = {\n${trayStates.join('\n')}\n};\n\nexport const TRAY_ICON_SIZES = [${TRAY_SIZES.join(', ')}] as const;\n`;
writeFileSync(
    join(scriptDir, '..', 'src', 'lifecycle', 'trayIcons.ts'),
    trayOutput,
    'utf8',
);

const assetDir = join(scriptDir, '..', 'assets');
mkdirSync(assetDir, { recursive: true });
const icoImages = ICO_SIZES.map((size) => ({
    size,
    png: toPng(size, render(size)),
}));
writeFileSync(join(assetDir, 'lore.ico'), toIco(icoImages));
writeFileSync(join(assetDir, 'lore.png'), icoImages.at(-1).png);
console.log('wrote trayIcons.ts, assets/lore.ico, and assets/lore.png');
