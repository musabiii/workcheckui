import sys
from PIL import Image, ImageDraw, ImageFilter

# App icon (files/app.ico): a person seen from behind, sitting at a monitor, on a rounded indigo plate.
# Usage: python tools/make_icon.py files/app.ico preview.png   (needs Pillow)
S = 1024                      # master canvas; every size is downscaled from it
K = S / 256                   # design units are on a 256 grid


def u(*v):
    return [int(round(x * K)) for x in v]


def gradient(w, h, top, bottom):
    g = Image.new('RGBA', (w, h))
    px = g.load()
    for y in range(h):
        t = y / (h - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
        for x in range(w):
            px[x, y] = c
    return g


def master():
    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))

    # Plate
    mask = Image.new('L', (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle(u(8, 8, 248, 248), radius=int(56 * K), fill=255)
    img.paste(gradient(S, S, (124, 108, 255), (67, 56, 202)), (0, 0), mask)

    d = ImageDraw.Draw(img)

    # Monitor: white bezel, light screen with a couple of "lines of work"
    d.rounded_rectangle(u(36, 40, 220, 170), radius=int(16 * K), fill=(255, 255, 255, 255))
    d.rounded_rectangle(u(48, 52, 208, 158), radius=int(8 * K), fill=(224, 231, 255, 255))
    for i, (x2, col) in enumerate([(150, (129, 140, 248)), (184, (165, 180, 252)), (132, (165, 180, 252))]):
        y = 66 + i * 16
        d.rounded_rectangle(u(62, y, x2, y + 7), radius=int(3.5 * K), fill=col + (255,))
    # Stand foot peeking out on both sides of the person
    d.rounded_rectangle(u(72, 176, 184, 186), radius=int(5 * K), fill=(255, 255, 255, 230))

    # Person from behind: a plate-colored outline first, so the silhouette separates from the monitor
    person = (30, 27, 75, 255)
    outline = (67, 56, 202, 255)
    pad = 7
    d.ellipse(u(128 - 30 - pad, 120 - pad, 128 + 30 + pad, 180 + pad), fill=outline)
    d.rounded_rectangle(u(62 - pad, 186 - pad, 194 + pad, 300), radius=int(54 * K), fill=outline)
    d.ellipse(u(98, 120, 158, 180), fill=person)                       # head
    d.rounded_rectangle(u(62, 186, 194, 300), radius=int(54 * K), fill=person)   # shoulders and back

    # Keep the person inside the plate
    clipped = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    clipped.paste(img, (0, 0), mask)
    return clipped


def main(out_ico, out_png):
    m = master()
    sizes = [256, 128, 64, 48, 40, 32, 24, 20, 16]
    frames = []
    for s in sizes:
        f = m.resize((s, s), Image.LANCZOS)
        if s <= 32:
            f = f.filter(ImageFilter.UnsharpMask(radius=0.6, percent=60, threshold=0))
        frames.append(f)
    frames[0].save(out_ico, format='ICO', sizes=[(s, s) for s in sizes], append_images=frames[1:])
    # Preview strip: each size on a light and a dark taskbar color
    strip = Image.new('RGBA', (sum(sizes) + 20 * len(sizes) + 20, 2 * 276), (243, 243, 243, 255))
    ImageDraw.Draw(strip).rectangle([0, 276, strip.width, strip.height], fill=(32, 32, 32, 255))
    x = 20
    for f in frames:
        for row in (0, 276):
            strip.paste(f, (x, row + 10 + (256 - f.height) // 2), f)
        x += f.width + 20
    strip.save(out_png)
    print('ok', out_ico)


main(sys.argv[1], sys.argv[2])
