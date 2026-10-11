#!/usr/bin/env python3
"""Offline import step: write .png sidecars for every JPEG-CMYK BLP1.

The Godot runtime cannot decode War3 "fake CMYK" (BGRA) JPEG BLPs and never
shells out to converters, so this must be run once after refreshing the
extracted asset tree:

    python tools/mpqtool/convert_cmyk_sidecars.py war3/game

Uses the same turbojpeg + R<->B swap as HiveWE (via blp_cmyk_to_png.py), which
is the only correct conversion; ImageMagick CMYK is color-managed and wrong.
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

from blp_cmyk_to_png import blp1_jpeg_to_png


def is_cmyk_jpeg_blp(path: Path) -> bool:
	try:
		with path.open("rb") as f:
			head = f.read(8)
			if len(head) < 8 or head[:4] != b"BLP1":
				return False
			content = struct.unpack_from("<I", head, 4)[0]
			return content == 0  # JPEG content (War3 JPEG BLPs are CMYK)
	except OSError:
		return False


def main() -> int:
	if len(sys.argv) != 2:
		print(__doc__, file=sys.stderr)
		return 2
	root = Path(sys.argv[1])
	if not root.is_dir():
		print(f"not a directory: {root}", file=sys.stderr)
		return 2
	converted = skipped = failed = 0
	for blp in sorted(root.rglob("*.blp")):
		if not is_cmyk_jpeg_blp(blp):
			skipped += 1
			continue
		png = blp.with_suffix(".png")
		try:
			blp1_jpeg_to_png(blp, png)
			converted += 1
		except Exception as exc:  # noqa: BLE001 - report and continue the batch
			failed += 1
			print(f"FAILED {blp}: {exc}", file=sys.stderr)
	print(f"converted={converted} non-jpeg-skipped={skipped} failed={failed}")
	return 1 if failed else 0


if __name__ == "__main__":
	raise SystemExit(main())
