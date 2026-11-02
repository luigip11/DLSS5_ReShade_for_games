"""MIT. Original GamePanel material for the DirectX pilot, with a complete BC1 mip chain.
No game texture is read, named, identified or redistributed by this builder.
"""
import argparse
import hashlib
from pathlib import Path
import struct
import numpy as np
from generate_asphalt import asphalt, bc1


def build(output):
    if np.__version__ != "2.3.5":
        raise RuntimeError("Reproducible sample requires NumPy 2.3.5")
    pixels = asphalt(22012)
    height, width, _ = pixels.shape
    levels = []
    while True:
        h, w, _ = pixels.shape
        padded = np.pad(pixels, ((0, max(0, 4-h)), (0, max(0, 4-w)), (0, 0)), mode="edge")
        levels.append(bc1(padded))
        if h == 1 and w == 1:
            break
        pixels = ((pixels[::2, ::2].astype("uint16") + pixels[1::2, ::2]
                   + pixels[::2, 1::2] + pixels[1::2, 1::2]) // 4).astype("uint8")
    header = bytearray(128)
    header[:4] = b"DDS "
    struct.pack_into("<7I", header, 4, 124, 0xA1007, height, width, width*height//2, 0, len(levels))
    struct.pack_into("<II4s5I", header, 76, 32, 4, b"DXT1", 0, 0, 0, 0, 0)
    struct.pack_into("<I", header, 108, 0x401008)
    output.mkdir(parents=True, exist_ok=True)
    data = header + b"".join(levels)
    (output / "Asphalt.dds").write_bytes(data)
    (output / "LICENSE.txt").write_text(
        "Original procedural asphalt by GamePanel contributors, 2026.\n"
        "MIT License. See the application's LICENSE file.\n"
        "One material for an explicitly selected captured surface; no geometry replacement.\n",
        encoding="utf-8", newline="\n")
    print(f"Original DirectX sample: {width}x{height}, {len(levels)} mips, {len(data)} bytes, SHA256 {hashlib.sha256(data).hexdigest()}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    build(parser.parse_args().output)
