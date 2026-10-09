import { useEffect, useState } from 'react';
import { cancelRender, continueRender, delayRender, staticFile } from 'remotion';

// Be Vietnam Pro, SIL Open Font License (public/fonts/OFL.txt). The files ship
// in the worker image, so a render needs no network.
export const FAMILY = 'Be Vietnam Pro';

export const WEIGHTS = { Light: 300, SemiBold: 600, Bold: 700, ExtraBold: 800 } as const;

export type Weight = keyof typeof WEIGHTS;

// Holds the render until every weight is loaded, and fails it if one is
// missing: a frame is never drawn in a fallback typeface. Answers whether the
// typeface is in, so that nothing is measured before it is.
export const useTypeface = () => {
  const [ready, setReady] = useState(false);
  const [handle] = useState(() => delayRender('typeface'));
  useEffect(() => {
    Promise.all(
      Object.entries(WEIGHTS).map(async ([name, weight]) => {
        const face = new FontFace(FAMILY, `url(${staticFile(`fonts/BeVietnamPro-${name}.ttf`)})`, {
          weight: String(weight),
        });
        document.fonts.add(await face.load());
      }),
    ).then(
      () => {
        setReady(true);
        continueRender(handle);
      },
      (error) => cancelRender(error),
    );
  }, [handle]);
  return ready;
};
