# Look test (ticket 25). Throwaway prototype: judged by eye, then discarded.
# Runs inside the container built from the Dockerfile next to it.
#
#   python look.py                  stills for /work/input (product.json + photos)
#   python look.py --sample         stills for a generated placeholder photo
#   python look.py --model u2net    use another cut-out model
#   python look.py --ignore-alpha   run the model even on a photo that is already cut out
#   python look.py --card 2.png     keep that photo uncut, on a card
#
# The Product is cut out of each photo, then placed on three designed
# backgrounds with a soft shadow. Only the photo's transparency is decided
# here: the Product's own pixels are copied from the photo, never repainted.

import argparse
import colorsys
import json
import re
import time
import unicodedata
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageOps
from scipy import ndimage

W, H = 1080, 1920
BOX = {"max_w": 860, "max_h": 1060, "center_y": 860}  # where the Product sits
CARD_RADIUS = 36
MAX_UPSCALE = 1.5
FONT = "/fonts/BeVietnamPro-SemiBold.ttf"
DEFAULT_MODEL = "birefnet-general-lite"

# A cut-out is rejected, and the photo used uncut on a card, outside these limits.
LIMITS = {
    "min_cover": 0.02,  # share of the photo kept: below this, nothing was found
    "max_cover": 0.92,  # above this, the background was kept too
    "max_soft": 0.08,   # share of the kept area that is half-transparent: parts of the Product are missing
    "max_halo": 0.12,   # share of the edge that is still the colour of the background
}

ROOT = Path("/work")


def load_photo(path):
    # Decoded once, with camera rotation applied.
    return ImageOps.exif_transpose(Image.open(path)).convert("RGBA")


def make_placeholder_photo():
    photo = Image.new("RGB", (1200, 1200), (236, 232, 225))
    draw = ImageDraw.Draw(photo)
    draw.rounded_rectangle((360, 220, 840, 980), radius=70, fill=(34, 74, 110))
    draw.rounded_rectangle((420, 300, 780, 560), radius=30, fill=(214, 226, 236))
    draw.text((600, 760), "ẢNH MẪU", font=ImageFont.truetype(FONT, 64), fill="white", anchor="mm")
    return photo.convert("RGBA")


def uncut(photo):
    """The photo as taken: its own colours everywhere it shows at all, white where it does not.

    Half-transparent pixels are not blended with white, or the Product's edge would be repainted.
    """
    rgba = np.asarray(photo)
    rgb = np.where(rgba[..., 3:] > 0, rgba[..., :3], 255).astype(np.uint8)
    return Image.fromarray(rgb, "RGB")


def has_own_transparency(photo):
    alpha = np.asarray(photo)[..., 3]
    return float((alpha < 8).mean()) > 0.02


class Model:
    """The cut-out model, loaded the first time a photo needs it."""

    def __init__(self, name):
        self.name, self.session = name, None

    def mask(self, image):
        from rembg import new_session, remove

        if self.session is None:
            started = time.time()
            self.session = new_session(self.name)
            print(f"model {self.name} loaded in {time.time() - started:.1f}s")
        started = time.time()
        mask = remove(image, session=self.session, only_mask=True)
        self.seconds = time.time() - started
        return mask


def cut_outs(photo, model, ignore_alpha):
    """Yields ways of cutting the Product out, best first: (cut-out, where its alpha came from).

    A cut-out is the photo's pixels with a new alpha.
    """
    if has_own_transparency(photo) and not ignore_alpha:
        yield photo, "photo"
    whole = uncut(photo)
    cutout = whole.convert("RGBA")
    cutout.putalpha(model.mask(whole))
    yield cutout, "model"


def assess(cutout, source):
    """Measures a cut-out and lists the reasons, if any, not to use it."""
    rgba = np.asarray(cutout)
    rgb, mask = rgba[..., :3].astype(np.int16), rgba[..., 3]
    kept = mask > 127
    cover = float(kept.mean())
    visible = mask > 12
    soft = float(((mask > 12) & (mask < 243)).sum() / max(1, visible.sum()))
    measures = {"cover": round(cover, 3), "soft": round(soft, 3)}
    reasons = []
    if cover < LIMITS["min_cover"]:
        reasons.append("no Product found")
    elif cover > LIMITS["max_cover"]:
        reasons.append("background kept")
    if soft > LIMITS["max_soft"]:
        reasons.append("parts of the Product are missing")

    # A halo can only be measured against a plain background, and a photo that
    # came already cut out has no background left to compare with.
    removed = mask < 12
    if source == "model" and removed.sum() > 1000 and kept.any():
        background = np.median(rgb[removed], axis=0)
        plain = float(np.abs(rgb[removed] - background).max(axis=1).mean()) < 10
        if plain:
            reach = max(2, round(0.004 * min(mask.shape)))
            edge = kept & ~ndimage.binary_erosion(kept, iterations=reach)
            like_background = np.abs(rgb[edge] - background).max(axis=1) < 14
            halo = float(like_background.mean())
            measures["halo"] = round(halo, 3)
            if halo > LIMITS["max_halo"]:
                reasons.append("background left around the edge")
    return measures, reasons


def product_colour(cutout):
    """The Product's most characteristic colour, as (hue, lightness, saturation), with the Product's overall brightness."""
    rgba = np.asarray(cutout)
    pixels = rgba[..., :3][rgba[..., 3] > 200]
    pixels = pixels[:: max(1, len(pixels) // 50000)]
    strip = Image.fromarray(pixels.reshape(1, -1, 3), "RGB").quantize(6)
    palette = np.asarray(strip.getpalette()[: 6 * 3]).reshape(-1, 3)
    counts = np.bincount(np.asarray(strip).ravel(), minlength=6)
    best, best_score = None, -1.0
    for colour, count in zip(palette, counts):
        h, l, s = colorsys.rgb_to_hls(*(colour / 255))
        # Near-black and near-white areas (lenses, glare) say little about the Product.
        weight = 0.2 if (l < 0.12 or l > 0.92) else 1.0
        score = count * (s + 0.15) * weight
        if score > best_score:
            best, best_score = (h, l, s), score
    brightness = float((pixels / 255).mean())
    return (*best, brightness)


# --- Backgrounds -------------------------------------------------------------
# Each is drawn from gradients and soft lights only. Nothing is generated.

_yy, _xx = np.mgrid[0:H, 0:W].astype(np.float32)
U, V = _xx / W, _yy / H


def _rgb(colour):
    return np.asarray(colour, dtype=np.float32)


def _blend(image, colour, amount):
    return image + (_rgb(colour) - image) * amount[..., None]


def _light(cx, cy, rx, ry):
    return np.exp(-(((U - cx) / rx) ** 2 + ((V - cy) / ry) ** 2))


def _vertical(top, bottom):
    return _rgb(top) + (_rgb(bottom) - _rgb(top)) * V[..., None]


def _smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def _vignette(image, strength):
    d = ((U - 0.5) / 0.75) ** 2 + ((V - 0.5) / 0.75) ** 2
    return image * (1 - strength * np.clip(d, 0, 1) ** 1.5)[..., None]


def _hls(h, l, s):
    return [c * 255 for c in colorsys.hls_to_rgb(h, l, s)]


def studio(floor, centre, colour):
    """A light seamless backdrop: pale wall curving into a floor."""
    image = _vertical((247, 245, 241), (231, 228, 222))
    into_floor = _smoothstep(floor - 0.05, floor + 0.07, V)
    image = _blend(image, (214, 210, 203), into_floor * (1 - 0.55 * _smoothstep(floor, 1.0, V)))
    image = _blend(image, (255, 255, 254), 0.7 * _light(0.5, centre - 0.05, 0.55, 0.33))
    return _vignette(image, 0.10)


def colour_light(floor, centre, colour):
    """Soft light in the Product's own colour, deep behind a light Product and pale behind a dark one."""
    h, _, s, brightness = colour
    s = 0.0 if s < 0.08 else s  # a grey Product has no hue worth borrowing
    if brightness > 0.5:
        base, lit = _hls(h, 0.17, min(0.5, s * 1.2)), _hls(h, 0.54, min(0.5, s * 1.3))
    else:
        base, lit = _hls(h, 0.84, min(0.45, s)), _hls(h, 0.95, min(0.5, s))
    image = np.broadcast_to(_rgb(base), (H, W, 3)).copy()
    image = _blend(image, lit, 0.9 * _light(0.5, centre, 0.7, 0.4))
    image = _blend(image, lit, 0.5 * _light(0.0, 0.0, 0.55, 0.3))
    image = _blend(image, lit, 0.4 * _light(1.0, 1.0, 0.65, 0.25))
    return _vignette(image, 0.25)


def dark_premium(floor, centre, colour):
    """Charcoal with a spotlight behind the Product and a faint sheen on the floor."""
    image = _vertical((23, 24, 29), (7, 7, 9))
    image = _blend(image, (84, 88, 102), 0.85 * _light(0.5, centre - 0.03, 0.5, 0.30))
    image = _blend(image, (58, 61, 72), 0.55 * _light(0.5, floor + 0.012, 0.55, 0.035))
    return _vignette(image, 0.45)


STYLES = {
    "studio": {"draw": studio, "shadow": 1.0, "glow": 0.0},
    "colour-light": {"draw": colour_light, "shadow": 1.15, "glow": 0.0},
    "dark-premium": {"draw": dark_premium, "shadow": 1.3, "glow": 0.16},
}


def to_image(array, seed=0):
    # A little noise before rounding keeps the gradients from banding.
    noise = np.random.default_rng(seed).uniform(-0.6, 0.6, array.shape[:2])[..., None]
    return Image.fromarray(np.clip(array + noise, 0, 255).round().astype(np.uint8), "RGB")


# --- Composition -------------------------------------------------------------


def place(subject):
    """Scales an RGBA subject into the box and returns it on a transparent 1080x1920 layer."""
    box = subject.getbbox()
    subject = subject.crop(box)
    scale = min(BOX["max_w"] / subject.width, BOX["max_h"] / subject.height, MAX_UPSCALE)
    size = (max(1, round(subject.width * scale)), max(1, round(subject.height * scale)))
    subject = subject.resize(size, Image.LANCZOS)
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    layer.alpha_composite(subject, ((W - size[0]) // 2, BOX["center_y"] - size[1] // 2))
    return layer


def as_card(photo):
    card = uncut(photo).convert("RGBA")
    scale = min(BOX["max_w"] / card.width, BOX["max_h"] / card.height, MAX_UPSCALE)
    mask = Image.new("L", card.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        (0, 0, card.width - 1, card.height - 1), radius=round(CARD_RADIUS / scale), fill=255
    )
    card.putalpha(mask)
    return card


def shadow(layer, on_floor):
    """Shadow opacity for a placed layer: a wide soft one, and where it stands, a tight one."""
    alpha = np.asarray(layer)[..., 3].astype(np.float32) / 255
    ambient = ndimage.shift(ndimage.gaussian_filter(alpha, 38), (30, 0), order=1) * 0.24
    if not on_floor:
        return ambient
    rows = np.flatnonzero(alpha.max(axis=1) > 0.5)
    bottom = int(rows[-1])
    # Whatever reaches the lowest few rows is what touches the floor.
    footprint = alpha[max(0, bottom - 40) : bottom + 1].max(axis=0)
    contact = np.zeros_like(alpha)
    contact[bottom - 4 : bottom + 12] = footprint
    contact = ndimage.gaussian_filter(contact, (12, 20)) * 0.5
    return np.clip(ambient + contact, 0, 1)


def compose(layer, style, colour, on_floor):
    alpha = np.asarray(layer)[..., 3]
    rows = np.flatnonzero(alpha.max(axis=1) > 127)
    floor, centre = (rows[-1] + 1) / H, (rows[0] + rows[-1]) / 2 / H
    image = STYLES[style]["draw"](floor, centre, colour)
    if STYLES[style]["glow"]:
        halo = ndimage.gaussian_filter(alpha.astype(np.float32) / 255, 34)
        image = _blend(image, (255, 255, 255), halo * STYLES[style]["glow"])
    shade = np.clip(shadow(layer, on_floor) * STYLES[style]["shadow"], 0, 0.9)
    image = image * (1 - shade[..., None])
    still = to_image(image).convert("RGBA")
    still.alpha_composite(layer)
    return still.convert("RGB")


# --- Sheets for looking at ---------------------------------------------------


def checker(size, cell=24):
    y, x = np.mgrid[0 : size[1], 0 : size[0]]
    tone = np.where(((x // cell) + (y // cell)) % 2 == 0, 150, 110).astype(np.uint8)
    return Image.fromarray(np.stack([tone, tone - 30, tone], axis=-1), "RGB").convert("RGBA")


def sheet(panels, panel_size, file):
    """Lays labelled panels out in a row."""
    gap, label_h = 24, 70
    font = ImageFont.truetype(FONT, 30)
    w, h = panel_size
    out = Image.new("RGB", (len(panels) * (w + gap) + gap, h + label_h + gap), (32, 32, 36))
    draw = ImageDraw.Draw(out)
    for i, (label, image) in enumerate(panels):
        x = gap + i * (w + gap)
        fitted = ImageOps.contain(image.convert("RGB"), panel_size, Image.LANCZOS)
        out.paste(fitted, (x + (w - fitted.width) // 2, gap + (h - fitted.height) // 2))
        draw.text((x + w // 2, gap + h + label_h // 2), label, font=font, fill="white", anchor="mm")
    out.save(file)


def slugify(text):
    text = unicodedata.normalize("NFD", text)
    text = "".join(ch for ch in text if not unicodedata.combining(ch)).replace("đ", "d").replace("Đ", "d")
    return re.sub(r"[^\w]+", "-", text).strip("-").lower() or "product"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--sample", action="store_true")
    parser.add_argument("--model", default=DEFAULT_MODEL)
    parser.add_argument("--ignore-alpha", action="store_true")
    parser.add_argument("--card", action="append", default=[], metavar="PHOTO")
    args = parser.parse_args()

    input_dir = ROOT / ("sample" if args.sample else "input")
    output_dir = ROOT / "output" / "look"
    product = json.loads((input_dir / "product.json").read_text(encoding="utf-8"))
    slug = slugify(product["name"])

    files = sorted(p for p in input_dir.iterdir() if p.suffix.lower() in {".jpg", ".jpeg", ".png", ".webp"})
    unknown = set(args.card) - {p.name for p in files}
    if unknown:
        raise SystemExit(f"--card names a photo that is not in {input_dir}: {', '.join(sorted(unknown))}")
    photos = [(p.stem, p.name, load_photo(p)) for p in files]
    if args.sample and not photos:
        photos = [("placeholder", "placeholder", make_placeholder_photo())]
    if not photos:
        raise SystemExit(f"No photos found in {input_dir}. Add .jpg, .png or .webp files.")

    model = Model(args.model)
    output_dir.mkdir(parents=True, exist_ok=True)
    checks = {}
    for stem, name, photo in photos:
        original = uncut(photo)
        reasons = ["kept uncut on request"] if name in args.card else []
        tries = () if reasons else cut_outs(photo, model, args.ignore_alpha)
        cutout = None
        for cutout, source in tries:
            measures, reasons = assess(cutout, source)
            by = f"{args.model}, {model.seconds:.1f}s" if source == "model" else "the photo's own transparency"
            print(f"{name}: {by} {measures}" + (f" rejected: {'; '.join(reasons)}" if reasons else " accepted"))
            if not reasons:
                break
        used = "card" if reasons else "cut-out"
        print(f"{name}: using the {used}")

        base = f"{slug}-{stem}"
        panels = [("Photo", original)]
        if cutout is not None:
            # Only transparency may differ from the photo file. Hidden pixels are not compared.
            shown = np.asarray(cutout)[..., 3] > 0
            same = np.array_equal(np.asarray(cutout)[..., :3][shown], np.asarray(photo)[..., :3][shown])
            checks[f"{name}: the cut-out's pixels are the photo's pixels"] = same
            on_checker = checker(cutout.size)
            on_checker.alpha_composite(cutout)
            panels.append(("Cut-out" if used == "cut-out" else f"Rejected: {'; '.join(reasons)}", on_checker))
        (output_dir / f"{base}-cutout.png").unlink(missing_ok=True)
        if used == "cut-out":
            cutout.save(output_dir / f"{base}-cutout.png")
        sheet(panels, (900, 900), output_dir / f"{base}-compare.png")

        layer = place(cutout if used == "cut-out" else as_card(photo))
        colour = product_colour(cutout if used == "cut-out" else original.convert("RGBA"))
        stills = []
        for style in STYLES:
            still = compose(layer, style, colour, on_floor=used == "cut-out")
            still.save(output_dir / f"{base}-{style}.png")
            stills.append((style, still))
            checks[f"{name}: {style} still is {W}x{H}"] = still.size == (W, H)
        sheet(stills, (540, 960), output_dir / f"{base}-styles.png")

    for label, ok in checks.items():
        print(f"{'PASS' if ok else 'FAIL'}  {label}")
    print(f"output: {output_dir}")
    if not all(checks.values()):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
