#!/usr/bin/env node
/**
 * Convert model/doodad/unit BLP1 JPEG → RGBA PNG sidecars.
 *
 * War3 BLP1 JPEG is CMYK where channels are actually B,G,R,A (HiveWE /
 * libturbojpeg TJPF_CMYK + R↔B). ImageMagick -colorspace sRGB/CMYK is wrong.
 *
 * Uses tools/mpqtool/blp_cmyk_to_png.py (ctypes + libturbojpeg).
 *
 * Usage:
 *   node convert_model_blp.cjs              # Adrenaline-relevant dirs
 *   node convert_model_blp.cjs --all        # all under war3/game (slow)
 *   node convert_model_blp.cjs --force      # rewrite existing PNGs
 */
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const GAME = path.resolve(__dirname, '../../war3/game');
const HELPER = path.join(__dirname, 'blp_cmyk_to_png.py');
const FORCE = process.argv.includes('--force');
const ALL = process.argv.includes('--all');

const ADRENALINE_DIRS = [
  'ReplaceableTextures/LordaeronTree',
  'ReplaceableTextures/TeamColor',
  'ReplaceableTextures/TeamGlow',
  'ReplaceableTextures/Cliff',
  'Units/Creeps',
  'Units/Critters',
  'Units/Orc',
  'Units/Human',
  'buildings',
  'Buildings',
  'Doodads/LordaeronSummer',
  'Doodads/Terrain/LordaeronTree',
  'Textures',
];

function isJpegBlp(buf) {
  return buf.toString('ascii', 0, 4) === 'BLP1' && buf.readUInt32LE(4) === 0;
}

function walk(dir, out = []) {
  if (!fs.existsSync(dir)) return out;
  for (const name of fs.readdirSync(dir)) {
    const p = path.join(dir, name);
    const st = fs.statSync(p);
    if (st.isDirectory()) walk(p, out);
    else if (name.toLowerCase().endsWith('.blp')) out.push(p);
  }
  return out;
}

function convertOne(blp) {
  const png = blp.replace(/\.blp$/i, '.png');
  if (!FORCE && fs.existsSync(png) && fs.statSync(png).size > 100) {
    return 'skip';
  }
  const buf = fs.readFileSync(blp);
  if (!isJpegBlp(buf)) return 'fail-format';
  try {
    execFileSync('python3', [HELPER, blp, png], { stdio: 'ignore' });
  } catch (_) {
    return 'fail-magick';
  }
  if (!fs.existsSync(png) || fs.statSync(png).size < 50) return 'fail-magick';
  return 'ok';
}

const roots = ALL
  ? [GAME]
  : ADRENALINE_DIRS.map((d) => path.join(GAME, d));

let files = [];
for (const r of roots) files = files.concat(walk(r));
files = [...new Set(files)];

let ok = 0,
  skip = 0,
  fail = 0;
for (const blp of files) {
  try {
    const r = convertOne(blp);
    if (r === 'ok') ok++;
    else if (r === 'skip') skip++;
    else {
      fail++;
      if (fail <= 15) console.warn(r, path.relative(GAME, blp));
    }
  } catch (e) {
    fail++;
    if (fail <= 15) console.warn('err', path.relative(GAME, blp), e.message);
  }
  if ((ok + skip + fail) % 100 === 0) {
    process.stdout.write(`… ${ok + skip + fail}/${files.length}\n`);
  }
}
console.log({ ok, skip, fail, total: files.length, force: FORCE, all: ALL });
