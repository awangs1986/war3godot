/**
 * Extract a .w3x/.w3m into war3/maps/<name>/ using mdx-m3-viewer MPQ
 * (7z cannot read HM3W+MPQ maps).
 *
 * Usage:
 *   node extract_map.cjs "/path/to/Map.w3x" [outDir]
 */
const fs = require('fs');
const path = require('path');
const MpqArchive = require('mdx-m3-viewer/dist/cjs/parsers/mpq/archive.js').default;

const CORE = [
  'war3map.w3e', 'war3map.w3i', 'war3map.doo', 'war3mapUnits.doo',
  'war3map.w3u', 'war3map.w3t', 'war3map.w3a', 'war3map.w3b', 'war3map.w3d', 'war3map.w3q',
  'war3map.w3h', 'war3map.w3g', 'war3map.imp', 'war3map.wts', 'war3map.wtg', 'war3map.wct',
  'war3map.j', 'war3map.lua', 'scripts\\war3map.j', 'scripts/war3map.j',
  'war3mapMap.blp', 'war3mapPreview.tga', 'war3map.mmp', 'war3map.shd', 'war3map.wpm',
  'war3mapMisc.txt', 'war3mapSkin.txt',
];

function loadMpq(filePath) {
  const buf = fs.readFileSync(filePath);
  const copy = new Uint8Array(buf.byteLength);
  copy.set(buf);
  const a = new MpqArchive();
  a.load(copy, true);
  return a;
}

function writeOut(outRoot, name, bytes) {
  const rel = name.replace(/\\/g, '/');
  const dest = path.join(outRoot, rel);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.writeFileSync(dest, Buffer.from(bytes));
}

function collectImportPaths(a) {
  const out = [];
  const imp = a.get('war3map.imp');
  if (!imp) return out;
  const b = Buffer.from(imp.bytes());
  // Best-effort: pull ASCII-ish path strings.
  let cur = '';
  for (let i = 0; i < b.length; i++) {
    const c = b[i];
    if (c >= 32 && c < 127) cur += String.fromCharCode(c);
    else {
      if (cur.length > 4 && /[\\/.]/.test(cur)) out.push(cur.replace(/\0/g, ''));
      cur = '';
    }
  }
  if (cur.length > 4) out.push(cur);
  return out;
}

function main() {
  const src = process.argv[2];
  if (!src || !fs.existsSync(src)) {
    console.error('Usage: node extract_map.cjs <map.w3x> [outDir]');
    process.exit(1);
  }
  const base = path.basename(src, path.extname(src));
  const out = process.argv[3]
    ? path.resolve(process.argv[3])
    : path.resolve(__dirname, '../../war3/maps', base);
  fs.mkdirSync(out, { recursive: true });
  console.log('Extract', src, '->', out);
  const a = loadMpq(src);
  const want = new Set([...CORE, ...collectImportPaths(a)]);
  // Also try listed names when listfile exists.
  try {
    for (const n of a.getFileNames()) {
      if (n && !n.startsWith('File') && !n.startsWith('(')) want.add(n);
    }
  } catch (_) {}

  let ok = 0;
  let miss = 0;
  for (const name of want) {
    try {
      const f = a.get(name);
      if (!f) {
        miss++;
        continue;
      }
      writeOut(out, name, f.bytes());
      ok++;
    } catch (_) {
      miss++;
    }
  }
  const hasW3e = fs.existsSync(path.join(out, 'war3map.w3e'));
  console.log('done ok=', ok, 'miss=', miss, 'war3map.w3e=', hasW3e);
  if (!hasW3e) process.exit(2);
}

main();
