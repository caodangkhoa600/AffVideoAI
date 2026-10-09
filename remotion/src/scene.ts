// What the worker hands a render: one Scene of a Storyboard, as data. Text and
// file names arrive here and nowhere else; none of it is ever run as code.

import type { ReactNode } from 'react';

/** A Product photo as a video shows it: cut out, or whole on a card, standing on its shadow. */
export type Layer = {
  /** In the render's public folder. */
  file: string;
  /** Of the file, in pixels: the Product with room around it for its shadow. */
  width: number;
  height: number;
  /** Of the Product within the file. It is centred. */
  productWidth: number;
  productHeight: number;
};

/** The layouts of a creative template (SceneLayout in the domain). */
export type Layout = 'Hook' | 'Reveal' | 'Facts' | 'Closing' | 'Solution';

export type Colours = { accent: string; tint: string; ghost: string; ink: string };

export type SceneInput = {
  /** The creative template whose layouts draw the Storyboard (CreativeTemplate in the domain). */
  template: 'LuxuryCinematic' | 'ProductShowcase' | 'ProblemSolution';
  layout: Layout;
  durationInFrames: number;
  /** The Scene's on-screen text. What each line is depends on the layout. */
  lines: string[];
  /** The photo this Scene shows. */
  product: Layer;
  /** What the Scene before left on screen, which this Scene carries on from. Null for the first Scene. */
  previous: { layout: Layout; product: Layer } | null;
  /** The studio backdrop, in the render's public folder. */
  backdrop: string;
  /** Taken from the Product's own colour. */
  colours: Colours;
};

/** The layouts a creative template draws: one for each Scene of its structure. */
export type Layouts = Partial<Record<Layout, (props: { scene: SceneInput }) => ReactNode>>;

export const WIDTH = 1080;
export const HEIGHT = 1920;
export const FPS = 30;
