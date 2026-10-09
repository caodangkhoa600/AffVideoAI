import { Composition } from 'remotion';
import { ProductShowcase } from './ProductShowcase';
import { FPS, HEIGHT, WIDTH, type SceneInput } from './scene';
import { TypefaceCheck } from './TypefaceCheck';
import { useTypeface } from './typeface';

const TEMPLATES = { ProductShowcase };

// One Scene of a Storyboard, drawn by the layout of its creative template.
// Nothing is measured or drawn until the typeface is in.
const Scene = (scene: SceneInput) => {
  const ready = useTypeface();
  const Layout = TEMPLATES[scene.template][scene.layout];
  return ready ? <Layout scene={scene} /> : null;
};

// Only here so the composition can be listed; a render always passes its own Scene as props.
const placeholder: SceneInput = {
  template: 'ProductShowcase',
  layout: 'Hook',
  durationInFrames: 3 * FPS,
  lines: [''],
  product: { file: 'fonts/OFL.txt', width: 1, height: 1, productWidth: 1, productHeight: 1 },
  previous: null,
  backdrop: 'fonts/OFL.txt',
  colours: { accent: '#22242a', tint: '#e2e2e4', ghost: '#e8e6e2', ink: '#141416' },
};

export const Root = () => (
  <>
    <Composition
      id="Scene"
      component={Scene}
      width={WIDTH}
      height={HEIGHT}
      fps={FPS}
      durationInFrames={placeholder.durationInFrames}
      defaultProps={placeholder}
      calculateMetadata={({ props }) => ({ durationInFrames: props.durationInFrames })}
    />
    <Composition id="TypefaceCheck" component={TypefaceCheck} width={WIDTH} height={HEIGHT} fps={FPS} durationInFrames={1} />
  </>
);
