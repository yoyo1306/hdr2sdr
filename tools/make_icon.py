"""Genere l'icone hdr2sdr : meme recette que Lumino (tuile + halo + LED),
mais lettre H (HDR) au lieu du L. Voir Lumino/make_icon.py pour l'original."""
from pathlib import Path

from PIL import Image, ImageDraw

S = 512
BG = (17, 24, 39, 255)      # #111827 (identique Lumino)
H_COL = (255, 255, 255, 255)
LED = (0, 213, 255, 255)    # cyan (identique Lumino)

root = Path(__file__).resolve().parents[1]
assets = root / "assets"
assets.mkdir(exist_ok=True)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# tuile arrondie (identique Lumino)
d.rounded_rectangle([8, 8, S - 8, S - 8], radius=120, fill=BG)

# halo subtil derriere le H (identique Lumino)
halo = Image.new("RGBA", (S, S), (0, 0, 0, 0))
hd = ImageDraw.Draw(halo)
hd.ellipse([90, 70, 422, 402], fill=(0, 213, 255, 28))
img = Image.alpha_composite(img, halo)
d = ImageDraw.Draw(img)

# H geometrique : memes barres que le L (76 d'epaisseur, 130 -> 382),
# meme emprise horizontale (150 -> 340) + traverse centrale
x0, y0 = 150, 130
bar, h = 76, 252
x1 = 340 - bar  # 264 : barre droite
d.rounded_rectangle([x0, y0, x0 + bar, y0 + h], radius=20, fill=H_COL)
d.rounded_rectangle([x1, y0, x1 + bar, y0 + h], radius=20, fill=H_COL)
d.rounded_rectangle([x0, y0 + h // 2 - bar // 2, x0 + (340 - 150),
                     y0 + h // 2 + bar // 2], radius=20, fill=H_COL)

# LED : pastille cyan avec lueur (identique Lumino, chevauche le bas
# de la barre droite comme elle chevauchait le pied du L)
cx, cy, r = 372, 352, 34
d.ellipse([cx - r - 14, cy - r - 14, cx + r + 14, cy + r + 14], fill=(0, 213, 255, 60))
d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=LED)
d.ellipse([cx - r + 10, cy - r + 8, cx - r + 26, cy - r + 24], fill=(255, 255, 255, 220))

img.save(assets / "hdr2sdr-preview.png")
img.save(assets / "hdr2sdr.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48),
                                        (64, 64), (128, 128), (256, 256)])
print(f"icones OK -> {assets}")
