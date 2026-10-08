# 26: Look: motion, layout and type

**What to build:** A second throwaway render of the founder's Product that no longer reads as a slideshow: a different layout in each Scene, large animated type that carries the Hook in the first two seconds, and motion with a clear rhythm, on the background style chosen in ticket 25. The founder watches it on a phone and gives the verdict again. This ticket also settles how the real renderer will produce motion graphics.

**Blocked by:** 25

**Status:** ready-for-agent

- [ ] Each of the four Scenes has its own layout; no two share the "Product in the middle, text underneath" arrangement
- [ ] The Hook fills a large part of the frame and is fully readable within the first two seconds
- [ ] Type animates in by word or by line, in time with the Scene changes
- [ ] The Product moves between Scenes (position, scale or angle of view within the allowed effects), never altered in appearance
- [ ] The same video is built two ways for comparison: with FFmpeg filters alone, and with Remotion, unless the first attempt in one of them already makes the other pointless, in which case that is stated with the reason
- [ ] For each way, these are recorded: render time for 20 seconds, what it adds to the worker image, how hard a new creative template would be to write, and its licence terms for commercial use checked against the vendor's current published terms
- [ ] The output is 1080x1920, H.264, AAC, about 20 seconds, with correct Vietnamese diacritics and BT.709 colour
- [ ] The founder has watched it on a phone and given the verdict: good enough to post, or not
- [ ] The choice of rendering approach is recorded as an ADR, and the spec and tickets 02, 08, 09 and 14 are updated to match it
- [ ] If the verdict is still "not good enough" after the week, work stops and the fallback (a paid image-to-video provider in place of the Affiliate Lab) is put to the founder before anything else is built
