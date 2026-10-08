// Day-one render test (ticket 01). Throwaway prototype: judged by eye, then discarded.
// Runs inside the container built from the Dockerfile next to it.
//
//   node render.mjs            render /work/input (product.json + photos)
//   node render.mjs --sample   render /work/sample with a generated placeholder photo

import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

const W = 1080;
const H = 1920;
const FPS = 30;
const SUPERSAMPLE = 2; // stills are composed at 2x so the zoom stays smooth
const CROSSFADE = 0.4;
const SCENE_SHARES = [0.15, 0.25, 0.35, 0.25]; // hook, reveal, facts, closing

const FONT = {
  regular: '/fonts/BeVietnamPro-Regular.ttf',
  semibold: '/fonts/BeVietnamPro-SemiBold.ttf',
  bold: '/fonts/BeVietnamPro-Bold.ttf',
};

const CARD = { maxW: 900, maxH: 1000, centerY: 720, radius: 36 };
const MAX_ZOOM = 1.14;
const TEXT_ZONE = { top: 1430, bottom: 1860, maxW: 960 };

const root = '/work';
const useSample = process.argv.includes('--sample');
const inputDir = path.join(root, useSample ? 'sample' : 'input');
const outputDir = path.join(root, 'output');
const tmpDir = fs.mkdtempSync(path.join(root, 'tmp-'));

function run(cmd, args) {
  const r = spawnSync(cmd, args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (r.status !== 0) {
    throw new Error(`${cmd} failed (${r.status}):\n${(r.stderr || '').slice(-4000)}`);
  }
  return r.stdout;
}

const ffmpeg = (args) => run('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', ...args]);

function probe(file) {
  return JSON.parse(
    run('ffprobe', ['-v', 'error', '-show_streams', '-show_format', '-of', 'json', file]),
  );
}

const even = (n) => Math.max(2, Math.round(n / 2) * 2);

// drawtext does not wrap, so lines are broken here. Width is estimated from the
// font size; Be Vietnam Pro averages a little over half an em per character.
function wrap(text, fontSize, maxLines) {
  const breakAt = (maxChars) => {
    const lines = [];
    let line = '';
    for (const word of text.trim().split(/\s+/)) {
      const next = line ? `${line} ${word}` : word;
      if (next.length > maxChars && line) {
        lines.push(line);
        line = word;
      } else {
        line = next;
      }
    }
    if (line) lines.push(line);
    return lines;
  };
  for (let size = fontSize; size >= 36; size -= 4) {
    const maxChars = Math.floor(TEXT_ZONE.maxW / (size * 0.58));
    let lines = breakAt(maxChars);
    if (lines.length > maxLines || lines.some((l) => l.length > maxChars)) continue;
    // Narrow the limit while the line count holds, so no line is left with one word.
    for (let limit = maxChars - 1; limit > 0; limit--) {
      const tighter = breakAt(limit);
      if (tighter.length !== lines.length || tighter.some((l) => l.length > limit)) break;
      lines = tighter;
    }
    return { size, lines };
  }
  throw new Error(`Text does not fit on screen: "${text}"`);
}

let textFileCount = 0;

// Text goes to ffmpeg through a file, never through the filter string, so no
// user text needs escaping.
function drawText({ text, font, size, y, appearAt, color = 'white', box = false }) {
  const file = path.join(tmpDir, `text-${textFileCount++}.txt`);
  fs.writeFileSync(file, text, 'utf8');
  const fade = `min(1,max(0,(t-${appearAt})/0.45))`;
  const opts = [
    `fontfile=${font}`,
    `textfile=${file}`,
    `fontsize=${size}`,
    `fontcolor=${color}`,
    `x=(w-text_w)/2`,
    `y='${y}+28*(1-${fade})'`,
    `alpha='${fade}'`,
  ];
  if (box) {
    opts.push('box=1', 'boxcolor=white@0.95', 'boxborderw=30');
  } else {
    opts.push('shadowcolor=black@0.55', 'shadowx=0', 'shadowy=4');
  }
  return `drawtext=${opts.join(':')}`;
}

// Lays a block of lines out from the top of the text zone and returns the filters.
function textBlock(entries) {
  const filters = [];
  let y = TEXT_ZONE.top;
  for (const e of entries) {
    for (const [i, line] of e.lines.entries()) {
      filters.push(drawText({ ...e, text: line, y, appearAt: e.appearAt + i * 0.12 }));
      y += Math.round(e.size * 1.3);
    }
    y += e.gapAfter ?? 0;
  }
  if (y > TEXT_ZONE.bottom + 40) {
    throw new Error('Too much text for one scene; shorten the hook, name or facts.');
  }
  return filters;
}

function makePlaceholderPhoto(file) {
  const label = path.join(tmpDir, 'placeholder.txt');
  fs.writeFileSync(label, 'ẢNH MẪU', 'utf8');
  ffmpeg([
    '-f', 'lavfi', '-i', 'color=c=0xECE8E1:s=1200x1200',
    '-vf',
    `drawbox=x=300:y=380:w=600:h=440:color=0x1B1B1F:t=fill,` +
      `drawtext=fontfile=${FONT.bold}:textfile=${label}:fontsize=72:fontcolor=white:x=(w-text_w)/2:y=(h-text_h)/2`,
    '-frames:v', '1', file,
  ]);
}

// Decodes each photo once into a normalised PNG (applies camera rotation, rejects
// anything ffmpeg cannot read as an image) and returns its size.
function normalisePhotos(files) {
  return files.map((src, i) => {
    const file = path.join(tmpDir, `photo-${i}.png`);
    ffmpeg(['-i', src, '-frames:v', '1', '-pix_fmt', 'rgba', file]);
    const { width, height } = probe(file).streams[0];
    return { file, width, height };
  });
}

function makeGradient(file) {
  // Starts below the lowest point the photo reaches at full zoom, so the
  // product itself is never darkened.
  const start = Math.ceil(CARD.centerY + (CARD.maxH / 2) * MAX_ZOOM) + 4;
  ffmpeg([
    '-f', 'lavfi', '-i', `color=c=black:s=${W}x${H},format=rgba`,
    '-vf', `geq=r=0:g=0:b=0:a='255*0.6*clip((Y-${start})/160,0,1)'`,
    '-frames:v', '1', file,
  ]);
}

// One still per scene: the photo, untouched apart from scaling and rounded
// corners, on a blurred and darkened copy of itself.
function composeStill(photo, file) {
  const S = SUPERSAMPLE;
  const fit = Math.min((CARD.maxW * S) / photo.width, (CARD.maxH * S) / photo.height);
  const fw = even(photo.width * fit);
  const fh = even(photo.height * fit);
  const r = CARD.radius * S;
  const corner =
    `if(gt(abs(X-W/2),W/2-${r})*gt(abs(Y-H/2),H/2-${r}),` +
    `lte(hypot(abs(X-W/2)-(W/2-${r}),abs(Y-H/2)-(H/2-${r})),${r}),1)`;
  const graph = [
    `[0:v]split=2[a][b]`,
    `[a]scale=${W * S}:${H * S}:force_original_aspect_ratio=increase,crop=${W * S}:${H * S},` +
      `scale=iw/8:ih/8,gblur=sigma=14,scale=${W * S}:${H * S}:flags=bicubic,` +
      `eq=brightness=-0.38:saturation=0.8[bg]`,
    `[b]scale=${fw}:${fh}:flags=lanczos,format=rgba,` +
      `geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='alpha(X,Y)*${corner}'[fg]`,
    `[bg][fg]overlay=x=(W-w)/2:y=${CARD.centerY * S}-h/2[out]`,
  ].join(';');
  ffmpeg(['-i', photo.file, '-filter_complex', graph, '-map', '[out]', '-frames:v', '1', file]);
}

const MOTION = {
  pushIn: (n, amount) => ({
    z: `1+${amount}*on/${n - 1}`,
    x: 'iw/2-(iw/zoom/2)',
    y: `${CARD.centerY * SUPERSAMPLE}*(1-1/zoom)`,
  }),
  pullOut: (n, amount) => ({
    z: `1+${amount}*(1-on/${n - 1})`,
    x: 'iw/2-(iw/zoom/2)',
    y: `${CARD.centerY * SUPERSAMPLE}*(1-1/zoom)`,
  }),
  drift: (n, amount) => ({
    z: `${1 + amount}`,
    x: `(iw-iw/zoom)*on/${n - 1}`,
    y: `${CARD.centerY * SUPERSAMPLE}*(1-1/zoom)`,
  }),
};

function renderScene({ still, gradient, seconds, motion, text, file }) {
  const frames = Math.round(seconds * FPS);
  const m = motion(frames);
  const graph = [
    `[0:v]zoompan=z='${m.z}':x='${m.x}':y='${m.y}':d=${frames}:s=${W}x${H}:fps=${FPS}[z]`,
    `[z][1:v]overlay=0:0[g]`,
    `[g]${[...text, 'format=yuv420p'].join(',')}[out]`,
  ].join(';');
  ffmpeg([
    '-i', still, '-i', gradient,
    '-filter_complex', graph, '-map', '[out]',
    '-frames:v', String(frames), '-r', String(FPS),
    '-c:v', 'libx264', '-preset', 'medium', '-crf', '14', '-pix_fmt', 'yuv420p',
    file,
  ]);
}

function joinScenes(clips, durations, total, file) {
  const inputs = clips.flatMap((c) => ['-i', c]);
  const steps = [];
  let previous = '0:v';
  let offset = 0;
  for (let i = 1; i < clips.length; i++) {
    offset += durations[i - 1];
    const label = i === clips.length - 1 ? 'v' : `x${i}`;
    steps.push(
      `[${previous}][${i}:v]xfade=transition=fade:duration=${CROSSFADE}:offset=${offset}[${label}]`,
    );
    previous = label;
  }
  ffmpeg([
    ...inputs,
    '-f', 'lavfi', '-t', String(total), '-i', 'anullsrc=channel_layout=stereo:sample_rate=44100',
    '-filter_complex', steps.join(';'),
    '-map', '[v]', '-map', `${clips.length}:a`,
    '-c:v', 'libx264', '-profile:v', 'high', '-preset', 'medium', '-crf', '18',
    '-pix_fmt', 'yuv420p', '-r', String(FPS),
    '-c:a', 'aac', '-b:a', '128k',
    '-t', String(total), '-movflags', '+faststart',
    file,
  ]);
}

function main() {
  const product = JSON.parse(fs.readFileSync(path.join(inputDir, 'product.json'), 'utf8'));
  const total = product.durationSeconds ?? 20;
  if (!product.name || !product.hook || !product.facts?.length) {
    throw new Error('product.json needs "name", "hook" and at least one entry in "facts".');
  }

  let photoFiles = fs
    .readdirSync(inputDir)
    .filter((f) => /\.(jpe?g|png|webp)$/i.test(f))
    .sort()
    .map((f) => path.join(inputDir, f));
  if (useSample && photoFiles.length === 0) {
    const placeholder = path.join(tmpDir, 'placeholder.png');
    makePlaceholderPhoto(placeholder);
    photoFiles = [placeholder];
  }
  if (photoFiles.length === 0) {
    throw new Error(`No photos found in ${inputDir}. Add .jpg, .png or .webp files.`);
  }

  const photos = normalisePhotos(photoFiles);
  const photoFor = (scene) => photos[scene % photos.length];
  const gradient = path.join(tmpDir, 'gradient.png');
  makeGradient(gradient);

  const durations = SCENE_SHARES.map((s) => s * total);
  const hook = wrap(product.hook, 92, 3);
  const name = wrap(product.name, 84, 2);
  const cta = wrap(product.cta ?? 'Xem chi tiết sản phẩm', 48, 1);
  const facts = product.facts.map((f) => wrap(f, 56, 2));
  const factSize = Math.min(...facts.map((f) => f.size));
  const factStep = (durations[2] - 1.2) / facts.length;

  const scenes = [
    {
      motion: (n) => MOTION.pushIn(n, 0.06),
      text: textBlock([{ ...hook, font: FONT.bold, appearAt: 0.25 }]),
    },
    {
      motion: (n) => MOTION.pushIn(n, MAX_ZOOM - 1),
      text: textBlock([{ ...name, font: FONT.bold, appearAt: 0.5 }]),
    },
    {
      motion: (n) => MOTION.drift(n, 0.08),
      text: textBlock(
        facts.map((f, i) => ({
          lines: f.lines,
          size: factSize,
          font: FONT.semibold,
          appearAt: 0.5 + i * factStep,
          gapAfter: 26,
        })),
      ),
    },
    {
      motion: (n) => MOTION.pullOut(n, 0.1),
      text: textBlock([
        { ...name, font: FONT.bold, appearAt: 0.4, gapAfter: 70 },
        { ...cta, font: FONT.semibold, appearAt: 1.0, color: '0x111111', box: true },
      ]),
    },
  ];

  const clips = scenes.map((scene, i) => {
    const still = path.join(tmpDir, `still-${i}.png`);
    const clip = path.join(tmpDir, `scene-${i}.mp4`);
    composeStill(photoFor(i), still);
    const isLast = i === scenes.length - 1;
    renderScene({
      still,
      gradient,
      seconds: durations[i] + (isLast ? 0 : CROSSFADE),
      motion: scene.motion,
      text: scene.text,
      file: clip,
    });
    console.log(`scene ${i + 1}/${scenes.length} rendered`);
    return clip;
  });

  fs.mkdirSync(outputDir, { recursive: true });
  const slug = product.name.normalize('NFD').replace(/[^\w]+/g, '-').replace(/^-|-$/g, '').toLowerCase();
  const output = path.join(outputDir, `${slug || 'video'}.mp4`);
  joinScenes(clips, durations, total, output);

  // A frame from the middle of each scene, for a quick look without a player.
  let at = 0;
  durations.forEach((d, i) => {
    ffmpeg([
      '-ss', String(at + d * 0.8), '-i', output,
      '-frames:v', '1', '-vf', 'scale=540:-2',
      path.join(outputDir, `${slug}-scene-${i + 1}.png`),
    ]);
    at += d;
  });

  const info = probe(output);
  const video = info.streams.find((s) => s.codec_type === 'video');
  const audio = info.streams.find((s) => s.codec_type === 'audio');
  const seconds = Number(info.format.duration);
  const checks = {
    'resolution 1080x1920': video?.width === W && video?.height === H,
    'video codec h264': video?.codec_name === 'h264',
    'audio codec aac': audio?.codec_name === 'aac',
    [`duration ${total}s (got ${seconds.toFixed(2)}s)`]: Math.abs(seconds - total) < 0.2,
  };
  for (const [label, ok] of Object.entries(checks)) console.log(`${ok ? 'PASS' : 'FAIL'}  ${label}`);
  console.log(`output: ${output}`);
  if (Object.values(checks).includes(false)) process.exitCode = 1;
}

try {
  main();
} finally {
  fs.rmSync(tmpDir, { recursive: true, force: true });
}
