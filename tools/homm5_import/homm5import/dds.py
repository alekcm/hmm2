"""Minimal dependency-free PNG writer + DDS decoder.

HoMM5 ships 9258 ``.dds`` files: DXT1/DXT3/DXT5 block-compressed and several
uncompressed layouts.  Unity imports PNG natively, so the importer converts.
Only the stdlib is used (zlib + struct), which keeps the pipeline installable
anywhere Python runs.
"""

from __future__ import annotations

import struct
import zlib
from typing import List, Optional, Tuple

DDS_MAGIC = b"DDS "
DDSD_LINEARSIZE = 0x00080000
DDPF_ALPHAPIXELS = 0x1
DDPF_FOURCC = 0x4


class DDSError(ValueError):
    pass


def _decode_bc1(data: bytes, width: int, height: int) -> bytearray:
    out = bytearray(width * height * 4)
    bw = (width + 3) // 4
    bh = (height + 3) // 4
    for by in range(bh):
        for bx in range(bw):
            o = (by * bw + bx) * 8
            if o + 8 > len(data):
                break
            c0, c1 = struct.unpack_from("<HH", data, o)
            pal = [_rgb565(c0), _rgb565(c1)]
            if c0 > c1:
                pal.append(_mix(pal[0], pal[1], 2, 1))
                pal.append(_mix(pal[0], pal[1], 1, 2))
            else:
                pal.append(_mix(pal[0], pal[1], 1, 1))
                pal.append((0, 0, 0, 0))
            for row in range(4):
                idx = data[o + 4 + row]
                for col in range(4):
                    px, py = bx * 4 + col, by * 4 + row
                    if px >= width or py >= height:
                        continue
                    r, g, b, a = pal[(idx >> (col * 2)) & 3]
                    p = (py * width + px) * 4
                    out[p] = r
                    out[p + 1] = g
                    out[p + 2] = b
                    out[p + 3] = a
    return out


def _rgb565(v: int) -> Tuple[int, int, int, int]:
    return (((v >> 11) & 31) * 255 + 15) // 31, (((v >> 5) & 63) * 255 + 31) // 63, ((v & 31) * 255 + 15) // 31, 255


def _mix(a: Tuple[int, int, int, int], b: Tuple[int, int, int, int], wa: int, wb: int) -> Tuple[int, int, int, int]:
    t = wa + wb
    return tuple((a[i] * wa + b[i] * wb) // t for i in range(4))  # type: ignore[return-value]


def _decode_bc2(data: bytes, width: int, height: int) -> bytearray:
    out = bytearray(width * height * 4)
    bw, bh = (width + 3) // 4, (height + 3) // 4
    for by in range(bh):
        for bx in range(bw):
            o = (by * bw + bx) * 16
            if o + 16 > len(data):
                break
            alpha_bits = int.from_bytes(data[o:o + 8], "little")
            c0, c1 = struct.unpack_from("<HH", data, o + 8)
            pal = [_rgb565(c0), _rgb565(c1), _mix(_rgb565(c0), _rgb565(c1), 2, 1), _mix(_rgb565(c0), _rgb565(c1), 1, 2)]
            for row in range(4):
                idx = data[o + 12 + row]
                for col in range(4):
                    px, py = bx * 4 + col, by * 4 + row
                    if px >= width or py >= height:
                        continue
                    r, g, b, _ = pal[(idx >> (col * 2)) & 3]
                    a = (alpha_bits >> ((row * 4 + col) * 4)) & 15
                    p = (py * width + px) * 4
                    out[p] = r
                    out[p + 1] = g
                    out[p + 2] = b
                    out[p + 3] = (a * 255) // 15
    return out


_BC3_ALPHA = [0, 255]


def _decode_bc3(data: bytes, width: int, height: int) -> bytearray:
    out = bytearray(width * height * 4)
    bw, bh = (width + 3) // 4, (height + 3) // 4
    for by in range(bh):
        for bx in range(bw):
            o = (by * bw + bx) * 16
            if o + 16 > len(data):
                break
            a0, a1 = data[o], data[o + 1]
            if a0 > a1:
                apal = [a0, a1] + [(a0 * (7 - i) + a1 * i) // 7 for i in range(1, 7)]
            else:
                apal = [a0, a1] + [(a0 * (5 - i) + a1 * i) // 5 for i in range(1, 5)] + [0, 255]
            aidx = int.from_bytes(data[o + 2:o + 8], "little")
            c0, c1 = struct.unpack_from("<HH", data, o + 8)
            pal = [_rgb565(c0), _rgb565(c1), _mix(_rgb565(c0), _rgb565(c1), 2, 1), _mix(_rgb565(c0), _rgb565(c1), 1, 2)]
            for row in range(4):
                idx = data[o + 12 + row]
                for col in range(4):
                    px, py = bx * 4 + col, by * 4 + row
                    if px >= width or py >= height:
                        continue
                    r, g, b, _ = pal[(idx >> (col * 2)) & 3]
                    a = apal[(aidx >> ((row * 4 + col) * 3)) & 7]
                    p = (py * width + px) * 4
                    out[p] = r
                    out[p + 1] = g
                    out[p + 2] = b
                    out[p + 3] = a
    return out


def decode_dds(data: bytes) -> Tuple[int, int, bytearray]:
    """Return (width, height, rgba_bytes).  Raises :class:`DDSError`."""
    if data[:4] != DDS_MAGIC:
        raise DDSError("not a DDS file")
    size, flags, height, width = struct.unpack_from("<IIII", data, 4)
    pitch = struct.unpack_from("<I", data, 20)[0]
    pf_size, pf_flags = struct.unpack_from("<II", data, 76)
    fourcc = data[84:88]
    bpp_bits, rmask, gmask, bmask, amask = struct.unpack_from("<IIIII", data, 88)
    offset = 128
    dxgi = None
    if fourcc == b"DX10":
        dxgi = struct.unpack_from("<I", data, 128)[0]
        offset = 152
    body = data[offset:]

    if fourcc == b"DXT1":
        return width, height, _decode_bc1(body, width, height)
    if fourcc in (b"DXT2", b"DXT3"):
        # DXT2 is BC2 with premultiplied alpha; the block layout is identical.
        return width, height, _decode_bc2(body, width, height)
    if fourcc in (b"DXT4", b"DXT5"):
        # DXT4 is BC3 with premultiplied alpha; the block layout is identical.
        return width, height, _decode_bc3(body, width, height)
    if dxgi is not None:
        # DXGI_FORMAT_BC1_UNORM 71, BC2 74, BC3 77
        if dxgi == 71:
            return width, height, _decode_bc1(body, width, height)
        if dxgi == 74:
            return width, height, _decode_bc2(body, width, height)
        if dxgi == 77:
            return width, height, _decode_bc3(body, width, height)
        raise DDSError("unsupported DXGI format %d" % dxgi)
    if fourcc in (b"ATI1", b"BC4U"):
        raise DDSError("single-channel BC4 not needed for game textures")
    if fourcc not in (b"\x00\x00\x00\x00", b"    "):
        raise DDSError("unsupported fourcc %r" % fourcc)

    # Uncompressed: reconstruct from the channel masks.
    bpp = (bpp_bits // 8) or 4
    out = bytearray(width * height * 4)
    masks = ((rmask, 8), (gmask, 8), (bmask, 8), (amask, 8))
    if bpp == 4 and (rmask, gmask, bmask, amask) == (0xFF0000, 0xFF00, 0xFF, 0xFF000000):
        return width, height, bytearray(_bgra_to_rgba(body, width * height))
    for i in range(width * height):
        o = i * bpp
        if o + bpp > len(body):
            break
        val = int.from_bytes(body[o:o + bpp], "little")
        p = i * 4
        for (mask, _bits), ch in zip(masks, range(4)):
            if not mask:
                out[p + ch] = 255 if ch == 3 else 0
                continue
            shift = (mask & -mask).bit_length() - 1
            bits = bin(mask).count("1")
            v = (val & mask) >> shift
            out[p + ch] = (v * 255) // ((1 << bits) - 1) if bits < 8 else v
    return width, height, out


def _bgra_to_rgba(body: bytes, count: int) -> bytearray:
    out = bytearray(count * 4)
    for i in range(count):
        o = i * 4
        if o + 4 > len(body):
            break
        b, g, r, a = body[o], body[o + 1], body[o + 2], body[o + 3]
        p = i * 4
        out[p], out[p + 1], out[p + 2], out[p + 3] = r, g, b, a
    return out


def write_png(path: str, width: int, height: int, rgba: bytes) -> None:
    raw = bytearray()
    stride = width * 4
    for y in range(height):
        raw.append(0)
        raw += rgba[y * stride:(y + 1) * stride]

    def chunk(tag: bytes, payload: bytes) -> bytes:
        return struct.pack(">I", len(payload)) + tag + payload + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    with open(path, "wb") as fh:
        fh.write(b"\x89PNG\r\n\x1a\n")
        fh.write(chunk(b"IHDR", ihdr))
        fh.write(chunk(b"IDAT", zlib.compress(bytes(raw), 9)))
        fh.write(chunk(b"IEND", b""))


def dds_to_png(src: str, dst: str) -> Tuple[int, int]:
    with open(src, "rb") as fh:
        w, h, rgba = decode_dds(fh.read())
    write_png(dst, w, h, bytes(rgba))
    return w, h
