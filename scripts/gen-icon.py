"""Generate DSPlayer flat taskbar icons: A (play), B (DS monogram), C (broadcast lens)."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

OUT = Path("assets/icon")
SIZE = 256
BG_BLUE = (37, 99, 235, 255)       # #2563EB
BG_DARK = (15, 23, 42, 255)        # #0F172A slate-ish dark
FG_WHITE = (255, 255, 255, 255)
FG_CYAN = (14, 165, 233, 255)      # #0EA5E9
RADIUS = int(SIZE * 0.22)

# A play triangle scale (larger for taskbar readability)
# Previous: 0.34 x 0.40  →  now ~0.46 x 0.54
A_TRI_W = 0.46
A_TRI_H = 0.54


def rounded_rect_mask(size: int, radius: int) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        (0, 0, size - 1, size - 1), radius=radius, fill=255
    )
    return mask


def base_tile(bg: tuple[int, int, int, int] = BG_BLUE) -> Image.Image:
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    tile = Image.new("RGBA", (SIZE, SIZE), bg)
    img.paste(tile, (0, 0), rounded_rect_mask(SIZE, RADIUS))
    return img


def save_png(img: Image.Image, path: Path) -> None:
    img.save(path, "PNG")
    print(f"Wrote {path} {img.size}")


def play_triangle_points(
    size: float,
    tri_w_frac: float,
    tri_h_frac: float,
    cx_frac: float = 0.52,
    cy_frac: float = 0.50,
) -> list[tuple[float, float]]:
    tri_w, tri_h = size * tri_w_frac, size * tri_h_frac
    cx, cy = size * cx_frac, size * cy_frac
    left = cx - tri_w * 0.38
    right = left + tri_w
    top, bottom = cy - tri_h / 2, cy + tri_h / 2
    return [(left, top), (right, cy), (left, bottom)]


def load_bold_font(px: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = [
        r"C:\Windows\Fonts\segoeuib.ttf",
        r"C:\Windows\Fonts\arialbd.ttf",
        r"C:\Windows\Fonts\calibrib.ttf",
        r"C:\Windows\Fonts\seguisb.ttf",
    ]
    for p in candidates:
        if Path(p).exists():
            return ImageFont.truetype(p, px)
    return ImageFont.load_default()


# --- A: Play (larger triangle) ---
def make_a_png() -> Image.Image:
    img = base_tile(BG_BLUE)
    d = ImageDraw.Draw(img)
    pts = play_triangle_points(float(SIZE), A_TRI_W, A_TRI_H)
    d.polygon(pts, fill=FG_WHITE)
    return img


def make_a_svg() -> str:
    r = RADIUS
    pts = play_triangle_points(256.0, A_TRI_W, A_TRI_H)
    poly = " ".join(f"{x:.2f},{y:.2f}" for x, y in pts)
    return f'''<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256" fill="none">
  <!-- A: Play on Tile (large triangle) -->
  <rect x="0" y="0" width="256" height="256" rx="{r}" ry="{r}" fill="#2563EB"/>
  <polygon points="{poly}" fill="#FFFFFF"/>
</svg>
'''


# --- B: DS monogram ---
def make_b_png(
    bg: tuple[int, int, int, int] = BG_BLUE,
    fg: tuple[int, int, int, int] = FG_WHITE,
) -> Image.Image:
    img = base_tile(bg)
    d = ImageDraw.Draw(img)
    font = load_bold_font(118)
    text = "DS"
    bbox = d.textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    x = (SIZE - tw) / 2 - bbox[0]
    y = (SIZE - th) / 2 - bbox[1] - SIZE * 0.02
    d.text((x, y), text, font=font, fill=fg)
    return img


def make_b_svg(bg_hex: str = "#2563EB", fg_hex: str = "#FFFFFF", note: str = "B: DS Monogram") -> str:
    r = RADIUS
    return f'''<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256" fill="none">
  <!-- {note} -->
  <rect x="0" y="0" width="256" height="256" rx="{r}" ry="{r}" fill="{bg_hex}"/>
  <text x="128" y="128" text-anchor="middle" dominant-baseline="central"
        font-family="Segoe UI, Arial, Helvetica, sans-serif" font-weight="700"
        font-size="118" fill="{fg_hex}" letter-spacing="-4">DS</text>
</svg>
'''


# --- C: Broadcast lens ---
def make_c_png() -> Image.Image:
    img = base_tile(BG_BLUE)
    d = ImageDraw.Draw(img)
    margin = SIZE * 0.20
    screen = [margin, SIZE * 0.24, SIZE - margin, SIZE * 0.76]
    stroke = max(10, int(SIZE * 0.055))
    d.rounded_rectangle(screen, radius=int(SIZE * 0.06), outline=FG_WHITE, width=stroke)
    pts = play_triangle_points(float(SIZE), 0.20, 0.24, cx_frac=0.515)
    d.polygon(pts, fill=FG_WHITE)
    pip_r = max(5, int(SIZE * 0.028))
    d.ellipse(
        [SIZE * 0.72, SIZE * 0.14, SIZE * 0.72 + pip_r * 2, SIZE * 0.14 + pip_r * 2],
        fill=FG_WHITE,
    )
    d.ellipse(
        [SIZE * 0.80, SIZE * 0.14, SIZE * 0.80 + pip_r * 2, SIZE * 0.14 + pip_r * 2],
        fill=FG_WHITE,
    )
    return img


def make_c_svg() -> str:
    r = RADIUS
    size = 256.0
    margin = size * 0.20
    stroke = size * 0.055
    scr_r = size * 0.06
    x1, y1 = margin, size * 0.24
    w = size - 2 * margin
    h = size * 0.52
    pts = play_triangle_points(size, 0.20, 0.24, cx_frac=0.515)
    poly = " ".join(f"{x:.2f},{y:.2f}" for x, y in pts)
    pip_r = size * 0.028
    return f'''<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256" fill="none">
  <!-- C: Broadcast Lens -->
  <rect x="0" y="0" width="256" height="256" rx="{r}" ry="{r}" fill="#2563EB"/>
  <rect x="{x1:.2f}" y="{y1:.2f}" width="{w:.2f}" height="{h:.2f}"
        rx="{scr_r:.2f}" ry="{scr_r:.2f}" fill="none" stroke="#FFFFFF" stroke-width="{stroke:.2f}"/>
  <polygon points="{poly}" fill="#FFFFFF"/>
  <circle cx="{size * 0.72 + pip_r:.2f}" cy="{size * 0.14 + pip_r:.2f}" r="{pip_r:.2f}" fill="#FFFFFF"/>
  <circle cx="{size * 0.80 + pip_r:.2f}" cy="{size * 0.14 + pip_r:.2f}" r="{pip_r:.2f}" fill="#FFFFFF"/>
</svg>
'''




def write_multi_ico(path: Path, base: Image.Image, sizes: list[tuple[int, int]] | None = None) -> None:
    """Multi-size ICO with PNG entries (Vista+)."""
    import io, struct
    if sizes is None:
        sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    entries: list[tuple[int, int, bytes]] = []
    for w, h in sizes:
        buf = io.BytesIO()
        base.resize((w, h), Image.Resampling.LANCZOS).save(buf, format="PNG")
        entries.append((w, h, buf.getvalue()))
    count = len(entries)
    header = struct.pack("<HHH", 0, 1, count)
    offset = 6 + 16 * count
    dir_entries = b""
    payloads = b""
    for w, h, data in entries:
        wb = 0 if w >= 256 else w
        hb = 0 if h >= 256 else h
        dir_entries += struct.pack("<BBBBHHII", wb, hb, 0, 0, 1, 32, len(data), offset)
        payloads += data
        offset += len(data)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(header + dir_entries + payloads)
    print(f"Wrote {path} ({path.stat().st_size} bytes)")

def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)

    # A — larger play triangle
    a = make_a_png()
    save_png(a, OUT / "dsplayer-icon-a-256.png")
    save_png(a, OUT / "dsplayer-icon-256.png")
    svg_a = make_a_svg()
    (OUT / "dsplayer-icon-a.svg").write_text(svg_a, encoding="utf-8")
    (OUT / "dsplayer-icon.svg").write_text(svg_a, encoding="utf-8")

    # Multi-size ICO for app embed (exe + windows)
    write_multi_ico(OUT / "dsplayer.ico", a)
    write_multi_ico(Path("src/DSPlayer/Assets/dsplayer.ico"), a)
    print("Wrote A SVG")

    # B — blue (original)
    b = make_b_png(BG_BLUE, FG_WHITE)
    save_png(b, OUT / "dsplayer-icon-b-256.png")
    (OUT / "dsplayer-icon-b.svg").write_text(make_b_svg(), encoding="utf-8")
    print("Wrote B SVG (blue)")

    # B — dark + cyan
    b_dark = make_b_png(BG_DARK, FG_CYAN)
    save_png(b_dark, OUT / "dsplayer-icon-b-dark-cyan-256.png")
    (OUT / "dsplayer-icon-b-dark-cyan.svg").write_text(
        make_b_svg("#0F172A", "#0EA5E9", "B: DS Monogram (dark + cyan)"),
        encoding="utf-8",
    )
    print("Wrote B SVG (dark cyan)")

    # C
    c = make_c_png()
    save_png(c, OUT / "dsplayer-icon-c-256.png")
    (OUT / "dsplayer-icon-c.svg").write_text(make_c_svg(), encoding="utf-8")
    print("Wrote C SVG")

    for p in sorted(OUT.glob("*")):
        print(f"  {p.name:36} {p.stat().st_size:6} bytes")


if __name__ == "__main__":
    main()
