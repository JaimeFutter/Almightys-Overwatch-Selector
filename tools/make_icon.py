import sys, math
from PIL import Image, ImageDraw, ImageFilter

ORANGE_A = (255, 170, 40)   # top of ring gradient
ORANGE_B = (240, 110, 20)   # bottom
WHITE = (250, 250, 250, 255)
BG_TOP, BG_BOT = (44, 48, 60), (22, 24, 32)

def lerp(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

def vertical_gradient(W, top, bot):
    g = Image.new("RGBA", (W, W))
    d = ImageDraw.Draw(g)
    for y in range(W):
        d.line([(0, y), (W, y)], fill=lerp(top, bot, y / (W - 1)) + (255,))
    return g

def render(size):
    S = 8
    W = size * S
    simple = size <= 24
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))

    # Dark rounded tile.
    pad = W * (0.02 if simple else 0.04)
    mask = Image.new("L", (W, W), 0)
    ImageDraw.Draw(mask).rounded_rectangle([pad, pad, W - pad, W - pad], radius=W * 0.22, fill=255)
    img.paste(vertical_gradient(W, BG_TOP, BG_BOT), (0, 0), mask)

    cx = cy = W / 2
    # Segmented ring: three thick arcs with angled gaps, like a scanning "watch" ring / signal sweep.
    R = W * (0.36 if simple else 0.34)
    th = W * (0.13 if simple else 0.095)
    ring_mask = Image.new("L", (W, W), 0)
    rd = ImageDraw.Draw(ring_mask)
    gap = 26 if simple else 22                                  # degrees
    for start in (-90 + gap / 2, 30 + gap / 2, 150 + gap / 2):  # gaps at top (-90), 30, 150
        rd.arc([cx - R, cy - R, cx + R, cy + R], start, start + 120 - gap, fill=255, width=int(th))
    img.paste(vertical_gradient(W, ORANGE_A, ORANGE_B), (0, 0), ring_mask)

    # Network: hub in the centre wired to a node sitting in each ring gap.
    d = ImageDraw.Draw(img)
    hub_r = W * (0.085 if simple else 0.07)
    node_r = W * (0.06 if simple else 0.05)
    lw = int(W * (0.06 if simple else 0.035))
    nodes = []
    for ang in (-90, 30, 150):
        a = math.radians(ang)
        nodes.append((cx + R * math.cos(a), cy + R * math.sin(a)))
    if not simple:
        for (x, y) in nodes:
            d.line([(cx, cy), (x, y)], fill=WHITE, width=lw)
    for (x, y) in nodes:
        d.ellipse([x - node_r, y - node_r, x + node_r, y + node_r], fill=WHITE)
    d.ellipse([cx - hub_r, cy - hub_r, cx + hub_r, cy + hub_r], fill=WHITE)
    # Orange core in the hub ties the colours together.
    core = hub_r * 0.45
    if not simple:
        d.ellipse([cx - core, cy - core, cx + core, cy + core], fill=ORANGE_B + (255,))

    return img.resize((size, size), Image.LANCZOS)

out, preview = sys.argv[1], sys.argv[2]
sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
frames = [render(s) for s in sizes]
frames[-1].save(out + "/app.ico", format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
frames[-1].save(out + "/app.png")
shown = [16, 24, 32, 48, 64, 256]
strip = Image.new("RGBA", (sum(shown) + 10 * len(shown) + 270, 256), (243, 243, 243, 255))
x = 0
for s in shown:
    f = frames[sizes.index(s)]
    strip.paste(f, (x, 256 - s), f); x += s + 10
dark = Image.new("RGBA", (260, 256), (32, 32, 32, 255)); dark.paste(frames[-1], (2, 0), frames[-1])
strip.paste(dark, (x, 0))
strip.save(preview)
print("ok")
