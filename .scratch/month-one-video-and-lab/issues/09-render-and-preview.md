# 09: Render and preview

**What to build:** A member presses render on a Storyboard version. A background job renders a Product Lock MP4, the page shows the job's current stage, and when it finishes the member previews the Rendered Video in the browser. This is the tracer bullet through the queue, the worker, Remotion, FFmpeg and storage. The look is the one the founder approved in ticket 26: Remotion draws the Scenes and FFmpeg joins them (ADR 0002).

**Blocked by:** 08

**Status:** ready-for-agent

- [ ] Rendering runs in the worker, not in the API request
- [ ] The job is stored in PostgreSQL and moves only through the defined states
- [ ] The web app polls and shows the current stage, with no percentage
- [ ] The Rendered Video is 1080x1920, H.264 video, AAC audio, MP4, tagged BT.709, within a small tolerance of the target duration, verified with ffprobe in a test
- [ ] With no audio supplied the video carries a silent audio track
- [ ] Scenes appear in Storyboard order with their on-screen text visible and Vietnamese diacritics correct
- [ ] Product Showcase has a different layout in each Scene, as a Remotion template, with type that animates in by word or by line
- [ ] The Hook is fully on screen within the first two seconds
- [ ] The Product is cut out of its photo by a model running locally in the worker and stands on a soft shadow on the studio backdrop; a cut-out that fails its checks falls back to the uncut photo on a card
- [ ] The Product image is not altered beyond scale, position, rotation and fades, and it carries on from where the previous Scene left it
- [ ] Each Scene is rendered on its own and the Scenes are joined by FFmpeg, with the total equal to the sum of the Scene durations
- [ ] Remotion and FFmpeg are each started with an argument list and no shell; user text reaches the template as input data and is escaped before entering any filter expression
- [ ] The Rendered Video is stored through the storage abstraction and is ready for review
- [ ] Temporary files are gone after the job ends
- [ ] A failed render shows the member a reason
- [ ] A member of another Organization is refused the job and the Rendered Video by identifier

## Comments

- 2026-10-08: Rewritten after ticket 26. What was learned about the look is in the comments of tickets 25 (cut-out, model, checks, shadow) and 26 (layouts, Remotion flags, joining Scenes). Two things there were not proven and fall to this ticket: rendering Scene by Scene with Remotion (the prototype rendered the whole video in one pass), and running the cut-out model from .NET (the prototype used Python). The cut-out makes this ticket larger than it was; it can be split off if it proves slow.
- 2026-10-09: Ticket 08 is done; its comments say what a Storyboard version holds and what it leaves to this ticket: the text limits the template must honour, the pacing of the Facts Scene that decides how many Facts are shown, and a Storyboard whose photo has since been removed.
