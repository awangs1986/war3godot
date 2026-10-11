#!/usr/bin/env python3
"""Decode War3 BLP1 JPEG (fake CMYK = BGRA) → PNG32.

Matches HiveWE: libturbojpeg TJPF_CMYK then R↔B swap.
ImageMagick -colorspace CMYK is NOT equivalent (color-managed).
"""
from __future__ import annotations

import ctypes
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image

TJINIT_DECOMPRESS = 1
TJPARAM_JPEGWIDTH = 5
TJPARAM_JPEGHEIGHT = 6
TJPF_CMYK = 11

def _load_turbojpeg() -> ctypes.CDLL:
	"""Load the same TurboJPEG decoder used by HiveWE on Windows or Linux."""
	here = Path(__file__).resolve()
	candidates = [
		here.with_name("turbojpeg.dll"),
		here.parent.parent / "HiveWE" / "HiveWE 0.10" / "turbojpeg.dll",
		here.parent.parent / "HiveWE" / "HiveWE.0.10" / "HiveWE 0.10" / "turbojpeg.dll",
		Path("libturbojpeg.so.0"),
		Path("turbojpeg.dll"),
	]
	for candidate in candidates:
		try:
			return ctypes.CDLL(str(candidate))
		except OSError:
			continue
	raise OSError("TurboJPEG library not found; checked: " + ", ".join(map(str, candidates)))


_lib = _load_turbojpeg()
_lib.tj3Init.argtypes = [ctypes.c_int]
_lib.tj3Init.restype = ctypes.c_void_p
_lib.tj3Destroy.argtypes = [ctypes.c_void_p]
_lib.tj3GetErrorStr.argtypes = [ctypes.c_void_p]
_lib.tj3GetErrorStr.restype = ctypes.c_char_p
_lib.tj3DecompressHeader.argtypes = [
	ctypes.c_void_p,
	ctypes.POINTER(ctypes.c_ubyte),
	ctypes.c_size_t,
]
_lib.tj3DecompressHeader.restype = ctypes.c_int
_lib.tj3Get.argtypes = [ctypes.c_void_p, ctypes.c_int]
_lib.tj3Get.restype = ctypes.c_int
_lib.tj3Decompress8.argtypes = [
	ctypes.c_void_p,
	ctypes.POINTER(ctypes.c_ubyte),
	ctypes.c_size_t,
	ctypes.POINTER(ctypes.c_ubyte),
	ctypes.c_int,
	ctypes.c_int,
]
_lib.tj3Decompress8.restype = ctypes.c_int


def jpeg_cmyk_to_rgba(jpg: bytes, out_w: int, out_h: int) -> Image.Image:
	handle = _lib.tj3Init(TJINIT_DECOMPRESS)
	if not handle:
		raise RuntimeError("tj3Init failed")
	try:
		src = (ctypes.c_ubyte * len(jpg)).from_buffer_copy(jpg)
		if _lib.tj3DecompressHeader(handle, src, len(jpg)) != 0:
			raise RuntimeError(_lib.tj3GetErrorStr(handle).decode())
		w = _lib.tj3Get(handle, TJPARAM_JPEGWIDTH)
		h = _lib.tj3Get(handle, TJPARAM_JPEGHEIGHT)
		buf = (ctypes.c_ubyte * (w * h * 4))()
		if _lib.tj3Decompress8(handle, src, len(jpg), buf, 0, TJPF_CMYK) != 0:
			raise RuntimeError(_lib.tj3GetErrorStr(handle).decode())
		arr = np.ctypeslib.as_array(buf).reshape((h, w, 4)).copy()
		# BGRA → RGBA
		rgba = arr[:, :, [2, 1, 0, 3]]
		img = Image.fromarray(rgba, "RGBA")
		if img.size != (out_w, out_h):
			img = img.resize((out_w, out_h), Image.Resampling.NEAREST)
		return img
	finally:
		_lib.tj3Destroy(handle)


def blp1_jpeg_to_png(blp_path: Path, png_path: Path) -> None:
	data = blp_path.read_bytes()
	if data[:4] != b"BLP1":
		raise ValueError("not BLP1")
	content, _alpha_bits, width, height = struct.unpack_from("<IIII", data, 4)
	if content != 0:
		raise ValueError("not JPEG BLP")
	mip_off = struct.unpack_from("<I", data, 28)[0]
	mip_sz = struct.unpack_from("<I", data, 28 + 64)[0]
	hdr_sz = struct.unpack_from("<I", data, 156)[0]
	if hdr_sz <= 0 or mip_off + mip_sz > len(data):
		raise ValueError("corrupt mip")
	jpg = data[160 : 160 + hdr_sz] + data[mip_off : mip_off + mip_sz]
	img = jpeg_cmyk_to_rgba(jpg, width, height)
	png_path.parent.mkdir(parents=True, exist_ok=True)
	img.save(png_path, format="PNG")


def main() -> int:
	if len(sys.argv) < 3:
		print("usage: blp_cmyk_to_png.py <in.blp|in.jpg> <out.png> [w] [h]", file=sys.stderr)
		return 2
	src = Path(sys.argv[1])
	dst = Path(sys.argv[2])
	if src.suffix.lower() == ".blp":
		blp1_jpeg_to_png(src, dst)
	else:
		w = int(sys.argv[3]) if len(sys.argv) > 3 else 0
		h = int(sys.argv[4]) if len(sys.argv) > 4 else 0
		jpg = src.read_bytes()
		if w <= 0 or h <= 0:
			# probe via turbojpeg header
			handle = _lib.tj3Init(TJINIT_DECOMPRESS)
			src_b = (ctypes.c_ubyte * len(jpg)).from_buffer_copy(jpg)
			_lib.tj3DecompressHeader(handle, src_b, len(jpg))
			w = _lib.tj3Get(handle, TJPARAM_JPEGWIDTH)
			h = _lib.tj3Get(handle, TJPARAM_JPEGHEIGHT)
			_lib.tj3Destroy(handle)
		jpeg_cmyk_to_rgba(jpg, w, h).save(dst, format="PNG")
	return 0


if __name__ == "__main__":
	raise SystemExit(main())
