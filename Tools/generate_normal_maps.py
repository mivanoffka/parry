"""Generate linear RGB normal maps matching DemoArt's code-drawn sprites.
Run from any directory with Python 3; no image libraries required.
"""
from pathlib import Path
import math
import struct
import zlib

OUTPUT = Path(__file__).resolve().parents[1] / 'Assets/Resources/Combat'


def height(kind, x, y):
    dx = x - (7.5 if kind == 'sword' else 31.5)
    if kind == 'sword':
        if 18 <= y < 61 and abs(dx) <= ((62-y)*.5 if y > 54 else 3.5):
            return .2 + .4 * max(0, 1 - abs(dx) / 4)
        if 14 <= y <= 18 and 1 <= x <= 14:
            return .22
        if 3 <= y < 14 and 6 <= x <= 9:
            return .13 + .03 * math.cos(y * math.pi * 2 / 3)
        return 0
    if kind == 'target':
        dome = dx*dx/370 + (y-37)*(y-37)/440
        if dome < 1:
            ring = math.hypot(dx, y-38)
            return .13 + .4*math.sqrt(1-dome) + (.035 if 11 < ring < 15 or ring < 6 else 0)
        return .06 if abs(dx) < 4 and y < 35 else 0
    # Head dome, muzzle and recessed eyes. Head overrides the cloak below it.
    dome = dx*dx/440 + (y-41)*(y-41)/270
    if dome < 1:
        h = .22 + .55*math.sqrt(1-dome)
        if 38 < y < 46 and (abs(x-25) < 3 or abs(x-39) < 3):
            h -= .055
        return h
    if 49 < y < 60 and (abs(x-16) < 5 or abs(x-47) < 5):
        return .18
    if 5 < y < 30 and abs(dx) < 10 + (30-y)*.35:
        width = 10 + (30-y)*.35
        return .15 + .18*max(0, 1-(dx/width)**2) + .035*math.cos(dx*.6)
    if 2 < y < 9 and (abs(x-23) < 5 or abs(x-41) < 5):
        return .1
    return 0


def normal(kind, x, y):
    if height(kind, x, y) <= 0:
        return (0, 0, 1)
    center = height(kind, x, y)
    def sample(sx, sy):
        # Avoid a steep false bevel at transparent silhouette pixels.
        value = height(kind, sx, sy)
        return value if value > 0 else center
    nx = -(sample(x+1, y)-sample(x-1, y))*4
    ny = -(sample(x, y+1)-sample(x, y-1))*4
    inv = 1/math.sqrt(nx*nx+ny*ny+1)
    return (nx*inv, ny*inv, inv)


def png_chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind+data) & 0xffffffff)


def generate():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for kind in ('player', 'target', 'sword'):
        width, height_px = (16 if kind == 'sword' else 64), 64
        rows = bytearray()
        for y in reversed(range(height_px)):  # PNG is top-down; Unity sprite UVs are bottom-up.
            rows.append(0)
            for x in range(width):
                n = normal(kind, x, y)
                assert abs(sum(v*v for v in n)-1) < 1e-6 and n[2] > 0
                rows.extend(round((v*.5+.5)*255) for v in n)
                rows.append(255)
        png = b'\x89PNG\r\n\x1a\n'
        png += png_chunk(b'IHDR', struct.pack('>IIBBBBB', width, height_px, 8, 6, 0, 0, 0))
        png += png_chunk(b'IDAT', zlib.compress(bytes(rows), 9)) + png_chunk(b'IEND', b'')
        path = OUTPUT / f'{kind}_normal.png'
        path.write_bytes(png)
        print(f'{path.name}: {width}x{height_px}, linear RGB normals validated')


if __name__ == '__main__':
    generate()
