// The Product Showcase creative template: the look the founder approved in
// ticket 26. Four layouts, no two alike. A Scene is rendered on its own, so each
// layout starts with what the Scene before left on screen and carries on from it.
//
// Type stays clear of the top 130 px and the bottom 380 px, where the platforms
// draw their own interface.

import type { ReactNode } from 'react';
import { AbsoluteFill, Easing, Img, staticFile } from 'remotion';
import { BACK, IN, INOUT, Product, Type, between, carried, fit, leftBehind, mix, move, narrowed, useDuration, useSeconds, words, type Pose } from './motion';
import { HEIGHT, WIDTH, type Layer, type Layout, type Layouts, type SceneInput } from './scene';
import { FAMILY } from './typeface';

const LEFT = 80; // left edge of left-aligned type
const EDGE = 900; // top of the Facts panel, which the Product stands on

/** Where each layout leaves the Product at the end of its Scene. The next Scene starts from here. */
const rest = (layout: Layout, layer: Layer): Pose => {
  switch (layout) {
    case 'Hook':
      return { cx: 720, cy: 870, height: narrowed(layer, 560), rot: -6 };
    case 'Reveal':
      return { cx: 560, cy: 1150, height: narrowed(layer, 1230), rot: 0 };
    case 'Facts': {
      const height = narrowed(layer, 700);
      return { cx: 770, cy: EDGE - height / 2, height, rot: 0 };
    }
    default:
      return { cx: 540, cy: 700, height: narrowed(layer, 860), rot: 0 };
  }
};

// The backdrop, and on it whatever the Scene before left that this one does not
// keep: the Facts panel drops away, and a photo this Scene does not show leaves.
const Stage = ({ scene, children }: { scene: SceneInput; children: ReactNode }) => {
  const t = useSeconds();
  const { previous } = scene;
  const fromFacts = previous?.layout === 'Facts' && scene.layout !== 'Facts';
  const leaving = leftBehind(scene, rest);
  const gone = move(t, 0, fromFacts ? 0.4 : 0.45, IN);
  return (
    <AbsoluteFill style={{ background: 'white' }}>
      <Img src={staticFile(scene.backdrop)} style={{ position: 'absolute', width: WIDTH, height: HEIGHT }} />
      {leaving && !fromFacts && <Product layer={previous!.product} pose={{ ...leaving, cx: mix(leaving.cx, -800, gone) }} />}
      {fromFacts && <Panel top={mix(EDGE, HEIGHT, gone)} colour={scene.colours.accent} />}
      {leaving && fromFacts && <Product layer={previous!.product} pose={{ ...leaving, cy: mix(leaving.cy, -700, gone) }} />}
      {children}
    </AbsoluteFill>
  );
};

const Panel = ({ top, colour }: { top: number; colour: string }) => (
  <div style={{ position: 'absolute', left: 0, width: WIDTH, top, height: HEIGHT, background: colour }} />
);

// 1. Hook: the Product between two blocks of capitals that fill the frame. The
//    words arrive 0.12 seconds apart, so the thirteen a Hook may have are all in
//    place before two seconds.
const Hook = ({ scene }: { scene: SceneInput }) => {
  const t = useSeconds();
  const duration = useDuration();
  const all = words((scene.lines[0] ?? '').toUpperCase());
  const half = Math.ceil(all.length / 2);
  const blocks = [all.slice(0, half).join(' '), all.slice(half).join(' ')].filter(Boolean);
  const box = { width: 940, height: 390, maxSize: 220, minSize: 40, leading: 1.2 };
  const size = Math.min(...blocks.map((block) => fit(block, 'ExtraBold', box).size), box.maxSize);
  const [first = [], second] = blocks.map((block) => fit(block, 'ExtraBold', { ...box, maxSize: size, minSize: size }).lines);
  const firstWords = words(first.join(' ')).length;

  const to = rest('Hook', scene.product);
  const from = carried(scene, rest);
  const pose = from
    ? between(from, to, move(t, 0, 0.7, INOUT))
    : {
        cx: to.cx,
        cy: mix(2500, to.cy, move(t, 0.05, 0.65, BACK)),
        height: mix(to.height - 40, to.height, t / duration),
        rot: mix(-10, to.rot, t / duration),
      };
  return (
    <Stage scene={scene}>
      <Type lines={first} weight="ExtraBold" size={size} colour={scene.colours.ink} at={0.08} leading={1.2} style={{ left: LEFT, top: 150 }} />
      <Product layer={scene.product} pose={pose} />
      {second && (
        <Type lines={second} weight="ExtraBold" size={size} colour={scene.colours.accent} at={0.08 + firstWords * 0.12} leading={1.2} style={{ left: LEFT, top: 1140 }} />
      )}
    </Stage>
  );
};

// 2. Reveal: the Product large and upright, its name top left, and the name
//    again very large and pale, running sideways behind it.
const Reveal = ({ scene }: { scene: SceneInput }) => {
  const t = useSeconds();
  const duration = useDuration();
  const name = (scene.lines[0] ?? '').toUpperCase();
  const { size, lines } = fit(name, 'Bold', { width: 900, height: 360, maxSize: 150, minSize: 40 });
  const running = {
    position: 'absolute',
    whiteSpace: 'pre',
    fontFamily: FAMILY,
    fontWeight: 800,
    fontSize: 330,
    lineHeight: 1,
    color: scene.colours.ghost,
  } as const;
  const text = `${name}  ·  `.repeat(4);

  const to = rest('Reveal', scene.product);
  const from = carried(scene, rest) ?? { ...to, cy: HEIGHT + to.height };
  const arrive = move(t, 0, 0.7, INOUT);
  // It arrives a little small and grows for the rest of the Scene.
  const pose = between(from, { ...to, height: to.height - 80 }, arrive);
  pose.height += 80 * move(t, 0.7, duration - 0.7, Easing.linear);
  return (
    <Stage scene={scene}>
      <div style={{ ...running, top: 480, opacity: move(t, 0.1, 0.4), transform: `translateX(${mix(-300, -900, t / duration)}px)` }}>{text}</div>
      <div style={{ ...running, top: 1560, opacity: move(t, 0.2, 0.4), transform: `translateX(${mix(-1900, -1300, t / duration)}px)` }}>{text}</div>
      <Product layer={scene.product} pose={pose} />
      <Type lines={lines} weight="Bold" size={size} colour={scene.colours.ink} at={0.35} step={0.08} lineStep={0.15} style={{ left: LEFT, top: 150 }} />
    </Stage>
  );
};

// 3. Facts: one at a time in white on a panel in the Product's own colour, with
//    a large counter top left. The Product stands on the panel's edge and nudges
//    at each new Fact. The timing is FactPacing in the domain, which decides how
//    many Facts a Storyboard shows.
const Facts = ({ scene }: { scene: SceneInput }) => {
  const t = useSeconds();
  const duration = useDuration();
  const facts = scene.lines;
  const each = (duration - 0.7) / Math.max(1, facts.length);

  const to = rest('Facts', scene.product);
  const from = carried(scene, rest);
  const enter = from ? move(t, 0, 0.7, INOUT) : move(t, 0.15, 0.6);
  const pose = between(from ?? { ...to, cx: 1700, rot: 8 }, to, enter);
  for (let i = 1; i < facts.length; i++) {
    pose.height += 36 * (move(t, 0.45 + i * each, 0.14) - move(t, 0.59 + i * each, 0.3, INOUT));
  }
  const stays = scene.previous?.layout === 'Facts';
  return (
    <Stage scene={scene}>
      <Panel top={stays ? EDGE : mix(HEIGHT, EDGE, move(t, 0, 0.5))} colour={scene.colours.accent} />
      <Product layer={scene.product} pose={pose} />
      <div style={{ position: 'absolute', left: LEFT + 6, top: 420, fontFamily: FAMILY, fontWeight: 600, fontSize: 64, color: scene.colours.accent, opacity: move(t, 0.45, 0.3) }}>
        / {String(facts.length).padStart(2, '0')}
      </div>
      {facts.map((fact, i) => {
        const start = 0.45 + i * each;
        const end = i === facts.length - 1 ? undefined : start + each;
        const { size, lines } = fit(fact, 'Bold', { width: 900, height: 540, maxSize: 140, minSize: 40 });
        return (
          <div key={i}>
            <Type lines={[String(i + 1).padStart(2, '0')]} weight="ExtraBold" size={200} colour={scene.colours.ink} at={start} out={end} style={{ left: LEFT, top: 215 }} />
            <Type lines={lines} weight="Bold" size={size} colour="white" at={start + 0.1} step={0.06} out={end} style={{ left: LEFT, top: EDGE + 90 }} />
          </div>
        );
      })}
    </Stage>
  );
};

// 4. Closing: the Product on a disc of its own pale colour, its name, and the
//    call to action in a pill.
const Closing = ({ scene }: { scene: SceneInput }) => {
  const t = useSeconds();
  const [name = '', cta = ''] = scene.lines;
  const { size, lines } = fit(name.toUpperCase(), 'Bold', { width: 900, height: 230, maxSize: 104, minSize: 40 });

  const to = rest('Closing', scene.product);
  const from = carried(scene, rest);
  const enter = move(t, 0.15, 0.65);
  const pose = from
    ? between(from, to, move(t, 0, 0.7, INOUT))
    : {
        cx: to.cx,
        cy: mix(to.cy + 80, to.cy, enter),
        height: mix(to.height * 0.35, to.height, move(t, 0.15, 0.65, BACK)),
        rot: mix(-14, 0, enter),
      };
  pose.cy += 10 * Math.sin((2 * Math.PI * t) / 3);
  return (
    <Stage scene={scene}>
      <div
        style={{
          position: 'absolute',
          left: to.cx - 470,
          top: to.cy - 470,
          width: 940,
          height: 940,
          borderRadius: '50%',
          background: scene.colours.tint,
          transform: `scale(${move(t, 0.1, 0.6, BACK)})`,
        }}
      />
      <Product layer={scene.product} pose={pose} />
      <Type
        lines={lines}
        weight="Bold"
        size={size}
        colour={scene.colours.ink}
        at={0.7}
        step={0.08}
        lineStep={0.15}
        style={{ left: 0, width: WIDTH, top: 1170, textAlign: 'center' }}
      />
      <div style={{ position: 'absolute', left: 0, width: WIDTH, top: 1400, display: 'flex', justifyContent: 'center' }}>
        <div
          style={{
            height: 132,
            padding: '0 65px',
            borderRadius: 66,
            background: scene.colours.accent,
            color: 'white',
            fontFamily: FAMILY,
            fontWeight: 600,
            fontSize: 50,
            lineHeight: '132px',
            whiteSpace: 'nowrap',
            opacity: move(t, 1.3, 0.15),
            transform: `translateY(${5 * Math.sin((2 * Math.PI * t) / 1.2)}px) scale(${mix(0.6, 1, move(t, 1.3, 0.55, BACK))})`,
          }}
        >
          {cta}
        </div>
      </div>
    </Stage>
  );
};

export const ProductShowcase: Layouts = { Hook, Reveal, Facts, Closing };
