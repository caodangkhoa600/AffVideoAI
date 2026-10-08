# 26: Look: motion, layout and type

**What to build:** A second throwaway render of the founder's Product that no longer reads as a slideshow: a different layout in each Scene, large animated type that carries the Hook in the first two seconds, and motion with a clear rhythm, on the background style chosen in ticket 25. The founder watches it on a phone and gives the verdict again. This ticket also settles how the real renderer will produce motion graphics.

**Blocked by:** 25

**Status:** ready-for-human

- [x] Each of the four Scenes has its own layout; no two share the "Product in the middle, text underneath" arrangement
- [x] The Hook fills a large part of the frame and is fully readable within the first two seconds
- [x] Type animates in by word or by line, in time with the Scene changes
- [x] The Product moves between Scenes (position, scale or angle of view within the allowed effects), never altered in appearance
- [x] The same video is built two ways for comparison: with FFmpeg filters alone, and with Remotion, unless the first attempt in one of them already makes the other pointless, in which case that is stated with the reason
- [x] For each way, these are recorded: render time for 20 seconds, what it adds to the worker image, how hard a new creative template would be to write, and its licence terms for commercial use checked against the vendor's current published terms
- [x] The output is 1080x1920, H.264, AAC, about 20 seconds, with correct Vietnamese diacritics and BT.709 colour
- [ ] The founder has watched it on a phone and given the verdict: good enough to post, or not
- [ ] The choice of rendering approach is recorded as an ADR, and the spec and tickets 02, 08, 09 and 14 are updated to match it
- [ ] If the verdict is still "not good enough" after the week, work stops and the fallback (a paid image-to-video provider in place of the Affiliate Lab) is put to the founder before anything else is built

## Comments

- 2026-10-08: `prototypes/day-one-render/motion.py` renders the founder's Product twice from the same assets: `output/motion/iphone-17-promax-ffmpeg.mp4` (FFmpeg filters alone) and `output/motion/iphone-17-promax-remotion.mp4` (Remotion, template in `remotion/src`). Both pass ffprobe: 1080x1920, H.264, AAC, 20 s, BT.709. Status is ready-for-human: the founder watches both on a phone, gives the verdict, and confirms the rendering approach. The ADR and the changes to the spec and tickets 02, 08, 09 and 14 wait for that.
- The four layouts, the same in both builds:
  1. Hook: two blocks of capitals about 180 px high, one above and one below the Product, which sits tilted, right of centre. The words arrive one by one and are all in place under 0.9 s in.
  2. Reveal: the Product large and upright, its name top left, and the name again very large and pale, running sideways behind it.
  3. Facts: one Fact at a time in white on a panel in the Product's own colour, with a large counter top left. The Product, from its second photo, stands on the panel's edge and nudges at each new Fact.
  4. Closing: the Product on a disc of its own pale colour, its name, and the call to action in a pill. This is the one Scene with the Product in the middle and text underneath.

  Between Scenes the Product carries on from where it was: it travels and straightens from the Hook into the reveal, leaves sideways as the second photo comes in, and returns for the closing. It is only scaled, moved and rotated. The shadow is baked into the Product's layer, and the script checks that the Product's opaque pixels are unchanged by it.
- What differs between the two builds, by eye: in the Remotion build each word rises from behind a mask and the pill pops in by scaling. FFmpeg's `drawtext` cannot mask or scale type, so there the words slide up while fading and the pill slides in. A Fact arrives word by word in Remotion and line by line in FFmpeg. Smaller things were not matched exactly: the easing curve of the Product's move into the reveal, and the fade of the pale running name. Type is placed by baseline in FFmpeg and by CSS box in Remotion, so lines can sit a few pixels apart. At rest the frames are near identical.
- Measured on this machine (14 cores), for the 20-second video of the founder's Product:

  | | FFmpeg filters alone | Remotion 4.0.534 |
  | --- | --- | --- |
  | Render time | 48 s, 50 s and 69 s over three runs. The four Scenes render side by side, each composed at 2160x3840 and scaled down. | 28 s, 30 s and 31 s, including the type-check and bundling the template. Frames are captured as PNG. |
  | Added to the image | Nothing beyond FFmpeg and the typeface. It does need something to measure text and to draw rounded shapes; here that was Pillow, in the worker it would be a .NET library. | 0.54 GB on disk: 273 MB of packages, 236 MB of Chrome Headless Shell, 8 MB of system libraries. This image already had Node; the .NET worker image would need Node as well. |
  | Writing a new creative template | Hard. A template is a list of layers whose motion is written as FFmpeg expression strings. Type cannot be masked, scaled, or moved letter by letter. `overlay` and `drawtext` place things on whole pixels, so slow moves step unless everything is composed at 2x, which is where the render time goes. There are no rounded shapes; each is a PNG made beforehand. Text must be measured outside FFmpeg to lay out words. A mistake shows only as a failed or wrong render. | Easy by comparison. A template is a React component laid out with CSS, with easing built in, sub-pixel motion, masks, and anything else a browser draws. It is type-checked, and Remotion Studio previews it frame by frame. The template is TypeScript, so it sits beside the .NET code and the worker starts it as a process, as it does FFmpeg. |
  | Licence for commercial use | LGPL 2.1 or later; Debian's build includes x264, which makes it GPL. No fee. The obligations fall on distributing the binary, not on running it on our own machine. Source: ffmpeg.org/legal.html, read 2026-10-08. | Not open source. Free, including commercial use, for individuals and for companies of up to 3 people. From 4 people a Company License is needed; for a product that renders automatically that is "Remotion for Automators": $0.01 per render, $100 a month minimum. Customers of such a product need no licence of their own, but may not upload their own Remotion code. Remotion 5 will make telemetry mandatory for Automators. Sources: remotion.pro/license, remotion.dev/docs/license/faq and LICENSE.md in the repository, read 2026-10-08. |

  Both end in H.264 through x264, so the note on ffmpeg.org about H.264 patents applies either way.
- Recommendation, for the founder to confirm: Remotion draws the Scenes, and FFmpeg stays for joining, audio and probing. Remotion rendered faster here, its type moves better, and Luxury Cinematic and Problem–Solution (ticket 14) would each be a component, not a new set of filter expressions. The cost is half a gigabyte in the worker image, a second language in the worker, and $100 a month or more once the company is 4 people.
- Learned, for ticket 09, whichever way is chosen:
  - Each Scene starts with the Product where the last Scene left it. The Product then appears to travel through the video even though each Scene is its own clip. A clip's cache key has to include the pose it starts from.
  - Bake the shadow into the Product's layer once, with the asset. Both builds then move one image per Product.
  - Keep type clear of the top 130 px and the bottom 380 px, where the platforms draw their own interface. These layouts aim for that and miss by about 30 px: the Hook's lower block and the pill both reach about y 1570.
  - In FFmpeg: `drawtext` with `y_align=baseline` keeps words on one baseline whatever their diacritics. `scale` and `rotate` with `eval=frame` animate a layer. Scene clips encoded with the same settings join with the concat demuxer and `-c copy`, with no second encode.
  - In Remotion: pass `--color-space bt709` and `--enforce-audio-track`; both were passed here and the file came out tagged BT.709 with an AAC track. Load the typeface from a file with `delayRender` before measuring any text. The render ran with the network off.
- Not checked: nobody has watched either video play. The frames were looked at, in the middle of the moves as well as at rest; smoothness and rhythm are for the founder's eye. Only the founder's Product and the placeholder were rendered, each with short text; a long Hook or long Facts were not tried. Words arrive 0.12 s apart, so a Hook of about 13 words or more would not be fully on screen by two seconds, and nothing checks that. Text too long for its box stops the run with a message. A Product wider than 0.86 of its height is shown smaller so that it stays in the frame; that path ran only on the placeholder's card, if at all.
