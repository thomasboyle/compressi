"""Extract pixel-art UI assets from the Compressi mockup via corner flood-fill."""
from __future__ import annotations

import os
from collections import deque
from PIL import Image, ImageDraw

SRC = r"C:\Users\thoma\.cursor\projects\d-C-Compressi\assets\c__Users_thoma_AppData_Roaming_Cursor_User_workspaceStorage_empty-window_images_image-6bd95fad-77ce-4187-bb89-b19bde1daee9.png"
OUT = r"D:\C++\Compressi\Compressi.App\Assets\ui"


def flood_alpha(im: Image.Image, tol: int = 22, seeds: list[tuple[int, int]] | None = None) -> Image.Image:
    """Make background transparent by flooding from corners / seeds."""
    rgba = im.convert("RGBA")
    w, h = rgba.size
    pix = rgba.load()
    if seeds is None:
        seeds = [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]

    visited = [[False] * w for _ in range(h)]
    q: deque[tuple[int, int]] = deque()

    def close(a, b) -> bool:
        return abs(a[0] - b[0]) <= tol and abs(a[1] - b[1]) <= tol and abs(a[2] - b[2]) <= tol

    for sx, sy in seeds:
        if 0 <= sx < w and 0 <= sy < h and not visited[sy][sx]:
            visited[sy][sx] = True
            q.append((sx, sy))

    # Use each seed's own color as reference for that flood region
    refs: dict[tuple[int, int], tuple[int, int, int]] = {}
    for sx, sy in list(q):
        r, g, b, _ = pix[sx, sy]
        refs[(sx, sy)] = (r, g, b)

    # Simpler: average seed colors as one bg
    seed_colors = []
    for sx, sy in seeds:
        if 0 <= sx < w and 0 <= sy < h:
            r, g, b, _ = pix[sx, sy]
            seed_colors.append((r, g, b))
    if not seed_colors:
        return rgba
    br = sum(c[0] for c in seed_colors) // len(seed_colors)
    bg = sum(c[1] for c in seed_colors) // len(seed_colors)
    bb = sum(c[2] for c in seed_colors) // len(seed_colors)
    bg_rgb = (br, bg, bb)

    q = deque(seeds)
    visited = [[False] * w for _ in range(h)]
    for sx, sy in seeds:
        if 0 <= sx < w and 0 <= sy < h:
            visited[sy][sx] = True

    while q:
        x, y = q.popleft()
        r, g, b, a = pix[x, y]
        if close((r, g, b), bg_rgb):
            pix[x, y] = (r, g, b, 0)
            for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                if 0 <= nx < w and 0 <= ny < h and not visited[ny][nx]:
                    visited[ny][nx] = True
                    q.append((nx, ny))
    return rgba


def trim_alpha(im: Image.Image, pad: int = 2) -> Image.Image:
    bbox = im.getbbox()
    if not bbox:
        return im
    l, t, r, b = bbox
    return im.crop((max(0, l - pad), max(0, t - pad), min(im.width, r + pad), min(im.height, b + pad)))


def nearest_scale(im: Image.Image, factor: int) -> Image.Image:
    return im.resize((im.width * factor, im.height * factor), Image.NEAREST)


def sample(im: Image.Image, x: int, y: int, r: int = 1) -> tuple[int, int, int]:
    w, h = im.size
    px = []
    for yy in range(max(0, y - r), min(h, y + r + 1)):
        for xx in range(max(0, x - r), min(w, x + r + 1)):
            px.append(im.getpixel((xx, yy)))
    n = len(px)
    return tuple(sum(c[i] for c in px) // n for i in range(3))  # type: ignore[return-value]


def save(im: Image.Image, name: str, scales: list[int] | None = None) -> None:
    path = os.path.join(OUT, name)
    im.save(path)
    print(f"  {name}: {im.size}")
    if scales:
        base, ext = os.path.splitext(name)
        for s in scales:
            nearest_scale(im, s).save(os.path.join(OUT, f"{base}@{s}x{ext}"))


def main() -> None:
    os.makedirs(OUT, exist_ok=True)
    # Clean previous derived assets but keep mockup
    for f in os.listdir(OUT):
        if f.startswith("_inspect") or (f.endswith(".png") and not f.startswith("_mockup")):
            os.remove(os.path.join(OUT, f))

    im = Image.open(SRC).convert("RGB")
    w, h = im.size
    print("size", w, h)

    # Palette samples from known good spots (from visual inspection)
    palette = {
        "app_bg": sample(im, 500, 20),
        "panel": sample(im, 100, 250),
        "active_fill": sample(im, 100, 62),
        "text": sample(im, 100, 100),
        "btn_green": sample(im, 400, 490),
        "drop_inner": sample(im, 360, 150),
        "security": sample(im, 800, 500),
        "border": sample(im, 210, 55),
    }
    for k, v in palette.items():
        print(f"  {k}: #{v[0]:02X}{v[1]:02X}{v[2]:02X}")

    # --- Large illustrations: keep cream bg (no keying) for fidelity ---
    smoothie_raw = im.crop((22, 330, 178, 520))
    smoothie_raw.save(os.path.join(OUT, "smoothie-raw.png"))
    smoothie = flood_alpha(smoothie_raw, tol=18)
    smoothie = trim_alpha(smoothie, 1)
    # If flood ate too much, fall back to raw with soft corner cleanup only
    if smoothie.getbbox() is None or smoothie.width < 40:
        smoothie = smoothie_raw.convert("RGBA")
    save(smoothie, "smoothie.png", scales=[2, 3])

    # Results empty camera + sparkles (keep background, crop tight around art+text-free zone)
    results_raw = im.crop((700, 120, 900, 285))
    results = flood_alpha(results_raw, tol=16)
    results = trim_alpha(results, 2)
    if results.width < 40:
        results = results_raw.convert("RGBA")
    save(results, "results-empty.png", scales=[2, 3])

    # Drop camera icon tile
    cam_raw = im.crop((335, 112, 395, 175))
    cam = flood_alpha(cam_raw, tol=20)
    cam = trim_alpha(cam, 1)
    save(cam, "icon-camera-add.png", scales=[2, 3, 4])

    # Leaf sprig
    leaf_raw = im.crop((505, 205, 560, 270))
    leaf = flood_alpha(leaf_raw, tol=18)
    leaf = trim_alpha(leaf, 1)
    save(leaf, "deco-leaf-sprig.png", scales=[2, 3])

    # Nav icons — crop icon cells from sidebar
    # From sidebar inspect: icons roughly at x=35-55
    nav = {
        "nav-compress": (32, 54, 54, 76),
        "nav-history": (32, 92, 54, 114),
        "nav-settings": (32, 130, 54, 152),
        "nav-about": (32, 168, 54, 190),
    }
    for name, box in nav.items():
        raw = im.crop(box)
        # For compress, also seed from active green so pill fill clears
        icon = flood_alpha(raw, tol=28, seeds=[(0, 0), (raw.width - 1, 0), (0, raw.height - 1), (raw.width - 1, raw.height - 1), (2, 2)])
        icon = trim_alpha(icon, 0)
        save(icon, f"{name}.png", scales=[3, 4])

    # Folder from browse button
    folder_raw = im.crop((318, 222, 338, 242))
    folder = flood_alpha(folder_raw, tol=25)
    folder = trim_alpha(folder, 0)
    save(folder, "icon-folder.png", scales=[3, 4])

    # Leaf on start button — sample from green button face
    btn_leaf_raw = im.crop((348, 470, 372, 498))
    btn_leaf = flood_alpha(btn_leaf_raw, tol=30)
    btn_leaf = trim_alpha(btn_leaf, 0)
    save(btn_leaf, "icon-leaf.png", scales=[3, 4])

    # Shield
    shield_raw = im.crop((642, 470, 672, 502))
    shield = flood_alpha(shield_raw, tol=24)
    shield = trim_alpha(shield, 0)
    save(shield, "icon-shield.png", scales=[3, 4])

    # Stipple pattern: fine low-contrast 4x4 tile for readable selected-control text
    active = palette["active_fill"]
    mid = (max(0, active[0] - 18), max(0, active[1] - 15), max(0, active[2] - 18))
    tile = Image.new("RGBA", (4, 4), (*active, 255))
    d = ImageDraw.Draw(tile)
    d.point((0, 0), fill=(*mid, 255))
    d.point((2, 2), fill=(*mid, 255))
    save(tile, "pattern-stipple.png", scales=[4, 8])

    fill = Image.new("RGBA", (256, 256))
    for y in range(0, 256, 4):
        for x in range(0, 256, 4):
            fill.paste(tile, (x, y))
    fill.save(os.path.join(OUT, "pattern-stipple-fill.png"))
    print("  pattern-stipple-fill.png: (256, 256)")

    btn_base = (167, 177, 143)
    btn_mid = (140, 150, 112)
    btn_tile = Image.new("RGBA", (4, 4), (*btn_base, 255))
    bd = ImageDraw.Draw(btn_tile)
    bd.point((0, 0), fill=(*btn_mid, 255))
    bd.point((2, 2), fill=(*btn_mid, 255))
    btn_fill = Image.new("RGBA", (256, 256))
    for y in range(0, 256, 4):
        for x in range(0, 256, 4):
            btn_fill.paste(btn_tile, (x, y))
    btn_fill.save(os.path.join(OUT, "pattern-stipple-button.png"))
    print("  pattern-stipple-button.png: (256, 256)")

    # Grain texture
    grain = Image.effect_noise((256, 256), 32).convert("L")
    grain_rgba = Image.new("RGBA", (256, 256))
    gp, rp = grain.load(), grain_rgba.load()
    for y in range(256):
        for x in range(256):
            v = gp[x, y]
            a = max(0, min(36, abs(int(v) - 128) // 3))
            rp[x, y] = (70, 60, 45, a)
    grain_rgba.save(os.path.join(OUT, "texture-grain.png"))
    print("  texture-grain.png: (256, 256)")

    # Soft wash for results panel (faint decorative bg)
    wash = im.crop((640, 90, 980, 360)).convert("RGBA")
    px = wash.load()
    for y in range(wash.height):
        for x in range(wash.width):
            r, g, b, _ = px[x, y]
            px[x, y] = (r, g, b, 40)
    wash.save(os.path.join(OUT, "deco-results-wash.png"))

    # Keep source
    im.save(os.path.join(OUT, "_mockup-source.png"))

    # Inspect strips for verification
    for name, box in {
        "sidebar": (0, 30, 200, h),
        "drop": (210, 70, 580, 280),
        "results": (600, 70, 1000, 440),
        "btn": (220, 450, 560, 525),
    }.items():
        im.crop(box).save(os.path.join(OUT, f"_inspect_{name}.png"))

    print("done")


if __name__ == "__main__":
    main()
