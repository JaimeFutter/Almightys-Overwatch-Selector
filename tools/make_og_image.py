import sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter
root = sys.argv[1]
W, H = 1200, 630
img = Image.new("RGB", (W, H), (20, 22, 30))
d = ImageDraw.Draw(img)
for y in range(H):  # subtle diagonal-ish glow
    t = y / H
    d.line([(0, y), (W, y)], fill=(int(24 + 10 * (1 - t)), int(26 + 6 * (1 - t)), int(36 + 8 * (1 - t))))
glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse([-200, -260, 520, 460], fill=(245, 140, 30, 70))
img.paste(glow.filter(ImageFilter.GaussianBlur(120)), (0, 0), glow.filter(ImageFilter.GaussianBlur(120)))
d = ImageDraw.Draw(img)
icon = Image.open(root + "/src/Aos.App/Assets/app.png").convert("RGBA").resize((150, 150), Image.LANCZOS)
img.paste(icon, (70, 70), icon)
def font(names, size):
    for n in names:
        try: return ImageFont.truetype(n, size)
        except OSError: pass
    return ImageFont.load_default()
MAXW = 600
def fit(text, names, size):
    while size > 20:
        f = font(names, size)
        if d.textlength(text, font=f) <= MAXW: return f
        size -= 2
    return font(names, size)
B = ["C:/Windows/Fonts/segoeuib.ttf", "C:/Windows/Fonts/arialbd.ttf"]
S = ["C:/Windows/Fonts/seguisb.ttf", "C:/Windows/Fonts/arial.ttf"]
Rg = ["C:/Windows/Fonts/segoeui.ttf", "C:/Windows/Fonts/arial.ttf"]
d.text((70, 250), "Pick your", font=fit("Overwatch servers", B, 64), fill=(245, 245, 247))
d.text((70, 318), "Overwatch servers", font=fit("Overwatch servers", B, 64), fill=(255, 165, 40))
sub = fit("Block the Middle East, EU or NA", S, 32)
d.text((70, 412), "Block the Middle East, EU or NA", font=sub, fill=(214, 216, 224))
d.text((70, 452), "servers in one click.", font=sub, fill=(214, 216, 224))
small = fit("Free · Open source · No VPN · Windows 10/11", Rg, 26)
d.text((70, 515), "Free · Open source · No VPN · Windows 10/11", font=small, fill=(160, 164, 178))
d.text((70, 560), "Almighty's Overwatch Selector", font=font(Rg, 24), fill=(140, 144, 158))
# App screenshot, tilted card on the right
shot = Image.open(root + "/docs/assets/screenshot-servers.png").convert("RGBA")
shot = shot.crop((0, 0, shot.width, int(shot.width * 0.82)))
shot = shot.resize((560, int(560 * shot.height / shot.width)), Image.LANCZOS)
mask = Image.new("L", shot.size, 0); ImageDraw.Draw(mask).rounded_rectangle([0, 0, *shot.size], 14, fill=255)
card = Image.new("RGBA", shot.size, (0, 0, 0, 0)); card.paste(shot, (0, 0), mask)
shadow = Image.new("RGBA", (card.width + 80, card.height + 80), (0, 0, 0, 0))
ImageDraw.Draw(shadow).rounded_rectangle([40, 40, card.width + 40, card.height + 40], 18, fill=(0, 0, 0, 160))
shadow = shadow.filter(ImageFilter.GaussianBlur(18))
x, y = 720, 110
img.paste(shadow, (x - 40, y - 20), shadow)
img.paste(card, (x, y), card)
img.save(root + "/docs/assets/og.png", optimize=True)
print("ok", img.size)
