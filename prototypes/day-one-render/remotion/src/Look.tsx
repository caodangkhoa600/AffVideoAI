// Look test 2 (ticket 26), the Remotion build. Throwaway prototype.
// The same four Scenes as motion.py builds with FFmpeg filters, from the same
// assets. The Product is an image that is only ever scaled, moved and rotated.

import { useEffect, useState, type CSSProperties, type ReactNode } from 'react';
import {
  AbsoluteFill,
  Easing,
  Img,
  Sequence,
  cancelRender,
  continueRender,
  delayRender,
  interpolate,
  staticFile,
  useCurrentFrame,
  useVideoConfig,
} from 'remotion';

type ProductLayer = { file: string; w: number; h: number; productW: number; productH: number };

export type Design = {
  name: string;
  hook: string;
  facts: string[];
  cta: string;
  fps: number;
  sceneSeconds: number[];
  colours: { accent: string; tint: string; ghost: string; ink: string };
  products: ProductLayer[];
};

const W = 1080;
const H = 1920;
const LEFT = 80;
const FAMILY = 'Be Vietnam Pro';
const WEIGHTS = { SemiBold: 600, Bold: 700, ExtraBold: 800 } as const;
type Weight = keyof typeof WEIGHTS;

const OUT = Easing.out(Easing.cubic);
const IN = Easing.in(Easing.cubic);
const INOUT = Easing.inOut(Easing.cubic);
const BACK = Easing.out(Easing.back(1.7)); // overshoots, then settles

// Progress from 0 to 1 of a move that starts at `at` seconds and lasts `over`.
const move = (t: number, at: number, over: number, easing = OUT) =>
  interpolate(t, [at, at + over], [0, 1], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp', easing });
const mix = (from: number, to: number, p: number) => from + (to - from) * p;

const useSeconds = () => useCurrentFrame() / useVideoConfig().fps;

let canvas: CanvasRenderingContext2D | null = null;
const measure = (text: string, weight: Weight, size: number) => {
  canvas ??= document.createElement('canvas').getContext('2d')!;
  canvas.font = `${WEIGHTS[weight]} ${size}px "${FAMILY}"`;
  return canvas.measureText(text).width;
};

// The largest size at which the text, broken into lines, fits the box.
const fit = (text: string, weight: Weight, maxW: number, maxH: number, maxSize: number, leading = 1.16, minSize = 40) => {
  for (let size = maxSize; size >= minSize; size -= 2) {
    const lines: string[] = [];
    let line = '';
    for (const word of text.split(/\s+/).filter(Boolean)) {
      const longer = `${line} ${word}`.trim();
      if (line && measure(longer, weight, size) > maxW) {
        lines.push(line);
        line = word;
      } else {
        line = longer;
      }
    }
    lines.push(line);
    if (lines.length * size * leading <= maxH && lines.every((l) => measure(l, weight, size) <= maxW)) {
      return { size, lines };
    }
  }
  throw new Error(`Text does not fit on screen: "${text}"`);
};

const Product = ({ p, cx, cy, height, rot = 0 }: { p: ProductLayer; cx: number; cy: number; height: number; rot?: number }) => (
  <Img
    src={staticFile(p.file)}
    style={{
      position: 'absolute',
      left: -p.w / 2,
      top: -p.h / 2,
      transform: `translate(${cx}px, ${cy}px) rotate(${rot}deg) scale(${height / p.productH})`,
    }}
  />
);

// Lines of type whose words rise into place one after another from behind the line below.
const Type = ({
  lines,
  weight,
  size,
  colour,
  at,
  step = 0.12,
  lineStep = 0,
  out,
  leading = 1.16,
  style,
}: {
  lines: string[];
  weight: Weight;
  size: number;
  colour: string;
  at: number;
  step?: number;
  lineStep?: number;
  out?: number;
  leading?: number;
  style?: CSSProperties;
}) => {
  const t = useSeconds();
  let word = 0;
  const leaving = out === undefined ? 1 : 1 - move(t, out - 0.2, 0.2, IN);
  return (
    <div style={{ position: 'absolute', fontFamily: FAMILY, fontWeight: WEIGHTS[weight], fontSize: size, color: colour, opacity: leaving, ...style }}>
      {lines.map((line, i) => (
        <div key={i} style={{ height: size * leading, whiteSpace: 'nowrap' }}>
          {line.split(' ').map((text, j, all) => {
            // Staggered lines each count their own words; otherwise the count runs on through the block.
            const p = move(t, at + i * lineStep + (lineStep ? j : word++) * step, 0.4, OUT);
            const gap = j === all.length - 1 ? '-0.02em' : '0.24em';
            return (
              // The padding leaves room for stacked diacritics inside the mask.
              <span key={j} style={{ display: 'inline-block', overflow: 'hidden', padding: '0.3em 0.02em 0.12em', margin: `-0.3em ${gap} -0.12em -0.02em`, lineHeight: 1 }}>
                <span style={{ display: 'inline-block', transform: `translateY(${mix(115, 0, p)}%)` }}>{text}</span>
              </span>
            );
          })}
        </div>
      ))}
    </div>
  );
};

const Backdrop = ({ children }: { children: ReactNode }) => (
  <AbsoluteFill>
    <Img src={staticFile('bg.png')} style={{ position: 'absolute', width: W, height: H }} />
    {children}
  </AbsoluteFill>
);

// The poses the Product holds at the end of each Scene; the next Scene starts from them.
const A = { cx: 720, cy: 870, h: 560, rot: -6 };
const B = { cx: 560, cy: 1150, h: 1230 };
const EDGE = 900;
const C = { cx: 770, h: 700 };
const D = { cy: 700, h: 860 };

// The height given, or less for a Product too wide to be shown that tall inside the frame.
const MAX_ASPECT = 0.86;
const narrowed = (p: ProductLayer, height: number) => Math.round(height * Math.min(1, (MAX_ASPECT * p.productH) / p.productW));

// 1. Hook: the Product between two blocks of type that fill the frame.
const HookScene = ({ d }: { d: Design }) => {
  const t = useSeconds();
  const words = d.hook.toUpperCase().split(/\s+/).filter(Boolean);
  const half = Math.ceil(words.length / 2);
  const blocks = [words.slice(0, half).join(' '), words.slice(half).join(' ')].filter(Boolean);
  const size = Math.min(...blocks.map((b) => fit(b, 'ExtraBold', 940, 430, 220, 1.2).size));
  const [first, second] = blocks.map((b) => fit(b, 'ExtraBold', 940, 430, size, 1.2, size).lines);
  const firstWords = first.join(' ').split(' ').length;
  const h = narrowed(d.products[0], A.h);
  return (
    <Backdrop>
      <Type lines={first} weight="ExtraBold" size={size} colour={d.colours.ink} at={0.08} leading={1.2} style={{ left: LEFT, top: 150 }} />
      <Product p={d.products[0]} cx={A.cx} cy={mix(2500, A.cy, move(t, 0.05, 0.65, BACK))} height={mix(h - 40, h, t / 3)} rot={mix(-10, A.rot, t / 3)} />
      {second && (
        <Type lines={second} weight="ExtraBold" size={size} colour={d.colours.accent} at={0.08 + firstWords * 0.12} leading={1.2} style={{ left: LEFT, top: 1140 }} />
      )}
    </Backdrop>
  );
};

// 2. Reveal: the Product large, its name running behind it and set top left.
const RevealScene = ({ d }: { d: Design }) => {
  const t = useSeconds();
  const name = d.name.toUpperCase();
  const { size, lines } = fit(name, 'Bold', 900, 360, 150);
  const arrive = move(t, 0, 0.7, INOUT);
  const running: CSSProperties = {
    position: 'absolute',
    whiteSpace: 'pre',
    fontFamily: FAMILY,
    fontWeight: 800,
    fontSize: 330,
    lineHeight: 1,
    color: d.colours.ghost,
  };
  const text = `${name}  ·  `.repeat(4);
  const from = narrowed(d.products[0], A.h);
  const to = narrowed(d.products[0], B.h);
  return (
    <Backdrop>
      <div style={{ ...running, top: 480, opacity: move(t, 0.1, 0.4), transform: `translateX(${mix(-300, -900, t / 5)}px)` }}>{text}</div>
      <div style={{ ...running, top: 1560, opacity: move(t, 0.2, 0.4), transform: `translateX(${mix(-1900, -1300, t / 5)}px)` }}>{text}</div>
      <Product
        p={d.products[0]}
        cx={mix(A.cx, B.cx, arrive)}
        cy={mix(A.cy, B.cy, arrive)}
        height={mix(from, to - 80, arrive) + 80 * move(t, 0.7, 4.3, Easing.linear)}
        rot={mix(A.rot, 0, arrive)}
      />
      <Type lines={lines} weight="Bold" size={size} colour={d.colours.ink} at={0.35} step={0.08} lineStep={0.15} style={{ left: LEFT, top: 150 }} />
    </Backdrop>
  );
};

// 3. Facts: one at a time on a panel in the Product's colour; the Product, seen
//    from its other photo, stands on the panel's edge.
const FactsScene = ({ d, seconds }: { d: Design; seconds: number }) => {
  const t = useSeconds();
  const each = (seconds - 0.7) / d.facts.length;
  const other = d.products[d.products.length - 1];
  const rest = narrowed(other, C.h);
  let height = rest;
  for (let i = 1; i < d.facts.length; i++) {
    height += 36 * (move(t, 0.45 + i * each, 0.14) - move(t, 0.59 + i * each, 0.3, INOUT));
  }
  const enter = move(t, 0.15, 0.6);
  return (
    <Backdrop>
      <Product p={d.products[0]} cx={mix(B.cx, -800, move(t, 0, 0.45, IN))} cy={B.cy} height={narrowed(d.products[0], B.h)} />
      <div
        style={{
          position: 'absolute',
          left: 0,
          width: W,
          top: mix(H, EDGE, move(t, 0, 0.5)),
          height: H,
          background: d.colours.accent,
        }}
      />
      <Product p={other} cx={mix(1700, C.cx, enter)} cy={EDGE - rest / 2} height={height} rot={mix(8, 0, enter)} />
      <div style={{ position: 'absolute', left: LEFT + 6, top: 420, fontFamily: FAMILY, fontWeight: 600, fontSize: 64, color: d.colours.accent, opacity: move(t, 0.45, 0.3) }}>
        / {String(d.facts.length).padStart(2, '0')}
      </div>
      {d.facts.map((fact, i) => {
        const start = 0.45 + i * each;
        const end = i === d.facts.length - 1 ? undefined : start + each;
        const { size, lines } = fit(fact, 'Bold', 900, 560, 140, 1.16, 56);
        return (
          <div key={i}>
            <Type lines={[String(i + 1).padStart(2, '0')]} weight="ExtraBold" size={200} colour={d.colours.ink} at={start} out={end} style={{ left: LEFT, top: 215 }} />
            <Type lines={lines} weight="Bold" size={size} colour="white" at={start + 0.1} step={0.06} out={end} style={{ left: LEFT, top: EDGE + 90 }} />
          </div>
        );
      })}
    </Backdrop>
  );
};

// 4. Closing: the Product on a disc of its own colour, its name and the call to action.
const ClosingScene = ({ d }: { d: Design }) => {
  const t = useSeconds();
  const other = d.products[d.products.length - 1];
  const { size, lines } = fit(d.name.toUpperCase(), 'Bold', 900, 250, 104);
  const enter = move(t, 0.15, 0.65);
  const pop = move(t, 1.3, 0.55, BACK);
  const leaving = narrowed(other, C.h);
  const h = narrowed(d.products[0], D.h);
  return (
    <Backdrop>
      <div style={{ position: 'absolute', left: 0, width: W, top: mix(EDGE, H, move(t, 0, 0.4, IN)), height: H, background: d.colours.accent }} />
      <Product p={other} cx={C.cx} cy={mix(EDGE - leaving / 2, -700, move(t, 0, 0.4, IN))} height={leaving} />
      <div
        style={{
          position: 'absolute',
          left: 540 - 490,
          top: D.cy - 490,
          width: 980,
          height: 980,
          borderRadius: '50%',
          background: d.colours.tint,
          transform: `scale(${move(t, 0.1, 0.6, BACK)})`,
        }}
      />
      <Product
        p={d.products[0]}
        cx={540}
        cy={mix(D.cy + 80, D.cy, enter) + 10 * Math.sin((2 * Math.PI * t) / 3)}
        height={mix(h * 0.35, h, move(t, 0.15, 0.65, BACK))}
        rot={mix(-14, 0, enter)}
      />
      <Type
        lines={lines}
        weight="Bold"
        size={size}
        colour={d.colours.ink}
        at={0.7}
        step={0.08}
        lineStep={0.15}
        style={{ left: 0, width: W, top: 1190, textAlign: 'center' }}
      />
      <div style={{ position: 'absolute', left: 0, width: W, top: 1434, display: 'flex', justifyContent: 'center' }}>
        <div
          style={{
            height: 132,
            padding: '0 65px',
            borderRadius: 66,
            background: d.colours.accent,
            color: 'white',
            fontFamily: FAMILY,
            fontWeight: 600,
            fontSize: 50,
            lineHeight: '132px',
            opacity: move(t, 1.3, 0.15),
            transform: `translateY(${5 * Math.sin((2 * Math.PI * t) / 1.2)}px) scale(${mix(0.6, 1, pop)})`,
          }}
        >
          {d.cta}
        </div>
      </div>
    </Backdrop>
  );
};

export const Look = (d: Design) => {
  // Nothing is measured or drawn until the typeface is in.
  const [ready, setReady] = useState(false);
  const [handle] = useState(() => delayRender('typeface'));
  useEffect(() => {
    Promise.all(
      Object.entries(WEIGHTS).map(async ([name, weight]) => {
        const face = new FontFace(FAMILY, `url(${staticFile(`BeVietnamPro-${name}.ttf`)})`, { weight: String(weight) });
        document.fonts.add(await face.load());
      }),
    )
      .then(() => {
        setReady(true);
        continueRender(handle);
      })
      .catch(cancelRender);
  }, [handle]);
  if (!ready || d.products.length === 0) return null;

  const frames = d.sceneSeconds.map((s) => Math.round(s * d.fps));
  const from = frames.map((_, i) => frames.slice(0, i).reduce((a, b) => a + b, 0));
  const scenes = [<HookScene d={d} />, <RevealScene d={d} />, <FactsScene d={d} seconds={d.sceneSeconds[2]} />, <ClosingScene d={d} />];
  return (
    <AbsoluteFill style={{ background: 'white' }}>
      {scenes.map((scene, i) => (
        <Sequence key={i} from={from[i]} durationInFrames={frames[i]}>
          {scene}
        </Sequence>
      ))}
    </AbsoluteFill>
  );
};
