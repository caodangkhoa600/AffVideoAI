# 01: Day-one render test

**What to build:** A throwaway script, outside the application, that renders one real Product the founder intends to promote into a 20-second 1080x1920 MP4 in the Product Showcase style: opening Hook text, product reveal with image motion, a Scene showing the Facts, and a closing call to action. The founder watches it and gives a verdict on whether a Product Lock video is good enough to post. This is a prototype: it is judged by eye and then discarded.

**Blocked by:** None (can start immediately). Needs from the founder: two or three photos of the Product, its name, and three or four Facts in Vietnamese.

**Status:** needs-info

- [x] The MP4 is produced with FFmpeg running in a container, not on the host
- [x] ffprobe reports 1080x1920, H.264 video, an AAC track, and about 20 seconds
- [x] Vietnamese text with diacritics renders correctly
- [ ] The Product's appearance is unchanged from the photos
- [ ] The founder has watched it and given a verdict: proceed, or spend up to a week on the look first
- [x] What was learned about the filter graph, fonts and motion is written into the ticket's comments for ticket 09
- [ ] If the verdict is "improve the look", new tickets are added for that work and the Affiliate Lab tickets are re-scoped

## Comments

- 2026-10-08: The prototype is in `prototypes/day-one-render/` and renders the fictional AirBeat X1 with a generated placeholder photo: 1080x1920, H.264, AAC, 20.00 s, diacritics correct. Still open: the founder's real photos and Facts, the "appearance unchanged" check on a real photo, and the verdict. Status is needs-info until the photos arrive.
- Learned, for ticket 09:
  - Compose one still per Scene at 2x resolution (blurred, darkened copy of the photo behind the untouched photo), then run `zoompan` on that single still. Zooming a 1x still jitters; zooming a looped video input is far slower.
  - Pass all on-screen text through `textfile=`, one `drawtext` per line. Set `expansion=none`, or a `%` or `\` in a Fact breaks the render. No user text then enters the filter string, and each line can be centred and timed on its own. `drawtext` does not wrap, so lines are broken beforehand.
  - Decode every photo once into a normalised PNG before anything else. This applies camera rotation and gives true dimensions.
  - Keep anything that darkens the frame for text legibility below the lowest point the photo reaches at full zoom, or the product itself is darkened.
  - Render each Scene to its own clip with the crossfade length added, then join with `xfade` at offsets equal to the running sum of Scene durations. The total then equals the sum of the durations exactly, and one Scene can be re-rendered alone.
  - Convert the photo's RGB to BT.709 explicitly before `zoompan` and tag the output with `setparams`; output options alone leave primaries and transfer untagged, and the default BT.601 conversion shifts the product's colours in players.
  - A silent `anullsrc` input encoded as AAC gives a valid audio track.
  - Debian trixie's FFmpeg 7.1 has everything needed. Be Vietnam Pro (SIL Open Font License) covers Vietnamese.
  - Text width is estimated from character count, which is crude. The real renderer should measure text.