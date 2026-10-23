"""Generates src/Tempo.App/Assets/Tempo.ico (32-bit DIB frames, no dependencies)."""
import struct, sys, pathlib

SIZES = [16, 20, 24, 32, 40, 48, 64, 128]
SS = 4  # supersampling factor per axis

def inside_round_rect(x, y, x0, y0, x1, y1, r):
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r

def sample(u, v):
    """u, v in [0,1]. Returns premultiplied-free RGBA floats."""
    if not inside_round_rect(u, v, 0.02, 0.02, 0.98, 0.98, 0.24):
        return None
    # Diagonal gradient: indigo -> violet -> pink.
    t = (u + v) / 2
    c0, c1 = (79, 70, 229), (192, 38, 211)
    col = tuple(c0[i] + (c1[i] - c0[i]) * t for i in range(3))
    # The "T": a top bar and a stem, slightly rounded.
    bar = inside_round_rect(u, v, 0.24, 0.24, 0.76, 0.38, 0.06)
    stem = inside_round_rect(u, v, 0.43, 0.24, 0.57, 0.78, 0.06)
    # A small accent dot, hinting at a timeline.
    dot = (u - 0.72) ** 2 + (v - 0.70) ** 2 <= 0.075 ** 2
    if bar or stem or dot:
        col = (255, 255, 255)
    return col

def render(size):
    rows = []
    for py in range(size):
        row = []
        for px in range(size):
            r = g = b = a = 0.0
            for sy in range(SS):
                for sx in range(SS):
                    c = sample((px + (sx + 0.5) / SS) / size, (py + (sy + 0.5) / SS) / size)
                    if c:
                        r += c[0]; g += c[1]; b += c[2]; a += 1
            n = SS * SS
            if a:
                row.append((round(b / a), round(g / a), round(r / a), round(255 * a / n)))
            else:
                row.append((0, 0, 0, 0))
        rows.append(row)
    return rows

def dib(size):
    rows = render(size)
    header = struct.pack('<IiiHHIIiiII', 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = b''.join(bytes(c for px in row for c in px) for row in reversed(rows))
    mask_stride = ((size + 31) // 32) * 4
    mask = bytes(mask_stride * size)
    return header + pixels + mask

def main(out):
    frames = [dib(s) for s in SIZES]
    data = struct.pack('<HHH', 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    for s, f in zip(SIZES, frames):
        dim = 0 if s >= 256 else s
        data += struct.pack('<BBBBHHII', dim, dim, 0, 0, 1, 32, len(f), offset)
        offset += len(f)
    data += b''.join(frames)
    pathlib.Path(out).write_bytes(data)

if __name__ == '__main__':
    main(sys.argv[1])
