// What every creative template is built from: easing, type that is measured and
// animated by word, and the Product's image, which is only ever scaled, moved,
// rotated and faded (Product Lock).

import type { CSSProperties } from 'react';
import { Easing, Img, interpolate, staticFile, useCurrentFrame, useVideoConfig } from 'remotion';
import type { Layer } from './scene';
import { FAMILY, WEIGHTS, type Weight } from './typeface';

export const OUT = Easing.out(Easing.cubic);
export const IN = Easing.in(Easing.cubic);
export const INOUT = Easing.inOut(Easing.cubic);
export const BACK = Easing.out(Easing.back(1.7)); // overshoots, then settles

/** Progress from 0 to 1 of a move that starts at `at` seconds and lasts `over`. */
export const move = (t: number, at: number, over: number, easing = OUT) =>
  interpolate(t, [at, at + over], [0, 1], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp', easing });

export const mix = (from: number, to: number, p: number) => from + (to - from) * p;

/** Seconds since the Scene began. */
export const useSeconds = () => useCurrentFrame() / useVideoConfig().fps;

let canvas: CanvasRenderingContext2D | null = null;
const measure = (text: string, weight: Weight, size: number) => {
  canvas ??= document.createElement('canvas').getContext('2d')!;
  canvas.font = `${WEIGHTS[weight]} ${size}px "${FAMILY}"`;
  return canvas.measureText(text).width;
};

export const words = (text: string) => text.split(/\s+/).filter(Boolean);

type Box = { width: number; height: number; maxSize: number; minSize: number; leading?: number };

/**
 * The text broken into lines at the largest size at which it fits the box. The
 * planner only sends text within the layout's limits; text that still does not
 * fit is set at the smallest size, never left out.
 */
export const fit = (text: string, weight: Weight, { width, height, maxSize, minSize, leading = 1.16 }: Box) => {
  let lines: string[] = [];
  for (let size = maxSize; ; size -= 2) {
    lines = [];
    let line = '';
    for (const word of words(text)) {
      const longer = `${line} ${word}`.trim();
      if (line && measure(longer, weight, size) > width) {
        lines.push(line);
        line = word;
      } else {
        line = longer;
      }
    }
    lines.push(line);
    const fits = lines.length * size * leading <= height && lines.every((l) => measure(l, weight, size) <= width);
    if (fits || size - 2 < minSize) return { size, lines };
  }
};

/** Where the Product is: its centre, its height on screen and its tilt in degrees. */
export type Pose = { cx: number; cy: number; height: number; rot: number };

export const between = (from: Pose, to: Pose, p: number): Pose => ({
  cx: mix(from.cx, to.cx, p),
  cy: mix(from.cy, to.cy, p),
  height: mix(from.height, to.height, p),
  rot: mix(from.rot, to.rot, p),
});

export const Product = ({ layer, pose, opacity }: { layer: Layer; pose: Pose; opacity?: number }) => (
  <Img
    src={staticFile(layer.file)}
    style={{
      position: 'absolute',
      left: -layer.width / 2,
      top: -layer.height / 2,
      opacity,
      transform: `translate(${pose.cx}px, ${pose.cy}px) rotate(${pose.rot}deg) scale(${pose.height / layer.productHeight})`,
    }}
  />
);

/** Lines of type whose words rise into place one after another from behind a mask. */
export const Type = ({
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
  /** When the first word starts to arrive, in seconds. */
  at: number;
  /** Between one word starting to arrive and the next. */
  step?: number;
  /** When set, lines start this far apart and each counts its own words. */
  lineStep?: number;
  /** When the type has faded out, if it does. */
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
            const p = move(t, at + i * lineStep + (lineStep ? j : word++) * step, 0.4, OUT);
            const gap = j === all.length - 1 ? '-0.02em' : '0.24em';
            return (
              // The padding leaves room for stacked diacritics inside the mask.
              <span key={j} style={{ display: 'inline-block', overflow: 'hidden', padding: '0.3em 0.02em 0.12em', margin: `-0.3em ${gap} -0.12em -0.02em`, lineHeight: 1 }}>
                {/* Not drawn at all until it starts to rise: stacked diacritics reach past the mask. */}
                <span style={{ display: 'inline-block', opacity: p > 0 ? 1 : 0, transform: `translateY(${mix(115, 0, p)}%)` }}>{text}</span>
              </span>
            );
          })}
        </div>
      ))}
    </div>
  );
};
