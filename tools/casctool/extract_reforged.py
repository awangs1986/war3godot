#!/usr/bin/env python3
"""Read approved Warcraft III 1.32 CASC files with HiveWE's CascLib.

The lookup prefixes mirror HiveWE Hierarchy::open_file. This utility extracts
only explicitly requested files and never modifies the Warcraft installation.
"""
from __future__ import annotations

import argparse
import ctypes
import os
import struct
from pathlib import Path


CASC_LOCALE_ALL = 0xFFFFFFFF
CASC_OPEN_BY_NAME = 0
INVALID_SIZE = 0xFFFFFFFF

REPLACEABLE_PATHS = {
	1: "ReplaceableTextures/TeamColor/TeamColor00",
	2: "ReplaceableTextures/TeamGlow/TeamGlow00",
	11: "ReplaceableTextures/Cliff/Cliff0",
}


class CascReader:
	def __init__(self, install: Path, dll_dir: Path) -> None:
		self._dll_cookie = os.add_dll_directory(str(dll_dir))
		self.lib = ctypes.WinDLL(str(dll_dir / "CascLib.dll"), use_last_error=True)
		self._bind()
		self.handle = ctypes.c_void_p()
		storage_path = str(install / ":w3")
		if not self.lib.CascOpenStorage(storage_path, CASC_LOCALE_ALL, ctypes.byref(self.handle)):
			raise OSError(f"CascOpenStorage failed ({self.lib.GetCascError()}): {storage_path}")

	def _bind(self) -> None:
		self.lib.CascOpenStorage.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.POINTER(ctypes.c_void_p)]
		self.lib.CascOpenStorage.restype = ctypes.c_bool
		self.lib.CascOpenFile.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.POINTER(ctypes.c_void_p)]
		self.lib.CascOpenFile.restype = ctypes.c_bool
		self.lib.CascGetFileSize.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_uint32)]
		self.lib.CascGetFileSize.restype = ctypes.c_uint32
		self.lib.CascReadFile.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32, ctypes.POINTER(ctypes.c_uint32)]
		self.lib.CascReadFile.restype = ctypes.c_bool
		self.lib.CascCloseFile.argtypes = [ctypes.c_void_p]
		self.lib.CascCloseFile.restype = ctypes.c_bool
		self.lib.CascCloseStorage.argtypes = [ctypes.c_void_p]
		self.lib.CascCloseStorage.restype = ctypes.c_bool
		self.lib.GetCascError.argtypes = []
		self.lib.GetCascError.restype = ctypes.c_uint32

	def close(self) -> None:
		if self.handle:
			self.lib.CascCloseStorage(self.handle)
			self.handle = ctypes.c_void_p()
		self._dll_cookie.close()

	def read_exact(self, casc_path: str) -> bytes | None:
		file_handle = ctypes.c_void_p()
		name = ctypes.c_char_p(casc_path.encode("utf-8"))
		if not self.lib.CascOpenFile(self.handle, name, 0, CASC_OPEN_BY_NAME, ctypes.byref(file_handle)):
			return None
		try:
			high = ctypes.c_uint32()
			size = self.lib.CascGetFileSize(file_handle, ctypes.byref(high))
			if size == INVALID_SIZE or high.value != 0:
				return None
			buffer = ctypes.create_string_buffer(size)
			read = ctypes.c_uint32()
			if not self.lib.CascReadFile(file_handle, buffer, size, ctypes.byref(read)) or read.value != size:
				raise OSError(f"CascReadFile failed ({self.lib.GetCascError()}): {casc_path}")
			return buffer.raw
		finally:
			self.lib.CascCloseFile(file_handle)

	def read_hd(self, relative_path: str, tileset: str = "L") -> tuple[str, bytes]:
		normalized = relative_path.replace("\\", "/").lstrip("/")
		candidates = [
			f"war3.w3mod:_hd.w3mod:_tilesets/{tileset}.w3mod:{normalized}",
			f"war3.w3mod:_hd.w3mod:{normalized}",
			f"war3.w3mod:{normalized}",
			f"war3.w3mod:_deprecated.w3mod:{normalized}",
		]
		for candidate in candidates:
			data = self.read_exact(candidate)
			if data is not None:
				return candidate, data
		raise FileNotFoundError(relative_path)


def _mdx_textures(data: bytes) -> list[tuple[int, str]]:
	if data[:4] != b"MDLX":
		raise ValueError("model is not MDX")
	position = 4
	while position + 8 <= len(data):
		tag = data[position : position + 4]
		size = struct.unpack_from("<I", data, position + 4)[0]
		payload = position + 8
		if tag == b"TEXS":
			result: list[tuple[int, str]] = []
			for offset in range(payload, payload + size, 268):
				if offset + 268 > payload + size:
					break
				replaceable = struct.unpack_from("<I", data, offset)[0]
				path = data[offset + 4 : offset + 264].split(b"\0", 1)[0].decode("utf-8", "replace")
				result.append((replaceable, path.replace("\\", "/")))
			return result
		position = payload + size
	return []


def _dds_path(path: str) -> str:
	stem = path.rsplit(".", 1)[0] if "." in path.rsplit("/", 1)[-1] else path
	return stem + ".dds"


def extract_model_bundle(reader: CascReader, model_path: str, output_root: Path, tileset: str) -> None:
	resolved, model_data = reader.read_hd(model_path, tileset)
	model_output = output_root / model_path.replace("\\", "/")
	model_output.parent.mkdir(parents=True, exist_ok=True)
	model_output.write_bytes(model_data)
	print(f"CASC_BUNDLE_MODEL {resolved} -> {model_output} ({len(model_data)} bytes)")
	seen: set[str] = set()
	for replaceable, source_path in _mdx_textures(model_data):
		if replaceable:
			source_path = REPLACEABLE_PATHS.get(replaceable, "")
		if not source_path:
			continue
		# Reforged cliff MDX files intentionally carry six authored placeholder
		# paths below `#ignore_Cliffs`; the terrain renderer supplies the cliff
		# replaceable texture instead. They are not real CASC resources.
		if any(part.lower().startswith("#ignore_") for part in source_path.split("/")):
			continue
		candidate = _dds_path(source_path)
		if candidate.lower() in seen:
			continue
		seen.add(candidate.lower())
		try:
			texture_resolved, texture_data = reader.read_hd(candidate, tileset)
		except FileNotFoundError:
			# Mirrors Warsmash: prefer DDS for a Reforged model, then retain the
			# authored extension when no DDS counterpart exists.
			texture_resolved, texture_data = reader.read_hd(source_path, tileset)
			candidate = source_path
		texture_output = output_root / candidate
		texture_output.parent.mkdir(parents=True, exist_ok=True)
		texture_output.write_bytes(texture_data)
		print(f"CASC_BUNDLE_TEXTURE {texture_resolved} -> {texture_output} ({len(texture_data)} bytes)")


def main() -> int:
	parser = argparse.ArgumentParser()
	parser.add_argument("relative_path", nargs="?")
	parser.add_argument("output")
	parser.add_argument("--install", required=True, help="Path to the Warcraft III: Reforged 1.32.10.18820 installation")
	parser.add_argument("--dll-dir", default=r"tools\HiveWE\HiveWE 0.10")
	parser.add_argument("--tileset", default="L")
	parser.add_argument("--bundle", action="store_true", help="extract an MDX and every referenced HD texture")
	parser.add_argument("--list-file", help="extract every relative path in a text file below the output root")
	args = parser.parse_args()
	if not args.relative_path and not args.list_file:
		parser.error("relative_path or --list-file is required")
	reader = CascReader(Path(args.install), Path(args.dll_dir).resolve())
	try:
		if args.list_file:
			output_root = Path(args.output)
			count = 0
			for raw in Path(args.list_file).read_text(encoding="utf-8-sig").splitlines():
				relative = raw.strip()
				if not relative or relative.startswith("#"):
					continue
				resolved, data = reader.read_hd(relative, args.tileset)
				output = output_root / relative.replace("\\", "/")
				output.parent.mkdir(parents=True, exist_ok=True)
				output.write_bytes(data)
				count += 1
				print(f"CASC_LIST_ASSET {resolved} -> {output} ({len(data)} bytes)")
			print(f"CASC_LIST_OK count={count}")
			return 0
		if args.bundle:
			extract_model_bundle(reader, args.relative_path, Path(args.output), args.tileset)
			return 0
		resolved, data = reader.read_hd(args.relative_path, args.tileset)
	finally:
		reader.close()
	output = Path(args.output)
	output.parent.mkdir(parents=True, exist_ok=True)
	output.write_bytes(data)
	print(f"CASC_EXTRACT_OK {resolved} -> {output} ({len(data)} bytes)")
	return 0


if __name__ == "__main__":
	raise SystemExit(main())
