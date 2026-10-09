// The Problem–Solution creative template. The Hook is a customer's problem,
// alone on a dark screen. The Product pushes it off as the solution, its Facts
// are ticked off one under another, and the closing asks for the click.
//
// Type stays clear of the top 130 px and the bottom 380 px, where the platforms
// draw their own interface.

import type { ReactNode } from 'react';
import { AbsoluteFill, Easing, Img, staticFile } from 'remotion';
import { BACK, IN, INOUT, Product, Type, between, carried, fit, leftBehind, mix, move, narrowed, useDuration, useSeconds, type Pose } from './motion';
import { HEIGHT, WIDTH, type Layer, type Layout, type Layouts, type SceneInput } from './scene';
import { FAMILY } from './typeface';

const LEFT = 80; // left edge of left-aligned type
const BAND = 800; // bottom of the band the Product stands in above the list of Facts
const LIST = 880; // top of the list of Facts

/**
 * Where each layout leaves the Product at the end of its Scene. The next Scene
 * starts from here. The problem is stated without the Product, so the Hook leaves none.
 */
const rest = (layout: Layout, layer: Layer): Pose | null => {
  switch (layout) {
    case 'Hook':
      return null;
    case 'Solution':
      return { cx: 560, cy: 1080, height: narrowed(layer, 900), rot: 0 };
    case 'Facts':
      return { cx: 540, cy: 450, height: narrowed(layer, 620), rot: 0 };
    default:
      return { cx: 540, cy: 900, height: narrowed(layer, 780), rot: 0 };
  }
};

// What each layout is set on: the problem on the dark, the closing on the
// Product's own colour, and everything between on the studio backdrop.
type Ground = 'dark' | 'studio' | 'colour';
const groundOf = (layout: Layout): Ground => (layout === 'Hook' ? 'dark' : layout === 'Closing' ? 'colour' : 'studio');

const Ground = ({ of, scene }: { of: Ground; scene: SceneInput }) =>
  of === 'studio' ? (
    <Img src={staticFile(scene.backdrop)} style={{ position: 'absolute', width: WIDTH, height: HEIGHT }} />
  ) : (
    <AbsoluteFill style={{ background: of === 'dark' ? scene.colours.ink : scene.colours.accent }} />
  );

// The ground, which pushes the one the Scene before was set on off the top of
// the frame, and a photo this Scene does not show leaving sideways.
const Stage = ({ scene, children }: { scene: SceneInput; children: ReactNode }) => {
  const t = useSeconds();
  const { previous } = scene;
  const before = previous ? groundOf(previous.layout) : null;
  const now = groundOf(scene.layout);
  const pushed = before && before !== now ? move(t, 0, 0.4, INOUT) : 1;
  const leaving = leftBehind(scene, rest);
  return (
    <AbsoluteFill style={{ background: 'white', overflow: 'hidden' }}>
      {before && pushed < 1 && (
        <AbsoluteFill style={{ transform: `translateY(${-HEIGHT * pushed}px)` }}>
          <Ground of={before} scene={scene} />
        </AbsoluteFill>
      )}
      <AbsoluteFill style={{ transform: `translateY(${HEIGHT * (1 - pushed)}px)` }}>
        <Ground of={now} scene={scene} />
      </AbsoluteFill>
      {leaving && <Product layer={previous!.product} pose={{ ...leaving, cx: mix(leaving.cx, -800, move(t, 0, 0.45, IN)) }} />}
      {children}
    </AbsoluteFill>
  );
};

// The Product moves on from where the Scene before left it, or comes in from
// outside the frame, tilted, and settles.
const useArrival = (scene: SceneInput, from: 'below' | 'above' = 'below'): Pose => {
  const t = useSeconds();
  const to = rest(scene.layout, scene.product)!;
  const before = carried(scene, rest);
  const outside = from === 'below' ? HEIGHT + to.height : -to.height;
  return before
    ? between(before, to, move(t, 0, 0.7, INOUT))
    : { ...to, cy: mix(outside, to.cy, move(t, 0.25, 0.6, BACK)), rot: mix(10, to.rot, move(t, 0.25, 0.6)) };
};

// 1. Hook: the problem in capitals that fill the dark frame, and nothing else.
//    The words arrive 0.12 seconds apart, so the thirteen a Hook may have are
//    all in place before two seconds. A bar is drawn under them as they come.
const Hook = ({ scene }: { scene: SceneInput }) => {
  const t = useSeconds();
  const duration = useDuration();
  const leading = 1.12;
  const { size, lines } = fit((scene.lines[0] ?? '').toUpperCase(), 'ExtraBold', { width: 920, height: 1080, maxSize: 210, minSize: 60, leading });
  const top = 190;
  // The problem leaves before the Scene ends: the next one opens on an empty dark frame and pushes it away.
  const leaving = 1 - move(t, duration - 0.2, 0.2, IN);
  return (
    <Stage scene={scene}>
      <Type lines={lines} weight="ExtraBold" size={size} colour="white" at={0.08} leading={leading} out={duration} style={{ left: LEFT, top }} />
      <div
        style={{
          position: 'absolute',
          left: LEFT,
          top: top + lines.length * size * leading + 50,
          width: 920 * move(t, 0.08, 1.8, Easing.linear),
          height: 22,
          background: scene.colours.tint,
          opacity: leaving,
        }}
      />
    </Stage>
  );
};

// 2. Solution: the studio pushes the dark away, and the Product comes up into
//    it across a tilted slab of its own pale colour, under a tag and its name.
const Solution = ({ scene }: { scene: SceneInput }) => {
  const t = useSeconds();
  const duration = useDuration();
  const [label = '', name = ''] = scene.lines;
  const { size, lines } = fit(name.toUpperCase(), 'ExtraBold', { width: 920, height: 330, maxSize: 150, minSize: 40 });

  const pose = useArrival(scene);
  // Once it has arrived it goes on growing a little for the rest of the Scene.
  pose.height -= 60 * (1 - move(t, 0.85, duration - 0.85, Easing.linear)) * move(t, 0, 0.7, INOUT);
  return (
    <Stage scene={scene}>
      <div
        style={{
          position: 'absolute',
          left: -220,
          top: 860,
          width: WIDTH + 440,
          height: 560,
          background: scene.colours.tint,
          transformOrigin: 'left center',
          transform: `rotate(-8deg) scaleX(${move(t, 0.2, 0.5)})`,
        }}
      />
      <Product layer={scene.product} pose={pose} />
      <div
        style={{
          position: 'absolute',
          left: LEFT,
          top: 150,
          height: 84,
          padding: '0 34px',
          borderRadius: 12,
          background: scene.colours.accent,
          color: 'white',
          fontFamily: FAMILY,
          fontWeight: 600,
          fontSize: 44,
          lineHeight: '84px',
          letterSpacing: '0.08em',
          whiteSpace: 'nowrap',
          opacity: move(t, 0.3, 0.15),
          transformOrigin: 'left center',
          transform: `scale(${mix(0.6, 1, move(t, 0.3, 0.45, BACK))})`,
        }}
      >
        {label.toUpperCase()}
      </div>
      <Type lines={lines} weight="ExtraBold" size={size} colour={scene.colours.ink} at={0.5} step={0.08} lineStep={0.15} style={{ left: LEFT, top: 275 }} />
    </Stage>
  );
};

// A box in the Product's colour that pops in with its tick.
const Tick = ({ at, top, colour }: { at: number; top: number; colour: string }) => {
  const t = useSeconds();
  return (
    <div
      style={{
        position: 'absolute',
        left: LEFT,
        top,
        width: 76,
        height: 76,
        borderRadius: 18,
        background: colour,
        opacity: move(t, at, 0.12),
        transform: `scale(${mix(0.4, 1, move(t, at, 0.4, BACK))})`,
      }}
    >
      <div style={{ position: 'absolute', left: 27, top: 11, width: 18, height: 38, borderRight: '9px solid white', borderBottom: '9px solid white', transform: 'rotate(45deg)' }} />
    </div>
  );
};

// 3. Facts: the Product in a band of its own pale colour, and under it the
//    Facts, each ticked in turn and left standing. The Product nudges at every
//    tick. The timing is FactPacing in the domain, which decides how many Facts
//    a Storyboard shows.
const Facts = ({ scene }: { scene: SceneInput }) => {
  const t = useSeconds();
  const duration = useDuration();
  const facts = scene.lines;
  const each = (duration - 0.6) / Math.max(1, facts.length);
  const row = Math.min(300, 640 / Math.max(1, facts.length));
  // One size for the whole list: the largest at which the longest Fact fits its row.
  const box = { width: 800, height: row - 40, maxSize: 76, minSize: 34 };
  const size = Math.min(box.maxSize, ...facts.map((fact) => fit(fact, 'Bold', box).size));

  const pose = useArrival(scene, 'above');
  for (let i = 0; i < facts.length; i++) {
    pose.height += 30 * (move(t, 0.6 + i * each, 0.14) - move(t, 0.74 + i * each, 0.3, INOUT));
  }
  return (
    <Stage scene={scene}>
      <div style={{ position: 'absolute', left: 0, width: WIDTH, top: mix(-BAND, 0, move(t, 0, 0.5)), height: BAND, background: scene.colours.tint }} />
      <Product layer={scene.product} pose={pose} />
      {facts.map((fact, i) => {
        const start = 0.6 + i * each;
        const { lines } = fit(fact, 'Bold', { ...box, maxSize: size, minSize: size });
        return (
          <div key={i}>
            <Tick at={start} top={LIST + i * row + Math.max(0, (size * 1.16 - 76) / 2)} colour={scene.colours.accent} />
            <Type lines={lines} weight="Bold" size={size} colour={scene.colours.ink} at={start + 0.1} step={0.06} style={{ left: LEFT + 112, top: LIST + i * row }} />
          </div>
        );
      })}
    </Stage>
  );
};

// 4. Closing: the Product's own colour fills the frame. Its name in white
//    capitals above it, and the call to action in a white pill under it.
const Closing = ({ scene }: { scene: SceneInput }) => {
  const t = useSeconds();
  const [name = '', cta = ''] = scene.lines;
  const { size, lines } = fit(name.toUpperCase(), 'ExtraBold', { width: 920, height: 300, maxSize: 124, minSize: 40 });

  const pose = useArrival(scene);
  const to = rest('Closing', scene.product)!;
  return (
    <Stage scene={scene}>
      <div
        style={{
          position: 'absolute',
          left: to.cx - 520,
          top: to.cy - 520,
          width: 1040,
          height: 1040,
          borderRadius: '50%',
          background: 'radial-gradient(circle, rgba(255, 255, 255, 0.3), rgba(255, 255, 255, 0) 68%)',
          opacity: move(t, 0.3, 0.6),
        }}
      />
      <Product layer={scene.product} pose={pose} />
      <Type lines={lines} weight="ExtraBold" size={size} colour="white" at={0.5} step={0.08} lineStep={0.15} style={{ left: LEFT, top: 170 }} />
      <div style={{ position: 'absolute', left: 0, width: WIDTH, top: 1390, display: 'flex', justifyContent: 'center' }}>
        <div
          style={{
            height: 132,
            padding: '0 65px',
            borderRadius: 66,
            background: 'white',
            color: scene.colours.accent,
            fontFamily: FAMILY,
            fontWeight: 700,
            fontSize: 50,
            lineHeight: '132px',
            whiteSpace: 'nowrap',
            opacity: move(t, 1.3, 0.15),
            transform: `scale(${mix(0.6, 1, move(t, 1.3, 0.55, BACK)) * (1 + 0.02 * Math.sin((2 * Math.PI * t) / 1.2))})`,
          }}
        >
          {cta}
        </div>
      </div>
    </Stage>
  );
};

export const ProblemSolution: Layouts = { Hook, Solution, Facts, Closing };
