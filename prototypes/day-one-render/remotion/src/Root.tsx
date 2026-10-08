import { Composition } from 'remotion';
import { Look, type Design } from './Look';

// Only here so the composition can be listed; a render always passes design.json as props.
const placeholder: Design = {
  name: '',
  hook: '',
  facts: [],
  cta: '',
  fps: 30,
  sceneSeconds: [3, 5, 7, 5],
  colours: { accent: '#333333', tint: '#dddddd', ghost: '#e5e5e5', ink: '#141416' },
  products: [],
};

export const Root = () => (
  <Composition
    id="Look"
    component={Look}
    width={1080}
    height={1920}
    fps={30}
    durationInFrames={600}
    defaultProps={placeholder}
    calculateMetadata={({ props }) => ({
      fps: props.fps,
      durationInFrames: Math.round(props.sceneSeconds.reduce((a, b) => a + b, 0) * props.fps),
    })}
  />
);
