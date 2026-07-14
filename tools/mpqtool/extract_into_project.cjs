/**
 * Extract War3 MPQs + a map into the Godot project's war3/ tree.
 *
 * HiveWE (tools/refs/HiveWE) unpacks via StormLib:
 *   mpq.ixx → MPQ::unpack() → SFileFindFirstFile / SFileExtractFile
 * We use mdx-m3-viewer MPQ (same format) as a portable fallback when StormLib
 * is not built; hierarchy idea matches hierarchy.ixx (map files first, then game).
 *
 * Usage: node extract_into_project.cjs
 */
const fs = require('fs');
const path = require('path');
const MpqArchive = require('mdx-m3-viewer/dist/cjs/parsers/mpq/archive.js').default;

const WAR3_INSTALL = '/path/to/Warcraft.III.R';
const PROJECT = path.resolve(__dirname, '../..');
const GAME_OUT = path.join(PROJECT, 'war3/game');
const MAP_OUT = path.join(PROJECT, 'war3/maps/Adrenaline');
const MAP_SRC = path.join(WAR3_INSTALL, 'Maps/(4)Adrenaline.w3m');

const MPQS = ['war3.mpq', 'War3x.mpq', 'War3xLocal.mpq', 'War3Patch.mpq'];

const KEEP_PREFIXES = [
  'TerrainArt/',
  'ReplaceableTextures/',
  'Textures/',
  'Units/',
  'Doodads/',
  'Buildings/',
  'buildings/',
  'Splats/',
  'UI/MiscData.txt',
  'UI/TerrainInfo.txt',
];

function loadMpq(filePath) {
  const buf = fs.readFileSync(filePath);
  const copy = new Uint8Array(buf.byteLength);
  copy.set(buf);
  const a = new MpqArchive();
  a.load(copy, true);
  return a;
}

function shouldKeep(name) {
  const n = name.replace(/\\/g, '/');
  if (n === '(listfile)' || n === '(attributes)' || n === '(signature)') return false;
  // skip audio / movies / UI chrome heavy junk under Units sounds
  if (/^Sound\//i.test(n) || /^Movies\//i.test(n)) return false;
  if (/^Units\/.*\.(wav|mp3)$/i.test(n)) return false;
  return KEEP_PREFIXES.some((p) => n.toLowerCase().startsWith(p.toLowerCase()) || n === p);
}

function writeFile(outRoot, name, bytes) {
  const rel = name.replace(/\\/g, '/');
  const dest = path.join(outRoot, rel);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.writeFileSync(dest, Buffer.from(bytes));
}

function extractArchive(mpqPath, outRoot, filterFn) {
  console.log('Loading', mpqPath);
  const a = loadMpq(mpqPath);
  const names = a.getFileNames();
  let ok = 0, fail = 0, skip = 0;
  for (const name of names) {
    if (filterFn && !filterFn(name)) {
      skip++;
      continue;
    }
    try {
      const f = a.get(name);
      if (!f) {
        fail++;
        continue;
      }
      writeFile(outRoot, name, f.bytes());
      ok++;
      if (ok % 500 === 0) console.log('  wrote', ok, '…');
    } catch (e) {
      fail++;
      if (fail < 15) console.warn('  fail', name, e.message);
    }
  }
  console.log('Done', path.basename(mpqPath), 'ok=', ok, 'fail=', fail, 'skip=', skip);
}

function extractMap(mapPath, outRoot) {
  console.log('Extracting map', mapPath);
  fs.mkdirSync(outRoot, { recursive: true });
  const a = loadMpq(mapPath);
  for (const name of a.getFileNames()) {
    if (name === '(listfile)' || name === '(attributes)') continue;
    try {
      const f = a.get(name);
      if (!f) continue;
      writeFile(outRoot, name, f.bytes());
      console.log('  map', name, f.bytes().length);
    } catch (e) {
      console.warn('  map fail', name, e.message);
    }
  }
}

function main() {
  fs.mkdirSync(GAME_OUT, { recursive: true });
  for (const mpq of MPQS) {
    const p = path.join(WAR3_INSTALL, mpq);
    if (!fs.existsSync(p)) {
      console.warn('missing', p);
      continue;
    }
    extractArchive(p, GAME_OUT, shouldKeep);
  }
  extractMap(MAP_SRC, MAP_OUT);

  // Write project-local defaults for Godot
  const cfgPath = path.join(PROJECT, 'war3/local_paths.json');
  fs.writeFileSync(
    cfgPath,
    JSON.stringify(
      {
        asset_root: 'res://war3/game',
        default_map_path: 'res://war3/maps/Adrenaline',
        source_install: WAR3_INSTALL,
        source_map: MAP_SRC,
      },
      null,
      2
    )
  );
  console.log('Wrote', cfgPath);
  console.log('game size hint: du -sh', GAME_OUT);
}

main();
