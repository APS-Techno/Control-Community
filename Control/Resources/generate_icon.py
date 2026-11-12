"""Generate multi-size .ico for APS System Control - Neon Blueprint with Magenta Plasma"""
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import os

script_dir = os.path.dirname(os.path.abspath(__file__))
ico_path = os.path.join(script_dir, "..", "DepositionControl2_Icon.ico")
ico_resources_path = os.path.join(script_dir, "DepositionControl2_Icon.ico")
png_path = os.path.join(script_dir, "AppIcon.png")


def draw_icon(size):
    # Work at 4x resolution for quality, then downscale
    s = max(size * 4, 512)
    scale = s / 120.0  # Our design is in 120x120 viewBox

    def sc(v):
        return int(v * scale)

    def scf(v):
        return v * scale

    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    bg = (14, 42, 74)
    border = (52, 152, 219)
    grid = (30, 95, 138)
    cyan = (0, 229, 255)
    cyan_dim = (0, 229, 255, 40)
    magenta = (255, 110, 199)
    magenta_bright = (255, 45, 149)

    r = sc(8)
    draw.rounded_rectangle([sc(5), sc(5), sc(115), sc(115)], radius=r, fill=bg, outline=border, width=max(sc(2), 2))

    gw = max(1, sc(0.5))
    for y in [30, 55, 80]:
        draw.line([sc(5), sc(y), sc(115), sc(y)], fill=grid, width=gw)
    for x in [30, 60, 90]:
        draw.line([sc(x), sc(5), sc(x), sc(115)], fill=grid, width=gw)

    # Chamber
    cw = max(sc(3), 3)
    draw.rounded_rectangle([sc(22), sc(28), sc(98), sc(80)], radius=sc(4), fill=None, outline=cyan, width=cw)

    # Magnetrons
    mw = max(sc(2), 2)
    for mx in [33, 53, 73]:
        draw.rounded_rectangle(
            [sc(mx), sc(32), sc(mx + 14), sc(39)],
            radius=sc(2), fill=(0, 229, 255, 38), outline=cyan, width=mw
        )

    # Plasma flows - magenta with glow
    plasma_layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    pd = ImageDraw.Draw(plasma_layer)

    pw_thick = max(sc(2.5), 3)
    pw_thin = max(sc(2), 2)

    plasma_lines = [
        # Left magnetron
        (38, 39, 34, 55, pw_thin, 0.9),
        (40, 39, 40, 56, pw_thick, 1.0),
        (42, 39, 46, 55, pw_thin, 0.9),
        # Center magnetron
        (58, 39, 54, 55, pw_thin, 0.9),
        (60, 39, 60, 56, pw_thick, 1.0),
        (62, 39, 66, 55, pw_thin, 0.9),
        # Right magnetron
        (78, 39, 74, 55, pw_thin, 0.9),
        (80, 39, 80, 56, pw_thick, 1.0),
        (82, 39, 86, 55, pw_thin, 0.9),
    ]

    for x1, y1, x2, y2, w, opacity in plasma_lines:
        alpha = int(255 * opacity)
        # Gradient from bright magenta to dimmer
        steps = 12
        for i in range(steps):
            t = i / steps
            lx1 = sc(x1 + (x2 - x1) * t)
            ly1 = sc(y1 + (y2 - y1) * t)
            lx2 = sc(x1 + (x2 - x1) * (t + 1 / steps))
            ly2 = sc(y1 + (y2 - y1) * (t + 1 / steps))
            fade = 1.0 - t * 0.6
            r_c = int(255 * fade)
            g_c = int(110 * fade + 45 * (1 - fade))
            b_c = int(199 * fade + 149 * (1 - fade))
            a = int(alpha * fade)
            pd.line([lx1, ly1, lx2, ly2], fill=(r_c, g_c, b_c, a), width=w)

    plasma_glow = plasma_layer.filter(ImageFilter.GaussianBlur(radius=sc(2.5)))
    img = Image.alpha_composite(img, plasma_glow)
    img = Image.alpha_composite(img, plasma_layer)

    # Plasma glow zones (elliptical halos)
    glow_layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow_layer)
    for cx in [40, 60, 80]:
        gd.ellipse(
            [sc(cx - 8), sc(48 - 10), sc(cx + 8), sc(48 + 10)],
            fill=(255, 110, 199, 20)
        )
    glow_layer = glow_layer.filter(ImageFilter.GaussianBlur(radius=sc(3)))
    img = Image.alpha_composite(img, glow_layer)

    # Re-create draw on composited image
    draw = ImageDraw.Draw(img)

    # Stage
    draw.rounded_rectangle(
        [sc(32), sc(67), sc(88), sc(72)],
        radius=sc(2), fill=(0, 229, 255, 50), outline=cyan, width=mw
    )

    # Pump line
    plw = max(sc(2.5), 3)
    draw.line([sc(60), sc(80), sc(60), sc(93)], fill=cyan, width=plw)
    draw.rounded_rectangle([sc(46), sc(91), sc(74), sc(103)], radius=sc(3), fill=None, outline=cyan, width=max(sc(2), 2))

    # APS text
    font_size = max(sc(9), 10)
    try:
        font = ImageFont.truetype("consola.ttf", font_size)
    except (OSError, IOError):
        try:
            font = ImageFont.truetype("cour.ttf", font_size)
        except (OSError, IOError):
            font = ImageFont.load_default()

    text = "APS"
    bbox = draw.textbbox((0, 0), text, font=font)
    tw = bbox[2] - bbox[0]
    tx = (s - tw) // 2
    ty = sc(8)

    text_glow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    tgd = ImageDraw.Draw(text_glow)
    tgd.text((tx, ty), text, fill=(0, 229, 255, 100), font=font)
    text_glow = text_glow.filter(ImageFilter.GaussianBlur(radius=sc(1.5)))
    img = Image.alpha_composite(img, text_glow)

    draw = ImageDraw.Draw(img)
    draw.text((tx, ty), text, fill=cyan, font=font)

    if s != size:
        img = img.resize((size, size), Image.LANCZOS)

    return img


target_sizes = [16, 24, 32, 48, 64, 128, 256]
images = [draw_icon(s) for s in target_sizes]

# Save ICO - pass the largest image and let PIL downsample to all requested sizes
largest = images[-1]
largest.save(
    ico_path,
    format="ICO",
    sizes=[(s, s) for s in target_sizes],
)
largest.save(
    ico_resources_path,
    format="ICO",
    sizes=[(s, s) for s in target_sizes],
)

# Save 256px PNG for reference
images[-1].save(png_path, format="PNG")

print(f"Icon saved: {ico_path}")
print(f"Icon saved: {ico_resources_path}")
print(f"PNG saved:  {png_path}")
print(f"Sizes: {[f'{s}x{s}' for s in target_sizes]}")
