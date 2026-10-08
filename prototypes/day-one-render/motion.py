# Look test 2 (ticket 26): motion, layout and type. Throwaway prototype: judged by eye, then discarded.
# Runs inside the container built from the Dockerfile next to it.
#
#   python motion.py                   the video built both ways, from /work/input
#   python motion.py --only ffmpeg     one way only (ffmpeg or remotion)
#   python motion.py --sample          a generated placeholder photo
#
# The same four Scenes are built twice: here with FFmpeg filters alone, and in
# remotion/src with Remotion. Both start from the same assets (the cut-out on a
# soft shadow, the studio backdrop, the colours taken from the Product), which
# are made first. The Product is only ever scaled, moved and rotated.

import argparse
import colorsys
import json
import shutil
import subprocess
import tempfile
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

import look

W, H, FPS = 1080, 1920, 30
SUPERSAMPLE = 2  # the FFmpeg build is composed at 2x: overlay and drawtext only place things on whole pixels
SCENE_SECONDS = [3, 5, 7, 5]  # hook, reveal, facts, closing

FONTS = Path("/fonts")
FONT = {w: str(FONTS / f"BeVietnamPro-{w}.ttf") for w in ("SemiBold", "Bold", "ExtraBold")}
INK = (20, 20, 22)
WHITE = (255, 255, 255)
LEFT = 80  # left edge of left-aligned type
MAX_ASPECT = 0.86  # width over height of the widest Product the layouts hold at full size

ROOT = look.ROOT
REMOTION = Path("/opt/remotion")


def run(args, **kwargs):
    r = subprocess.run([str(a) for a in args], capture_output=True, text=True, **kwargs)
    if r.returncode != 0:
        raise SystemExit(f"{args[0]} failed ({r.returncode}):\n{(r.stderr or r.stdout)[-4000:]}")
    return r.stdout


def hex_colour(rgb):
    return "0x%02X%02X%02X" % tuple(rgb)


# --- Assets, shared by both builds --------------------------------------------


def product_layer(subject):
    """The Product on its own soft shadow, centred in a transparent layer.

    Where the Product is opaque its pixels are the subject's pixels, unchanged.
    """
    subject = subject.crop(subject.getbbox())
    if subject.height > 1800:
        scale = 1800 / subject.height
        subject = subject.resize((round(subject.width * scale), 1800), Image.LANCZOS)
    pad = round(0.14 * subject.height)
    rgba = np.zeros((subject.height + 2 * pad, subject.width + 2 * pad, 4), np.float32)
    rgba[pad:-pad, pad:-pad] = np.asarray(subject, np.float32)
    rgb, alpha = rgba[..., :3], rgba[..., 3] / 255
    wide = ndimage.shift(ndimage.gaussian_filter(alpha, 0.035 * subject.height), (0.028 * subject.height, 0), order=1)
    tight = ndimage.shift(ndimage.gaussian_filter(alpha, 0.012 * subject.height), (0.010 * subject.height, 0), order=1)
    shade = np.clip(wide * 0.24 + tight * 0.16, 0, 1)
    # The Product over a black shadow, kept unpremultiplied.
    out_alpha = alpha + shade * (1 - alpha)
    out_rgb = np.where(out_alpha[..., None] > 0, rgb * alpha[..., None] / np.maximum(out_alpha, 1e-6)[..., None], 0)
    layer = np.dstack([out_rgb, out_alpha * 255]).round().astype(np.uint8)

    solid = alpha == 1
    if not np.array_equal(layer[..., :3][solid], rgb[solid].astype(np.uint8)):
        raise SystemExit("The shadow changed the Product's own pixels.")
    return Image.fromarray(layer, "RGBA"), {"productW": subject.width, "productH": subject.height}


def palette(colour):
    """Accent colours taken from the Product: a deep one for panels and type, two pale ones for shapes."""
    h, _, s, _ = colour
    if s < 0.08:  # a grey Product has no hue worth borrowing
        return {"accent": (34, 36, 42), "tint": (226, 226, 228), "ghost": (232, 230, 226)}
    rgb = lambda l, sat: tuple(round(c * 255) for c in colorsys.hls_to_rgb(h, l, sat))
    return {
        "accent": rgb(0.24, min(0.60, max(0.30, s * 1.5))),
        "tint": rgb(0.84, min(0.50, max(0.25, s))),
        "ghost": rgb(0.85, min(0.30, s * 0.7)),
    }


def make_assets(input_dir, assets, sample):
    product = json.loads((input_dir / "product.json").read_text(encoding="utf-8"))
    if not (product.get("name") and product.get("hook") and product.get("facts")):
        raise SystemExit('product.json needs "name", "hook" and at least one entry in "facts".')
    files = sorted(p for p in input_dir.iterdir() if p.suffix.lower() in {".jpg", ".jpeg", ".png", ".webp"})
    photos = [(p.name, look.load_photo(p)) for p in files[:2]]
    if sample and not photos:
        photos = [("placeholder", look.make_placeholder_photo())]
    if not photos:
        raise SystemExit(f"No photos found in {input_dir}. Add .jpg, .png or .webp files.")

    assets.mkdir(parents=True, exist_ok=True)
    model = look.Model(look.DEFAULT_MODEL)
    layers, colour = [], None
    for i, (name, photo) in enumerate(photos):
        subject, reasons = None, []
        for subject, source in look.cut_outs(photo, model, False):
            _, reasons = look.assess(subject, source)
            if not reasons:
                break
        if reasons:
            print(f"{name}: cut-out rejected ({'; '.join(reasons)}), using the photo on a card")
            subject = look.as_card(photo)
        colour = colour or look.product_colour(subject)
        layer, size = product_layer(subject)
        layer.save(assets / f"product-{i + 1}.png")
        layers.append({"file": f"product-{i + 1}.png", "w": layer.width, "h": layer.height, **size})

    colours = palette(colour)
    look.to_image(look.studio(1.3, 0.5, None)).save(assets / "bg.png")
    disc = Image.new("RGBA", (2000, 2000), (0, 0, 0, 0))
    ImageDraw.Draw(disc).ellipse((0, 0, 1999, 1999), fill=colours["tint"])
    disc.resize((1000, 1000), Image.LANCZOS).save(assets / "disc.png")
    for path in FONT.values():
        shutil.copy(path, assets / Path(path).name)

    design = {
        "name": product["name"],
        "hook": product["hook"],
        "facts": product["facts"],
        "cta": product.get("cta", "Xem chi tiết sản phẩm"),
        "fps": FPS,
        "sceneSeconds": SCENE_SECONDS,
        "colours": {k: "#%02x%02x%02x" % v for k, v in {**colours, "ink": INK}.items()},
        "products": layers,
    }
    (assets / "design.json").write_text(json.dumps(design, ensure_ascii=False, indent=2), encoding="utf-8")
    return design, colours


# --- The FFmpeg build ---------------------------------------------------------
# A Scene is a list of layers, back to front. Everything that moves is an Anim,
# which compiles to an FFmpeg expression in t (seconds from the Scene's start).

EASE = {
    "linear": "{p}",
    "in": "pow({p},3)",
    "out": "(1-pow(1-{p},3))",
    "inout": "({p}*{p}*(3-2*{p}))",
    "back": "(1+2.70158*pow({p}-1,3)+1.70158*pow({p}-1,2))",  # overshoots, then settles
}


class Anim:
    def __init__(self, start):
        self.start, self.end, self.terms = start, start, []

    def to(self, value, at, over, ease="out"):
        p = f"clip((t-{at:.3f})/{over:.3f},0,1)"
        self.terms.append((value - self.end, EASE[ease].format(p=p)))
        self.end = value
        return self

    def wave(self, amount, period):
        self.terms.append((amount, f"sin(2*PI*t/{period})"))
        return self

    def expr(self, scale=1.0):
        return "+".join([f"({self.start * scale:.3f})"] + [f"({d * scale:.3f})*{e}" for d, e in self.terms])


def still(value):
    return value if isinstance(value, Anim) else Anim(value)


class Picture:
    """An image file. `height` is the height on screen, in output pixels, of `of` rows of the file."""

    def __init__(self, file, cx, cy, height, of, rot=None):
        self.file, self.cx, self.cy, self.height, self.of, self.rot = file, still(cx), still(cy), still(height), of, rot


class Panel:
    """A plain rectangle. `y` is its top edge."""

    def __init__(self, colour, y, height):
        self.colour, self.y, self.height = colour, still(y), height


class Text:
    """One run of type on one baseline. It slides up into place and fades in at `at`."""

    def __init__(self, text, font, size, x, baseline, colour, at=0.0, out=None, rise=70, x_anim=None, y_anim=None):
        self.text, self.font, self.size, self.x, self.baseline, self.colour = text, font, size, x, baseline, colour
        self.at, self.out, self.rise, self.x_anim, self.y_anim = at, out, rise, x_anim, y_anim


def measure(text, font, size):
    return ImageFont.truetype(FONT[font], size).getlength(text)


def fit(text, font, max_w, max_h, max_size, leading=1.16, min_size=40):
    """The largest size at which the text, broken into lines, fits the box. Returns (size, lines)."""
    words = text.split()
    for size in range(max_size, min_size - 1, -2):
        lines, line = [], ""
        for word in words:
            longer = f"{line} {word}".strip()
            if line and measure(longer, font, size) > max_w:
                lines.append(line)
                line = word
            else:
                line = longer
        lines.append(line)
        if len(lines) * size * leading <= max_h and all(measure(l, font, size) <= max_w for l in lines):
            return size, lines
    raise SystemExit(f'Text does not fit on screen: "{text}"')


def by_word(line, font, size, x, baseline, colour, at, step, **kwargs):
    """One Text per word, each arriving `step` seconds after the one before."""
    texts, typed = [], ""
    for i, word in enumerate(line.split()):
        texts.append(Text(word, font, size, x + measure(typed, font, size), baseline, colour, at + i * step, **kwargs))
        typed += word + " "
    return texts


def compile_scene(layers, seconds, tmp, tag):
    """Returns the ffmpeg arguments that render the layers to one clip."""
    inputs, graph, n = ["-loop", "1", "-framerate", FPS, "-t", seconds, "-i", layers[0]], [], 0
    graph.append(f"[0:v]scale={W * SUPERSAMPLE}:{H * SUPERSAMPLE}:flags=bicubic,format=gbrp[b0]")
    current = "b0"
    pending = []  # drawtext filters waiting to be applied to the current base

    def flush():
        nonlocal current, n
        if pending:
            n += 1
            graph.append(f"[{current}]{','.join(pending)}[b{n}]")
            current = f"b{n}"
            pending.clear()

    for layer in layers[1:]:
        if isinstance(layer, Text):
            # Text reaches ffmpeg through a file, never through the filter string.
            file = tmp / f"{tag}-text-{len(pending)}-{n}.txt"
            file.write_text(layer.text, encoding="utf-8")
            arrive = f"clip((t-{layer.at:.3f})/0.4,0,1)"
            shown = EASE["out"].format(p=arrive)
            alpha = arrive if layer.out is None else f"{arrive}*(1-clip((t-{layer.out - 0.2:.3f})/0.2,0,1))"
            x = layer.x_anim.expr(SUPERSAMPLE) if layer.x_anim else f"{layer.x * SUPERSAMPLE:.1f}"
            y = layer.y_anim.expr(SUPERSAMPLE) if layer.y_anim else f"{layer.baseline * SUPERSAMPLE:.1f}"
            pending.append(
                "drawtext="
                + ":".join(
                    [
                        f"fontfile={FONT[layer.font]}",
                        f"textfile={file}",
                        "expansion=none",
                        "y_align=baseline",
                        f"fontsize={layer.size * SUPERSAMPLE}",
                        f"fontcolor={hex_colour(layer.colour)}",
                        f"x='{x}'",
                        f"y='{y}+{layer.rise * SUPERSAMPLE}*(1-{shown})'",
                        f"alpha='{alpha}'",
                    ]
                )
            )
            continue
        flush()
        index = inputs.count("-i")
        n += 1
        if isinstance(layer, Panel):
            inputs += ["-f", "lavfi", "-i", f"color=c={hex_colour(layer.colour)}:s={W * SUPERSAMPLE}x{layer.height * SUPERSAMPLE}:r={FPS}:d={seconds}"]
            graph.append(f"[{current}][{index}:v]overlay=x=0:y='{layer.y.expr(SUPERSAMPLE)}':eval=frame:format=gbrp[b{n}]")
        else:
            inputs += ["-loop", "1", "-framerate", FPS, "-t", seconds, "-i", layer.file]
            chain = ["format=rgba"]
            if layer.rot:
                chain.append(f"rotate=a='({layer.rot.expr()})*PI/180':ow='hypot(iw,ih)':oh=ow:c=none")
            factor = f"({layer.height.expr(SUPERSAMPLE / layer.of)})"
            chain.append(f"scale=w='max(2,trunc(iw*{factor}))':h='max(2,trunc(ih*{factor}))':eval=frame:flags=bicubic")
            graph.append(f"[{index}:v]{','.join(chain)}[p{n}]")
            graph.append(
                f"[{current}][p{n}]overlay=x='{layer.cx.expr(SUPERSAMPLE)}-w/2':y='{layer.cy.expr(SUPERSAMPLE)}-h/2':eval=frame:format=gbrp[b{n}]"
            )
        current = f"b{n}"
    flush()
    # RGB is converted to BT.709 explicitly and the clip tagged to match.
    graph.append(
        f"[{current}]scale={W}:{H}:flags=lanczos:out_color_matrix=bt709:out_range=tv,format=yuv420p,"
        "setparams=colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv[out]"
    )
    script = tmp / f"{tag}.graph"
    script.write_text(";\n".join(graph), encoding="utf-8")
    clip = tmp / f"{tag}.mp4"
    return [
        "ffmpeg", "-hide_banner", "-loglevel", "error", "-y", *inputs,
        "-/filter_complex", script, "-map", "[out]",
        "-frames:v", round(seconds * FPS), "-r", FPS,
        "-c:v", "libx264", "-profile:v", "high", "-preset", "medium", "-crf", "16", "-pix_fmt", "yuv420p",
        "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv",
        clip,
    ], clip


def scenes(design, colours, assets):
    """The four Scenes of the template, each with its own layout."""
    bg = assets / "bg.png"
    p1 = design["products"][0]
    p2 = design["products"][-1]
    hook, name = design["hook"].upper(), design["name"].upper()
    accent, ghost = colours["accent"], colours["ghost"]

    def product(p, cx, cy, height, rot=None):
        return Picture(assets / p["file"], cx, cy, height, p["productH"], rot)

    def narrowed(p, height):
        """The height given, or less for a Product too wide to be shown that tall inside the frame."""
        return round(height * min(1, MAX_ASPECT * p["productH"] / p["productW"]))

    # 1. Hook: the Product between two blocks of type that fill the frame.
    words = hook.split()
    half = (len(words) + 1) // 2
    blocks = [" ".join(words[:half]), " ".join(words[half:])]
    fitted = [fit(b, "ExtraBold", 940, 430, 220, leading=1.2) for b in blocks if b]
    size = min(s for s, _ in fitted)
    a = {"cx": 720, "cy": 870, "h": narrowed(p1, 560), "rot": -6}
    rising = product(p1, a["cx"], Anim(2500).to(a["cy"], 0.05, 0.65, "back"), Anim(a["h"] - 40).to(a["h"], 0, 3, "linear"), Anim(-10).to(a["rot"], 0, 3, "linear"))
    one, at = [bg], 0.08
    # The Product sits to the right of the short lines, clear of the words, and behind the second block.
    for block, top, colour, behind in zip(blocks, (150, 1140), (INK, accent), (rising, None)):
        if block:
            _, lines = fit(block, "ExtraBold", 940, 430, size, leading=1.2, min_size=size)
            for i, line in enumerate(lines):
                texts = by_word(line, "ExtraBold", size, LEFT, top + size * (1.0 + 1.2 * i), colour, at, 0.12)
                one += texts
                at += 0.12 * len(texts)
        if behind:
            one.append(behind)

    # 2. Reveal: the Product large, its name running behind it and set top left.
    b = {"cx": 560, "cy": 1150, "h": narrowed(p1, 1230)}
    size, lines = fit(name, "Bold", 900, 360, 150)
    running = f"{name}  ·  " * 4
    two = [
        bg,
        Text(running, "ExtraBold", 330, 0, 0, ghost, 0.1, rise=0, x_anim=Anim(-300).to(-900, 0, 5, "linear"), y_anim=Anim(770)),
        Text(running, "ExtraBold", 330, 0, 0, ghost, 0.2, rise=0, x_anim=Anim(-1900).to(-1300, 0, 5, "linear"), y_anim=Anim(1850)),
        product(
            p1,
            Anim(a["cx"]).to(b["cx"], 0, 0.7, "inout"),
            Anim(a["cy"]).to(b["cy"], 0, 0.7, "inout"),
            Anim(a["h"]).to(b["h"] - 80, 0, 0.7, "inout").to(b["h"], 0.7, 4.3, "linear"),
            Anim(a["rot"]).to(0, 0, 0.7, "inout"),
        ),
    ]
    for i, line in enumerate(lines):
        two += by_word(line, "Bold", size, LEFT, 150 + size * (1.0 + 1.16 * i), INK, 0.35 + 0.15 * i, 0.08)

    # 3. Facts: one at a time on a panel in the Product's colour; the Product, seen
    #    from its other photo, stands on the panel's edge.
    edge, seconds = 900, SCENE_SECONDS[2]
    facts = design["facts"]
    each = (seconds - 0.7) / len(facts)
    c = {"cx": 770, "h": narrowed(p2, 700)}
    c["cy"] = edge - c["h"] / 2
    height = Anim(c["h"])
    for i in range(1, len(facts)):
        height.to(c["h"] + 36, 0.45 + i * each, 0.14).to(c["h"], 0.59 + i * each, 0.3, "inout")
    three = [
        bg,
        product(p1, Anim(b["cx"]).to(-800, 0, 0.45, "in"), b["cy"], b["h"]),
        Panel(accent, Anim(H).to(edge, 0, 0.5), H - edge),
        product(p2, Anim(1700).to(c["cx"], 0.15, 0.6), c["cy"], height, Anim(8).to(0, 0.15, 0.6)),
    ]
    for i, fact in enumerate(facts):
        start = 0.45 + i * each
        end = None if i == len(facts) - 1 else start + each
        three.append(Text(f"{i + 1:02d}", "ExtraBold", 200, LEFT, 380, INK, start, end, rise=40))
        size, lines = fit(fact, "Bold", 900, 560, 140, min_size=56)
        for j, line in enumerate(lines):
            three.append(Text(line, "Bold", size, LEFT, edge + 90 + size * (1.0 + 1.16 * j), WHITE, start + 0.1 * (j + 1), end))
    three.append(Text(f"/ {len(facts):02d}", "SemiBold", 64, LEFT + 6, 480, accent, 0.45))

    # 4. Closing: the Product on a disc of its own colour, its name and the call to action.
    d = {"cy": 700, "h": narrowed(p1, 860)}
    size, lines = fit(name, "Bold", 900, 250, 104)
    four = [
        bg,
        Panel(accent, Anim(edge).to(H, 0, 0.4, "in"), H - edge),
        product(p2, c["cx"], Anim(c["cy"]).to(-700, 0, 0.4, "in"), c["h"]),
        Picture(assets / "disc.png", 540, d["cy"], Anim(4).to(980, 0.1, 0.6, "back"), 1000),
        product(
            p1,
            540,
            Anim(d["cy"] + 80).to(d["cy"], 0.15, 0.65).wave(10, 3),
            Anim(d["h"] * 0.35).to(d["h"], 0.15, 0.65, "back"),
            Anim(-14).to(0, 0.15, 0.65),
        ),
    ]
    for i, line in enumerate(lines):
        x = (W - measure(line, "Bold", size)) / 2
        four += by_word(line, "Bold", size, x, 1190 + size * (1.0 + 1.16 * i), INK, 0.7 + 0.15 * i, 0.08)
    cta, cta_size = design["cta"], 50
    cta_w = measure(cta, "SemiBold", cta_size)
    pill_w, pill_h = round(cta_w + 130), 132
    pill = Image.new("RGBA", (pill_w * 2, pill_h * 2), (0, 0, 0, 0))
    ImageDraw.Draw(pill).rounded_rectangle((0, 0, pill_w * 2 - 1, pill_h * 2 - 1), radius=pill_h, fill=accent)
    pill.save(assets / "pill.png")
    pill_y = Anim(2100).to(1500, 1.3, 0.55, "back").wave(5, 1.2)
    four.append(Picture(assets / "pill.png", 540, pill_y, pill_h, pill_h * 2))
    text_y = Anim(2100 + 18).to(1500 + 18, 1.3, 0.55, "back").wave(5, 1.2)
    four.append(Text(cta, "SemiBold", cta_size, (W - cta_w) / 2, 0, WHITE, 1.3, rise=0, y_anim=text_y))
    return [one, two, three, four]


def render_ffmpeg(design, colours, assets, output):
    with tempfile.TemporaryDirectory(prefix="motion-") as tmp:
        tmp = Path(tmp)
        jobs = [compile_scene(layers, s, tmp, f"scene-{i}") for i, (layers, s) in enumerate(zip(scenes(design, colours, assets), SCENE_SECONDS))]
        # A Scene does not depend on its neighbours' frames, so they render side by side.
        with ThreadPoolExecutor() as pool:
            list(pool.map(lambda job: run(job[0]), jobs))
        listing = tmp / "clips.txt"
        listing.write_text("".join(f"file '{clip}'\n" for _, clip in jobs), encoding="utf-8")
        total = sum(SCENE_SECONDS)
        run([
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "concat", "-safe", "0", "-i", listing,
            "-f", "lavfi", "-t", total, "-i", "anullsrc=channel_layout=stereo:sample_rate=44100",
            "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "128k",
            "-t", total, "-movflags", "+faststart", output,
        ])


# --- The Remotion build -------------------------------------------------------


def render_remotion(assets, output):
    # The packages and the browser are in the image; the template is read from the mounted folder.
    shutil.rmtree(REMOTION / "src", ignore_errors=True)
    shutil.copytree(ROOT / "remotion" / "src", REMOTION / "src")
    shutil.copy(ROOT / "remotion" / "tsconfig.json", REMOTION / "tsconfig.json")
    run(["npx", "tsc", "--noEmit"], cwd=REMOTION)
    run(
        [
            "npx", "remotion", "render", "src/index.ts", "Look", output,
            "--public-dir", assets, "--props", assets / "design.json",
            "--codec", "h264", "--crf", "16", "--color-space", "bt709", "--enforce-audio-track",
            "--image-format", "png", "--log", "error",
        ],
        cwd=REMOTION,
    )


# --- Checks -------------------------------------------------------------------


def check(file, label, checks):
    info = json.loads(run(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", file]))
    video = next((s for s in info["streams"] if s["codec_type"] == "video"), {})
    audio = next((s for s in info["streams"] if s["codec_type"] == "audio"), {})
    seconds, total = float(info["format"]["duration"]), sum(SCENE_SECONDS)
    colour = [video.get(k) for k in ("color_space", "color_primaries", "color_transfer")]
    checks[f"{label}: 1080x1920"] = (video.get("width"), video.get("height")) == (W, H)
    checks[f"{label}: video codec h264"] = video.get("codec_name") == "h264"
    checks[f"{label}: audio codec aac"] = audio.get("codec_name") == "aac"
    checks[f"{label}: duration {total}s (got {seconds:.2f}s)"] = abs(seconds - total) < 0.2
    checks[f"{label}: BT.709 colour (got {colour})"] = colour == ["bt709"] * 3


def frames_sheet(file, output):
    """Frames at two seconds, where the Hook must already be readable, and late in each Scene."""
    times, at = [("Hook at 2.0s", 2.0)], 0
    for i, s in enumerate(SCENE_SECONDS):
        if i:
            times.append((f"Scene {i + 1} at {at + s * 0.8:.1f}s", at + s * 0.8))
        at += s
    with tempfile.TemporaryDirectory() as tmp:
        panels = []
        for i, (label, t) in enumerate(times):
            frame = Path(tmp) / f"{i}.png"
            run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-ss", t, "-i", file, "-frames:v", "1", frame])
            panels.append((label, Image.open(frame).copy()))
        look.sheet(panels, (540, 960), output)



def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--sample", action="store_true")
    parser.add_argument("--only", choices=["ffmpeg", "remotion"])
    args = parser.parse_args()

    input_dir = ROOT / ("sample" if args.sample else "input")
    output_dir = ROOT / "output" / "motion"
    assets = output_dir / "assets"
    started = time.time()
    design, colours = make_assets(input_dir, assets, args.sample)
    print(f"assets made in {time.time() - started:.1f}s")
    slug = look.slugify(design["name"])

    builds = {
        "ffmpeg": lambda out: render_ffmpeg(design, colours, assets, out),
        "remotion": lambda out: render_remotion(assets, out),
    }
    checks, seconds = {}, {}
    for label, build in builds.items():
        if args.only not in (None, label):
            continue
        output = output_dir / f"{slug}-{label}.mp4"
        started = time.time()
        build(output)
        seconds[label] = round(time.time() - started, 1)
        print(f"{label}: {sum(SCENE_SECONDS)}s of video rendered in {seconds[label]}s")
        check(output, label, checks)
        frames_sheet(output, output_dir / f"{slug}-{label}-frames.png")

    for label, ok in checks.items():
        print(f"{'PASS' if ok else 'FAIL'}  {label}")
    print(f"output: {output_dir}")
    if not all(checks.values()):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
