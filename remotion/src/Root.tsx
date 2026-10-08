import { Composition } from 'remotion';
import { TypefaceCheck } from './TypefaceCheck';

export const Root = () => (
  <Composition id="TypefaceCheck" component={TypefaceCheck} width={1080} height={1920} fps={30} durationInFrames={1} />
);
