"""Generate a simple tray icon for hdr2sdr."""
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:
    raise SystemExit("Pillow required: pip install pillow")

root = Path(__file__).resolve().parents[1]
assets = root / "assets"
assets.mkdir(exist_ok=True)
out = assets / "hdr2sdr.ico"

sizes = [16, 24, 32, 48, 64, 128, 256]
images = []
for size in sizes:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = max(1, size // 12)
    # dark OLED-like plate
    d.rounded_rectangle(
        [pad, pad, size - pad - 1, size - pad - 1],
        radius=max(2, size // 6),
        fill=(18, 18, 22, 255),
        outline=(80, 220, 180, 255),
        width=max(1, size // 16),
    )
    # HDR accent bar
    y0 = size // 3
    y1 = size - size // 3
    d.rectangle([size // 4, y0, size - size // 4, y1], fill=(255, 180, 40, 255))
    # SDR thin highlight
    d.rectangle(
        [size // 4, y1 - max(2, size // 12), size - size // 4, y1],
        fill=(90, 170, 255, 255),
    )
    images.append(img)

images[0].save(out, format="ICO", sizes=[(s, s) for s in sizes], append_images=images[1:])
print(f"Wrote {out}")
