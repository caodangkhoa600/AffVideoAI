# Remotion draws the Scenes, FFmpeg joins them

Each Scene of a Rendered Video is drawn by Remotion: a creative template is a set of React components, laid out with CSS and rendered frame by frame in Chrome Headless Shell inside the worker's container. FFmpeg stays for what surrounds that: joining Scene clips, audio, and probing the result. We chose this over drawing Scenes with FFmpeg filters alone after building the same 20-second video both ways (ticket 26). The founder judged the result good enough to post. Remotion rendered it faster (about 30 s against 47 to 69 s), moved type in ways `drawtext` cannot (masked reveals, scaling, sub-pixel motion), and made a new creative template a component to write, where FFmpeg needed a new set of filter expression strings that fail only at render time.

## Consequences

- The worker image gains Node, the Remotion packages and Chrome Headless Shell, about 0.54 GB on top of Node. The .NET worker starts Remotion as a process with an argument list, as it does FFmpeg.
- Creative templates are TypeScript, a second language beside the .NET code. Text and asset paths reach a template as input data, never as code.
- Remotion is not open source. It is free, including for commercial use, for individuals and companies of up to 3 people. From 4 people it needs a Company License, which for a product that renders automatically is $0.01 per render with a $100 a month minimum (terms read 2026-10-08). Remotion 5 makes telemetry mandatory under that licence. Members must never be able to supply their own Remotion code.
- The Remotion version is pinned with a lock file, and a version change is a deliberate act that includes re-reading the licence.
- Product Lock is unchanged: a template may only scale, move, rotate and fade the Product's image.
- The prototype rendered the whole video in one pass. Rendering Scene by Scene, so that only changed Scenes are rendered again, was not tried; ticket 09 has to prove it.
- If the licence terms become unacceptable, the fallback is FFmpeg filters alone, with plainer type motion and every creative template rewritten.
