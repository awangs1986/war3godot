#!/usr/bin/env node
/** Convert War3 TerrainArt BLP1 → RGB PNG sidecars (Godot cannot load CMYK JPEG). */
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const ROOT = path.resolve(__dirname, '../../war3/game/TerrainArt');

function blp1ToJpegBuffer(buf) {
  if (buf.toString('ascii', 0, 4) !== 'BLP1') return null;
  const content = buf.readUInt32LE(4);
  if (content !== 0) return null; // only JPEG path here
  const mipOff = buf.readUInt32LE(28);
  const mipSize = buf.readUInt32LE(28 + 64);
  const jpegHeaderSize = buf.readUInt32LE(156);
  if (jpegHeaderSize <= 0 || mipOff + mipSize > buf.length) return null;
  const header = buf.subarray(160, 160 + jpegHeaderSize);
  const mip = buf.subarray(mipOff, mipOff + mipSize);
  return Buffer.concat([header, mip]);
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
  if (fs.existsSync(png) && fs.statSync(png).size > 100) {
    skip++;
    continue;
  }
  try {
    const buf = fs.readFileSync(blp);
    const jpg = blp1ToJpegBuffer(buf);
    if (!jpg) {
      fail++;
      continue;
    }
    const tmp = png + '.tmp.jpg';
    fs.writeFileSync(tmp, jpg);
    execFileSync('magick', [tmp, '-colorspace', 'sRGB', png], { stdio: 'ignore' });
    fs.unlinkSync(tmp);
    ok++;
    if (ok % 20 === 0) console.log('converted', ok);
  } catch (e) {
    fail++;
    if (fail < 10) console.warn('fail', blp, e.message);
  }
}
console.log({ ok, skip, fail, total: files.length });
