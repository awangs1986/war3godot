#!/usr/bin/env python3
"""Prewarm Reforged MDX bundles listed one path per line."""
from __future__ import annotations

import argparse
from pathlib import Path

from extract_reforged import CascReader, extract_model_bundle


def main() -> int:
	parser = argparse.ArgumentParser()
	parser.add_argument("manifest", help="UTF-8 text file with one relative MDX path per line")
	parser.add_argument("output_root", help="Reforged cache root")
	parser.add_argument("--install", required=True, help="Path to the Warcraft III: Reforged 1.32.10.18820 installation")
	parser.add_argument("--dll-dir", default=r"tools\HiveWE\HiveWE 0.10")
	parser.add_argument("--tileset", default="L")
	args = parser.parse_args()

	paths = []
	for raw in Path(args.manifest).read_text(encoding="utf-8").splitlines():
		path = raw.strip()
		if path and not path.startswith("#"):
			paths.append(path)

	reader = CascReader(Path(args.install), Path(args.dll_dir).resolve())
	ok = 0
	classic_only = 0
	missing = 0
	failed = 0
	try:
		for index, model_path in enumerate(paths, 1):
			print(f"CASC_PREWARM_BEGIN {index}/{len(paths)} {model_path}", flush=True)
			try:
				resolved, _ = reader.read_hd(model_path, args.tileset)
			except FileNotFoundError:
				missing += 1
				print(f"CASC_PREWARM_MISSING {model_path}", flush=True)
				continue
			if ":_hd.w3mod:" not in resolved:
				classic_only += 1
				print(f"CASC_PREWARM_CLASSIC_ONLY {resolved}", flush=True)
				continue
			try:
				extract_model_bundle(reader, model_path, Path(args.output_root), args.tileset)
				ok += 1
			except Exception as exc:  # keep the rest of the map usable
				failed += 1
				print(f"CASC_PREWARM_FAILED {model_path}: {exc}", flush=True)
	finally:
		reader.close()
	print(
		f"CASC_PREWARM_DONE requested={len(paths)} hd={ok} "
		f"classic_only={classic_only} missing={missing} failed={failed}",
		flush=True,
	)
	return 1 if failed else 0


if __name__ == "__main__":
	raise SystemExit(main())
