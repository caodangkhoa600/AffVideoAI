// The Luxury Cinematic creative template: three long Scenes, little text, and
// nothing that arrives faster than a fade. The Product is lit out of the dark
// over the Hook, seen close over its Facts, and set on a pale plinth to close.
//
// Type stays clear of the top 130 px and the bottom 380 px, where the platforms
// draw their own interface.

import type { CSSProperties, ReactNode } from 'react';
import { AbsoluteFill } from 'remotion';
import { IN, INOUT, Product, between, carried, fit, leftBehind, move, narrowed, useDuration, useSeconds, words, type Pose } from './motion';
import { WIDTH, type Colours, type Layer, type Layout, type Layouts, type SceneInput } from './scene';
import { FAMILY, WEIGHTS, type Weight } from './typeface';

const LEFT = 90; // left edge of left-aligned type
const DARK = '#08080a';
const PLINTH = 1040; // bottom of the pale panel the closing sets the Product on

/** Where each layout leaves the Product at the end of its Scene. The next Scene starts from here. */
const rest = (layout: Layout, layer: Layer): Pose => {
  switch (layout) {
    case 'Hook':
      return { cx: 540, cy: 690, height: narrowed(layer, 900), rot: 0 };
    case 'Facts':
      // Close, and off centre: it runs past the edge of the frame when it is wide.
      return { cx: 700, cy: 600, height: narrowed(layer, 1060), rot: 0 };
    default:
      return { cx: 540, cy: 570, height: narrowed(layer, 760), rot: 0 };
  }
};

// What each layout is set on: the dark with one light behind the Product, the
// Product's own deep colour, and a pale plinth over the dark.
const ground = (layout: Layout, { accent, ghost }: Colours) => {
  switch (layout) {
    case 'Hook':
      return `radial-gradient(ellipse 80% 48% at 50% 38%, color-mix(in srgb, ${accent} 75%, black), ${DARK})`;
    case 'Facts':
      return `linear-gradient(160deg, color-mix(in srgb, ${accent} 80%, white), ${accent})`;
    default:
      return `linear-gradient(180deg, #f6f3ee, ${ghost} ${PLINTH}px, ${DARK} ${PLINTH}px)`;
  }
};

// The ground, faded through from the one the Scene before was set on, and a
// photo this Scene does not show fading away.
const Stage = ({ scene, children }: { scene: SceneInput; children: ReactNode }) => {
  const t = useSeconds();
  const { previous } = scene;
  const through = previous && previous.layout !== scene.layout ? move(t, 0, 1, INOUT) : 1;
  const leaving = leftBehind(scene, rest);
  return (
    <AbsoluteFill style={{ background: DARK }}>
      {previous && through < 1 && <AbsoluteFill style={{ background: ground(previous.layout, scene.colours) }} />}
      <AbsoluteFill style={{ background: ground(scene.layout, scene.colours), opacity: through }} />
      {leaving && <Product layer={previous!.product} pose={leaving} opacity={1 - move(t, 0, 0.6, IN)} />}
      {children}
    </AbsoluteFill>
  );
};

// The Product glides from where the Scene before left it, or fades in where
// this Scene rests it. `settled` is 0 while it has yet to leave where it was.
const useArrival = (scene: SceneInput) => {
  const t = useSeconds();
  const to = rest(scene.layout, scene.product);
  const from = carried(scene, rest);
  const settled = from ? move(t, 0, 1.4, INOUT) : 1;
  return { pose: from ? between(from, to, settled) : { ...to }, opacity: from ? 1 : move(t, 0.2, 1, INOUT), settled };
};

/** Lines of type whose words fade in one after another. Nothing rises and nothing is masked. */
const Soft = ({
  lines,
  weight,
  size,
  colour,
  at,
  step,
  over = 0.8,
  out,
  tracking = 0,
  leading = 1.3,
  style,
}: {
  lines: string[];
  weight: Weight;
  size: number;
  colour: string;
  /** When the first word starts to fade in, in seconds. */
  at: number;
  /** Between one word starting to fade in and the next. */
  step: number;
  /** How long a word takes to fade in. */
  over?: number;
  /** When the type has faded out, if it does. */
  out?: number;
  /** Space added between letters, in ems. */
  tracking?: number;
  leading?: number;
  style?: CSSProperties;
}) => {
  const t = useSeconds();
  let word = 0;
  const leaving = out === undefined ? 1 : 1 - move(t, out - 0.3, 0.3, IN);
  return (
    <div style={{ position: 'absolute', fontFamily: FAMILY, fontWeight: WEIGHTS[weight], fontSize: size, color: colour, letterSpacing: `${tracking}em`, opacity: leaving, ...style }}>
      {lines.map((line, i) => (
        <div key={i} style={{ height: size * leading, lineHeight: `${size * leading}px`, whiteSpace: 'pre' }}>
          {line.split(' ').map((text, j) => {
            const p = move(t, at + word++ * step, over, INOUT);
            return (
              <span key={j} style={{ display: 'inline-block', opacity: p, transform: `translateY(${14 * (1 - p)}px)` }}>
                {j > 0 ? ` ${text}` : text}
              </span>
            );
          })}
        </div>
      ))}
    </div>
  );
};

// `fit` measures type as it is set without tracking, so tracked type is fitted to a narrower box:
// a letter is a little over half an em wide, so tracking widens a line by about 1.8 times itself.
const tracked = (width: number, tracking: number) => width / (1 + 1.8 * tracking);

// A hairline that draws outwards from its middle.
const Rule = ({ at, top, width, colour }: { at: number; top: number; width: number; colour: string }) => {
  const drawn = width * move(useSeconds(), at, 0.9, INOUT);
  return <div style={{ position: 'absolute', left: (WIDTH - drawn) / 2, width: drawn, top, height: 2, background: colour }} />;
};

// 1. Hook: the Product lit out of the dark, pushing in slowly, with the Hook
//    under it. The words fade in closer together the more of them there are:
//    the last starts by 1.3 seconds and takes 0.6, so the Hook is whole before two.
const Hook = ({ scene }: { scene: SceneInput }) => {
  const t = useSeconds();
  const duration = useDuration();
  const { size, lines } = fit(scene.lines[0] ?? '', 'Light', { width: 880, height: 290, maxSize: 78, minSize: 34, leading: 1.3 });
  const step = Math.min(0.15, 1 / Math.max(1, words(lines.join(' ')).length - 1));

  const { pose, opacity, settled } = useArrival(scene);
  pose.height *= 1 + 0.07 * (t / duration - 1) * settled;
  return (
    <Stage scene={scene}>
      <Product layer={scene.product} pose={pose} opacity={opacity} />
      <Rule at={0.2} top={1195} width={120} colour={scene.colours.tint} />
      <Soft lines={lines} weight="Light" size={size} colour="white" at={0.3} step={step} over={0.6} style={{ left: 0, width: WIDTH, top: 1240, textAlign: 'center' }} />
    </Stage>
  );
};

// 2. Facts: the Product close and off centre on its own deep colour, drifting,
//    and one Fact at a time under it. The timing is FactPacing in the domain,
//    which decides how many Facts a Storyboard shows.
const Facts = ({ scene }: { scene: SceneInput }) => {
  const t = useSeconds();
  const duration = useDuration();
  const facts = scene.lines;
  const each = (duration - 0.8) / Math.max(1, facts.length);
  const numbered = (n: number) => String(n).padStart(2, '0');

  const { pose, opacity, settled } = useArrival(scene);
  pose.cx += 36 * (t / duration - 1) * settled;
  return (
    <Stage scene={scene}>
      <Product layer={scene.product} pose={pose} opacity={opacity} />
      {facts.map((fact, i) => {
        const start = 0.8 + i * each;
        const end = i === facts.length - 1 ? undefined : start + each;
        const { size, lines } = fit(fact, 'Light', { width: 880, height: 300, maxSize: 84, minSize: 36, leading: 1.3 });
        return (
          <div key={i}>
            {facts.length > 1 && (
              <Soft lines={[`${numbered(i + 1)} / ${numbered(facts.length)}`]} weight="SemiBold" size={30} colour={scene.colours.tint} at={start} step={0} out={end} tracking={0.3} style={{ left: LEFT, top: 1150 }} />
            )}
            <Soft lines={lines} weight="Light" size={size} colour="white" at={start} step={0.15} out={end} style={{ left: LEFT, top: 1220 }} />
          </div>
        );
      })}
    </Stage>
  );
};

// 3. Closing: the Product on a pale plinth, and under it on the dark its name
//    in spaced capitals and the call to action, over a rule.
const Closing = ({ scene }: { scene: SceneInput }) => {
  const [name = '', cta = ''] = scene.lines;
  const { size, lines } = fit(name.toUpperCase(), 'SemiBold', { width: tracked(900, 0.18), height: 240, maxSize: 66, minSize: 28, leading: 1.35 });

  const { pose, opacity } = useArrival(scene);
  return (
    <Stage scene={scene}>
      <Product layer={scene.product} pose={pose} opacity={opacity} />
      <Soft lines={lines} weight="SemiBold" size={size} colour="white" at={0.6} step={0.12} tracking={0.18} leading={1.35} style={{ left: 0, width: WIDTH, top: PLINTH + 90, textAlign: 'center' }} />
      <Soft lines={[cta.toUpperCase()]} weight="SemiBold" size={34} colour={scene.colours.tint} at={1.2} step={0.1} tracking={0.22} style={{ left: 0, width: WIDTH, top: 1420, textAlign: 'center' }} />
      <Rule at={1.5} top={1492} width={220} colour={scene.colours.tint} />
    </Stage>
  );
};

export const LuxuryCinematic: Layouts = { Hook, Facts, Closing };
