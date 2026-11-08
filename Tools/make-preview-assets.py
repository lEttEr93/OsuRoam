#!/usr/bin/env python3
"""生成预览用的内置贴图（Assets/Preview/*.png）。

这些贴图只是「默认皮肤」——以后要做皮肤支持，同名的 PNG 从皮肤目录读出来覆盖即可。
全部按可变尺寸设计：
  - note.png        单点（竖向拉长会显得糊，所以只按宽度缩放，高度固定比例裁切）
  - hold-body.png   长条身（左右 1px 是描边，中间 1px 列会被横向拉伸，纵向平铺）
  - hold-head.png   长条头
  - hold-tail.png   长条尾
  - key.png         列背景（纵向拉伸，顶部留一点渐变暗示按键区）
  - judge.png       判定线（横向平铺）
  - column-1..4.png 每列色调（叠加在 key 上，给不同列不同色）
"""

import pathlib
import struct
import zlib

W = 256
OUT = pathlib.Path(r"D:\AI\repos\osu-roam\Assets\Preview")


def write_png(path: pathlib.Path, width: int, height: int, pixels: list[list[tuple[int, int, int, int]]]):
    raw = bytearray()
    for row in pixels:
        raw.append(0)  # filter type 0
        for r, g, b, a in row:
            raw += bytes((r, g, b, a))

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    path.write_bytes(png)


def rounded_bar(width: int, height: int, body=(245, 249, 251), alpha=255, radius_ratio=0.35,
                edge=(255, 255, 255), glow=True):
    """一个带圆角、顶部略亮的横向条（单点音符的形状）。"""
    pixels = []
    radius = max(2, int(height * radius_ratio))
    for y in range(height):
        row = []
        for x in range(width):
            # 圆角：四个角按半径裁掉
            dx = min(x, width - 1 - x)
            dy = min(y, height - 1 - y)
            inside = True
            if dx < radius and dy < radius:
                inside = (radius - dx) ** 2 + (radius - dy) ** 2 <= radius ** 2
            if not inside:
                row.append((0, 0, 0, 0))
                continue
            # 竖向渐变：上亮下暗，缩放后仍有体积感
            t = y / max(1, height - 1)
            shade = 1.0 - 0.18 * t
            r = int(body[0] * shade)
            g = int(body[1] * shade)
            b = int(body[2] * shade)
            a = alpha
            # 上下各 1px 的亮边
            if y == 0 or y == height - 1:
                r, g, b = edge
                a = int(alpha * (0.55 if y else 0.85))
            row.append((r, g, b, a))
        pixels.append(row)
    return pixels


def solid(width: int, height: int, color, alpha=255):
    return [[(color[0], color[1], color[2], alpha) for _ in range(width)] for _ in range(height)]


def main():
    OUT.mkdir(parents=True, exist_ok=True)

    # 单点：宽 256 高 32，缩放时按比例
    write_png(OUT / "note.png", 256, 32, rounded_bar(256, 32))
    # 长条头（同样的形状，略矮一点更贴合）
    write_png(OUT / "hold-head.png", 256, 26, rounded_bar(256, 26, body=(232, 150, 172)))
    # 长条尾：薄一点
    write_png(OUT / "hold-tail.png", 256, 10, rounded_bar(256, 10, body=(251, 242, 245), radius_ratio=0.5))
    # 长条身：4px 宽（左右描边 + 中间可拉伸），上下可平铺
    body = solid(4, 8, (225, 81, 123))
    for y in range(8):
        body[y][0] = (168, 52, 88, 255)
        body[y][3] = (168, 52, 88, 255)
        body[y][1] = (232, 120, 150, 255)
        body[y][2] = (232, 120, 150, 255)
    write_png(OUT / "hold-body.png", 4, 8, body)
    # 列背景：竖直渐变，底部略亮
    key = []
    for y in range(64):
        t = y / 63
        alpha = int(26 + 34 * t)
        key.append([(255, 255, 255, alpha) for _ in range(4)])
    write_png(OUT / "key.png", 4, 64, key)
    # 判定线
    write_png(OUT / "judge.png", 8, 3, solid(8, 3, (140, 165, 180), 235))
    # 每列色调（极淡，叠加用）
    for index, color in enumerate([(255, 255, 255), (255, 255, 255), (255, 255, 255), (255, 255, 255)], start=1):
        write_png(OUT / f"column-{index}.png", 4, 4, solid(4, 4, color, 0))

    for path in sorted(OUT.glob("*.png")):
        print(f"{path.name:16} {path.stat().st_size:6d} B")


if __name__ == "__main__":
    main()
