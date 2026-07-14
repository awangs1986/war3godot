#!/usr/bin/env node
/**
 * Re-convert TerrainArt BLP → RGBA PNG with alpha preserved.
 * Ground atlases store soft-blend masks in left 4×4 cells (alpha); RGB-only
 * conversion (ImageMagick CMYK→sRGB) drops them and causes hard square borders.
 *
 * Uses mdx-m3-viewer BlpImage + custom JPEG decoder (BLP non-standard RGBA JPEG).
 */
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

global.ImageData = class ImageData {
  constructor(w, h) {
    this.width = w;
    this.height = h;
    this.data = new Uint8ClampedArray(w * h * 4);
  }
};

const { BlpImage } = require('mdx-m3-viewer/dist/cjs/parsers/blp/image.js');

const ROOT = path.resolve(__dirname, '../../war3/game/TerrainArt');
const FORCE = process.argv.includes('--force');

function crc32(buf) {
  let c = ~0;
  for (let i = 0; i < buf.length; i++) {
    c ^= buf[i];
    for (let k = 0; k < 8; k++) c = (c >>> 1) ^ (0xedb88320 & -(c & 1));
  }
  return ~c >>> 0;
}

function writePngRGBA(filePath, width, height, rgba) {
  const signature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  function chunk(type, data) {
    const len = Buffer.alloc(4);
    len.writeUInt32BE(data.length, 0);
    const typeBuf = Buffer.from(type);
    const crcBuf = Buffer.alloc(4);
    crcBuf.writeUInt32BE(crc32(Buffer.concat([typeBuf, data])), 0);
    return Buffer.concat([len, typeBuf, data, crcBuf]);
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 6; // RGBA
  ihdr[10] = 0;
  ihdr[11] = 0;
  ihdr[12] = 0;
  const stride = width * 4;
  const raw = Buffer.alloc((stride + 1) * height);
  for (let y = 0; y < height; y++) {
    raw[y * (stride + 1)] = 0; // filter none
    Buffer.from(rgba.buffer, rgba.byteOffset + y * stride, stride).copy(raw, y * (stride + 1) + 1);
  }
  const compressed = zlib.deflateSync(raw, { level: 6 });
  const out = Buffer.concat([signature, chunk('IHDR', ihdr), chunk('IDAT', compressed), chunk('IEND', Buffer.alloc(0))]);
  fs.writeFileSync(filePath, out);
}

function walk(dir, out = []) {
  for (const name of fs.readdirSync(dir)) {
    const p = path.join(dir, name);
    const st = fs.statSync(p);
    if (st.isDirectory()) walk(p, out);
    else if (name.toLowerCase().endsWith('.blp')) out.push(p);
  }
  return out;
}

const files = walk(ROOT);
let ok = 0, skip = 0, fail = 0;
for (const blp of files) {
  const png = blp.replace(/\.blp$/i, '.png');
  if (!FORCE && fs.existsSync(png)) {
    // Re-convert if existing PNG lacks alpha (RGB / no translucent pixels).
    try {
      const buf = fs.readFileSync(png);
      const hasAlphaChunk = buf.includes(Buffer.from('tRNS')) || (buf[25] === 6); // color type in IHDR
      // IHDR color type at offset 25 after signature+len+type = 8+4+4=16, +8 width height = 24, byte 25 is color type... 
      // Actually: sig 8, len 4, IHDR 4, data starts 16: w4 h4 depth1 color1 → color at 16+8+1=25
      const colorType = buf[25];
      if (colorType === 6 && !FORCE) {
        skip++;
        continue;
      }
    } catch (_) {}
  }
  try {
    const buf = fs.readFileSync(blp);
    const img = new BlpImage();
    img.load(buf);
    const mip = img.getMipmap(0);
    writePngRGBA(png, mip.width, mip.height, mip.data);
    ok++;
    if (ok % 20 === 0) console.log('converted', ok);
  } catch (e) {
    fail++;
    if (fail <= 12) console.warn('fail', path.relative(ROOT, blp), e.message);
  }
}
console.log({ ok, skip, fail, total: files.length, force: FORCE });
