"""Converts the Poly Haven ground textures into HTownSim's WorldTriplanar format (system Python + Pillow + NumPy).

    python scripts/convert_ground_textures.py

The shader multiplies a material's own tint by a detail map where mid-grey means "no change". Pure luminance (as the
project's older textures use) would throw away what makes these scans read as real (brown leaves in the lawn,
flecks in the mulch), so each channel is normalised to its own mean instead: the average stays mid-grey and the
material tint still sets the overall colour, but the variation keeps its hue. Normal maps are Poly Haven's OpenGL
("nor_gl") maps, which is Unity's convention, copied as they are.

Output: J:/Unity/projects/HTownSim/Assets/HTownSim/Textures/Ground/<Name>.png and <Name>_N.png
"""
import os
import shutil

import numpy as np
from PIL import Image

SRC = os.path.join(os.path.dirname(__file__), "..", "textures", "polyhaven")
DST = r"J:\Unity\projects\HTownSim\Assets\HTownSim\Textures\Ground"

# Unity texture name -> Poly Haven id, and how much of the colour variation to keep (1 = all of it).
MAPS = {
    "GroundParking": ("asphalt_04", 0.85),
    "GroundLawn": ("leafy_grass", 1.0),
    "GroundVacant": ("grass_ground", 1.0),
    # The scan alternates light and dark panels; at full strength it read as an orange checkerboard.
    "GroundPaving": ("concrete_pavement_03", 0.35),
    "GroundMulch": ("yellow_flower_mulch", 1.0),
    "Bark": ("bark_brown_02", 1.0),
    # House lap siding (tinted per house in Unity, so keep the board shadows but not the brown paint).
    "Siding": ("brown_planks_05", 0.3),
}


def convert(name, asset, strength):
    src = os.path.join(SRC, asset)
    rgb = np.asarray(Image.open(os.path.join(src, f"{asset}_Diffuse_1k.png")).convert("RGB")).astype(np.float32) / 255.0
    # Work in linear light so "mean = mid-grey" holds after the shader's sRGB decode.
    lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    mean = lin.reshape(-1, 3).mean(axis=0)
    rel = lin / mean  # 1.0 = average
    rel = 1.0 + (rel - 1.0) * strength
    # Partial desaturation of the variation for low-strength maps keeps panel/joint detail but drops the hue swings.
    if strength < 0.6:
        lum = rel.mean(axis=2, keepdims=True)
        rel = lum + (rel - lum) * strength
    detail_lin = np.clip(0.2140 * rel, 0.0, 1.0)  # 0.214 linear = sRGB 0.5, i.e. mid-grey at the average
    srgb = np.where(detail_lin <= 0.0031308, detail_lin * 12.92, 1.055 * detail_lin ** (1 / 2.4) - 0.055)
    # Scale so the average lands exactly on sRGB 128.
    srgb *= 0.5 / srgb.reshape(-1, 3).mean(axis=0)
    Image.fromarray((np.clip(srgb, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(DST, f"{name}.png"))
    shutil.copyfile(os.path.join(src, f"{asset}_nor_gl_1k.png"), os.path.join(DST, f"{name}_N.png"))
    print(name, "<-", asset, "mean linear", np.round(mean, 3))


if __name__ == "__main__":
    os.makedirs(DST, exist_ok=True)
    for name, (asset, strength) in MAPS.items():
        convert(name, asset, strength)
