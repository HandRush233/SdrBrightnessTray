from PIL import Image, ImageDraw
import os

OUT = os.path.join(os.path.dirname(__file__), "..", "assets")
os.makedirs(OUT, exist_ok=True)
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def rounded(draw, box, radius, fill=None, outline=None, width=1):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def render(size):
    scale = 4
    n = size * scale
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Background tile with a diagonal-ish blue gradient.
    margin = round(n * 0.035)
    tile = [margin, margin, n - margin, n - margin]
    radius = round(n * 0.22)
    bg = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    bd = ImageDraw.Draw(bg)
    top = (13, 86, 156, 255)
    bottom = (49, 181, 238, 255)
    for y in range(n):
        t = y / max(1, n - 1)
        c = tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(4))
        bd.line([(0, y), (n, y)], fill=c)
    mask = Image.new("L", (n, n), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle(tile, radius=radius, fill=255)
    img.alpha_composite(Image.composite(bg, Image.new("RGBA", (n, n), (0, 0, 0, 0)), mask))

    # Monitor body.
    body = [n * 0.205, n * 0.235, n * 0.795, n * 0.655]
    stroke = max(1.15, size * 0.045) * scale
    rounded(d, body, n * 0.045, fill=(7, 23, 37, 255), outline=(255, 255, 255, 255), width=round(stroke))

    # Screen.
    inset = n * 0.055
    screen = [body[0] + inset, body[1] + inset, body[2] - inset, body[3] - inset]
    rounded(d, screen, n * 0.022, fill=(5, 16, 27, 255))

    # Stand.
    neck_w = n * 0.06
    d.rounded_rectangle([n * 0.5 - neck_w / 2, body[3] - n * 0.005, n * 0.5 + neck_w / 2, n * 0.755],
                        radius=neck_w * 0.25, fill=(255, 255, 255, 255))
    base_w = n * 0.22
    base_h = max(1.4 * scale, n * 0.028)
    d.rounded_rectangle([n * 0.5 - base_w / 2, n * 0.755 - base_h / 2, n * 0.5 + base_w / 2, n * 0.755 + base_h / 2],
                        radius=base_h / 2, fill=(255, 255, 255, 255))

    # Brightness slider inside the screen.
    track_y = screen[1] + (screen[3] - screen[1]) * 0.58
    track_x0 = screen[0] + (screen[2] - screen[0]) * 0.14
    track_x1 = screen[2] - (screen[2] - screen[0]) * 0.14
    track_h = max(1.25 * scale, size * 0.026 * scale)
    track_h = min(track_h, (screen[3] - screen[1]) * 0.22)
    d.rounded_rectangle([track_x0, track_y - track_h / 2, track_x1, track_y + track_h / 2],
                        radius=track_h / 2, fill=(88, 108, 126, 255))

    knob_x = track_x0 + (track_x1 - track_x0) * 0.68
    d.rounded_rectangle([track_x0, track_y - track_h / 2, knob_x, track_y + track_h / 2],
                        radius=track_h / 2, fill=(87, 205, 255, 255))
    knob_r = max(2.25 * scale, size * 0.034 * scale)
    knob_r = min(knob_r, (screen[3] - screen[1]) * 0.23)
    d.ellipse([knob_x - knob_r, track_y - knob_r, knob_x + knob_r, track_y + knob_r],
              fill=(255, 255, 255, 255))

    return img.resize((size, size), Image.LANCZOS)


images = [render(s) for s in SIZES]
images[-1].save(os.path.join(OUT, "app.png"))
images[-1].save(os.path.join(OUT, "app.ico"), format="ICO",
                sizes=[(s, s) for s in SIZES], append_images=images[:-1])
print("wrote", os.path.join(OUT, "app.ico"))
