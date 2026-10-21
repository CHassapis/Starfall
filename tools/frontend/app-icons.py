"""Installs the owner's Starfall home-screen icon kit (Starfall_PWA_Home_Screen_Kit.zip, folder icons/) as the app's
icons: the supplied PNGs are copied unchanged; favicon-16/48 and favicon.ico are plain Lanczos resizes of the kit's
source-icon-1024.png (no redraw). Needs Pillow.
    python3 tools/frontend/app-icons.py PATH/TO/KIT/icons
"""
import shutil
import sys
from pathlib import Path

from PIL import Image

kit = Path(sys.argv[1])
out = Path("src/frontend/apps/builder-app/public/icons")
out.mkdir(parents=True, exist_ok=True)
for name in ["apple-touch-icon.png", "favicon-32.png", "icon-192.png", "icon-512.png", "icon-maskable-192.png", "icon-maskable-512.png"]:
    shutil.copyfile(kit / name, out / name)
source = Image.open(kit / "source-icon-1024.png").convert("RGB")
for size in (16, 48):
    source.resize((size, size), Image.LANCZOS).save(out / f"favicon-{size}.png", optimize=True)
source.save(out / "favicon.ico", sizes=[(16, 16), (32, 32), (48, 48)])
print("icons written to", out)
