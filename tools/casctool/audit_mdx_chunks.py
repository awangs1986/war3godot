#!/usr/bin/env python3
"""Report non-mesh MDX chunks used by a Reforged model manifest."""
from __future__ import annotations

import argparse
import struct
from collections import Counter, defaultdict
from pathlib import Path


INTERESTING = {b"ATCH", b"PREM", b"PRE2", b"RIBB", b"EVTS", b"LITE", b"CAMS", b"CLID", b"TXAN"}


def chunks(data: bytes) -> list[str]:
	if data[:4] != b"MDLX":
		return []
	result: list[str] = []
	position = 4
	while position + 8 <= len(data):
		tag = data[position : position + 4]
		size = struct.unpack_from("<I", data, position + 4)[0]
		end = position + 8 + size
		if end > len(data):
			break
		if tag in INTERESTING:
			result.append(tag.decode("ascii"))
		position = end
	return result


def main() -> int:
	parser = argparse.ArgumentParser()
	parser.add_argument("manifest")
	parser.add_argument("root")
	args = parser.parse_args()
	root = Path(args.root)
	counts: Counter[str] = Counter()
	models: dict[str, list[str]] = defaultdict(list)
	missing = 0
	for raw in Path(args.manifest).read_text(encoding="utf-8-sig").splitlines():
		rel = raw.strip()
		if not rel or rel.startswith("#"):
			continue
		path = root / rel.replace("\\", "/")
		if not path.is_file():
			# Reforged commonly stores Foo.mdx where classic object data asks for
			# Foo0.mdx/Foo1.mdx variation names.
			stem = path.stem
			if stem and stem[-1].isdigit():
				fallback = path.with_name(stem.rstrip("0123456789") + path.suffix)
				if fallback.is_file():
					path = fallback
				else:
					missing += 1
					continue
			else:
				missing += 1
				continue
		found = chunks(path.read_bytes())
		for tag in found:
			counts[tag] += 1
			models[tag].append(rel)
	for tag in sorted(INTERESTING):
		name = tag.decode("ascii")
		print(f"MDX_CHUNK {name} models={counts[name]}")
		for rel in models[name]:
			print(f"  {rel}")
	print(f"MDX_CHUNK_AUDIT_OK missing={missing}")
	return 1 if missing else 0


if __name__ == "__main__":
	raise SystemExit(main())
