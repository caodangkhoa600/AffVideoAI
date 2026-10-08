# 09: Render and preview

**What to build:** A member presses render on a Storyboard version. A background job renders a Product Lock MP4, the page shows the job's current stage, and when it finishes the member previews the Rendered Video in the browser. This is the tracer bullet through the queue, the worker, FFmpeg and storage.

**Blocked by:** 08

**Status:** ready-for-agent

- [ ] Rendering runs in the worker, not in the API request
- [ ] The job is stored in PostgreSQL and moves only through the defined states
- [ ] The web app polls and shows the current stage, with no percentage
- [ ] The Rendered Video is 1080x1920, H.264 video, AAC audio, MP4, within a small tolerance of the target duration, verified with ffprobe in a test
- [ ] With no audio supplied the video carries a silent audio track
- [ ] Scenes appear in Storyboard order with their on-screen text visible and Vietnamese diacritics correct
- [ ] The Product image is not altered beyond scale, position, rotation and fades
- [ ] FFmpeg is called with an argument list; no shell is used and user text is escaped before entering filter expressions
- [ ] The Rendered Video is stored through the storage abstraction and is ready for review
- [ ] Temporary files are gone after the job ends
- [ ] A failed render shows the member a reason
- [ ] A member of another Organization is refused the job and the Rendered Video by identifier
