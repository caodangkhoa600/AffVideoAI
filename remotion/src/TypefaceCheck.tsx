import { AbsoluteFill } from 'remotion';
import { FAMILY, WEIGHTS, useTypeface } from './typeface';

// One frame of Vietnamese in every bundled weight. Rendering it inside the
// worker container with the network off proves the image can draw text.
const SAMPLES = [
  'Tiếng Việt đầy đủ dấu',
  'Ắ Ằ Ẳ Ẵ Ặ Ấ Ầ Ẩ Ẫ Ậ',
  'ế ề ể ễ ệ ố ồ ổ ỗ ộ ớ ờ ở ỡ ợ',
  'ứ ừ ử ữ ự ỳ ý ỷ ỹ ỵ đ Đ',
];

export const TypefaceCheck = () => {
  useTypeface();
  return (
    <AbsoluteFill style={{ background: '#f4f2ee', color: '#141416', fontFamily: FAMILY, padding: 80, gap: 40, justifyContent: 'center' }}>
      {Object.entries(WEIGHTS).map(([name, weight]) => (
        <div key={name} style={{ fontWeight: weight }}>
          <div style={{ fontSize: 36, opacity: 0.6 }}>{name}</div>
          {SAMPLES.map((line) => (
            <div key={line} style={{ fontSize: 64, lineHeight: 1.35 }}>
              {line}
            </div>
          ))}
        </div>
      ))}
    </AbsoluteFill>
  );
};
