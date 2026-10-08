# 01: Day-one render test

**What to build:** A throwaway script, outside the application, that renders one real Product the founder intends to promote into a 20-second 1080x1920 MP4 in the Product Showcase style: opening Hook text, product reveal with image motion, a Scene per Fact, and a closing call to action. The founder watches it and gives a verdict on whether a Product Lock video is good enough to post. This is a prototype: it is judged by eye and then discarded.

**Blocked by:** None (can start immediately). Needs from the founder: two or three photos of the Product, its name, and three or four Facts in Vietnamese.

**Status:** ready-for-agent

- [ ] The MP4 is produced with FFmpeg running in a container, not on the host
- [ ] ffprobe reports 1080x1920, H.264 video, an AAC track, and about 20 seconds
- [ ] Vietnamese text with diacritics renders correctly
- [ ] The Product's appearance is unchanged from the photos
- [ ] The founder has watched it and given a verdict: proceed, or spend up to a week on the look first
- [ ] What was learned about the filter graph, fonts and motion is written into the ticket's comments for ticket 09
- [ ] If the verdict is "improve the look", new tickets are added for that work and the Affiliate Lab tickets are re-scoped
