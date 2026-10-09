# 09: Render and preview

**What to build:** A member presses render on a Storyboard version. A background job renders a Product Lock MP4, the page shows the job's current stage, and when it finishes the member previews the Rendered Video in the browser. This is the tracer bullet through the queue, the worker, Remotion, FFmpeg and storage. The look is the one the founder approved in ticket 26: Remotion draws the Scenes and FFmpeg joins them (ADR 0002).

**Blocked by:** 08

**Status:** done

- [x] Rendering runs in the worker, not in the API request
- [x] The job is stored in PostgreSQL and moves only through the defined states
- [x] The web app polls and shows the current stage, with no percentage
- [x] The Rendered Video is 1080x1920, H.264 video, AAC audio, MP4, tagged BT.709, within a small tolerance of the target duration, verified with ffprobe in a test
- [x] With no audio supplied the video carries a silent audio track
- [x] Scenes appear in Storyboard order with their on-screen text visible and Vietnamese diacritics correct
- [x] Product Showcase has a different layout in each Scene, as a Remotion template, with type that animates in by word or by line
- [x] The Hook is fully on screen within the first two seconds
- [x] The Product is cut out of its photo by a model running locally in the worker and stands on a soft shadow on the studio backdrop; a cut-out that fails its checks falls back to the uncut photo on a card
- [x] The Product image is not altered beyond scale, position, rotation and fades, and it carries on from where the previous Scene left it
- [x] Each Scene is rendered on its own and the Scenes are joined by FFmpeg, with the total equal to the sum of the Scene durations
- [x] Remotion and FFmpeg are each started with an argument list and no shell; user text reaches the template as input data and is escaped before entering any filter expression
- [x] The Rendered Video is stored through the storage abstraction and is ready for review
- [x] Temporary files are gone after the job ends
- [x] A failed render shows the member a reason
- [x] A member of another Organization is refused the job and the Rendered Video by identifier

## Comments

- 2026-10-08: Rewritten after ticket 26. What was learned about the look is in the comments of tickets 25 (cut-out, model, checks, shadow) and 26 (layouts, Remotion flags, joining Scenes). Two things there were not proven and fall to this ticket: rendering Scene by Scene with Remotion (the prototype rendered the whole video in one pass), and running the cut-out model from .NET (the prototype used Python). The cut-out makes this ticket larger than it was; it can be split off if it proves slow.
- 2026-10-09: Ticket 08 is done; its comments say what a Storyboard version holds and what it leaves to this ticket: the text limits the template must honour, the pacing of the Facts Scene that decides how many Facts are shown, and a Storyboard whose photo has since been removed.
- 2026-10-09, implemented. `dotnet test` passed 412 tests: the API in-process against PostgreSQL and MinIO in containers, and the worker in a container built from its own Dockerfile, rendering the jobs the tests queue over HTTP. The MP4 the API serves is inspected with ffprobe in that container. The web app passed its typecheck and lint, and the Remotion project its typecheck. The stack was rebuilt with `docker compose up --build --wait` and migrated, and in headless Chrome as the seeded Owner: a 20-second Variant of the AirBeat X1 was rendered by pressing "Render video" on its page, the stage went Queued, Cutting the Product out of its photos, Rendering, Ready for review, the video played (20 s, 1080 by 1920, seekable), and reloading the page found it again. I looked at frames of what the tests rendered, at rest and in the moves between Scenes; nobody has watched a video play.

  How each box was checked:

  - By a test at the HTTP seam: the worker renders, not the API; the job's states; 1080x1920, H.264, AAC, MP4, BT.709, 450 frames for 15 seconds and the duration; the silent track; a different picture in each Scene; the Hook in place at two seconds, with a Hook of 13 words and 60 characters; the cut-out and the card; storage and ready for review; temporary files; the failure reason; another Organization refused.
  - By a test on pixels, with no API: the checks a cut-out has to pass, and that the layer holds the Product's solid pixels unchanged.
  - By eye only, on frames: Scenes in Storyboard order with their text, Vietnamese diacritics, type arriving by word, the Product carrying on between Scenes. No test reads text off a frame.
  - By reading the code only: that Remotion and FFmpeg are started with an argument list and no shell. The test renders a Hook and a Fact full of quotes, `$( )`, backticks and markup, and they come out set in type as written.
  - The web page (polling, stage, failure, preview) has no automated test; that is ticket 17's browser test.

  Things that differ from what the ticket or spec might lead a reader to expect:

  - A Project that has a Rendered Video cannot be deleted: 409, and the page says why. Ticket 08 left this as the founder's call and suggested refusing; I refused. Nothing deletes a Rendered Video until ticket 11, so such a Project stays. The check on the Compose stack left two of them on the AirBeat X1 ("Ticket 09 check").
  - The cut-out is kept in object storage beside the photo (`….video-layer.png` and `.json`) and removed with it, so a photo is cut once. This is not the Scene-clip cache of ticket 12.
  - A Rendered Video says which photos it shows on a card (`uncutAssetIds`), and the page tells the member. The ticket did not ask for it; without it the fallback is invisible.
  - Scene clips are drawn two at a time (`Rendering:ScenesAtOnce`).
  - The stages a member sees include one the spec's list does not: "Cutting the Product out of its photos" (generating assets), where most of a first render's time goes.
  - Rendering twice makes two jobs and two Rendered Videos. One job for two clicks is ticket 10.
  - A job the worker was rendering when it stopped stays in that state for good. Ticket 10's lease is what picks it up.
  - Every failure but a refused Storyboard reads "something went wrong while …" with the stage; the detail is only in the worker's log. Structured failure detail is ticket 10.
  - If the model itself cannot run (file missing, out of memory) the job fails. Only a cut-out that fails its checks falls back to a card.
  - The preview endpoint reads the whole MP4 into memory to answer a player's requests for parts of it.

  Measured on this machine, for 15 seconds of video: about 60 seconds for a first render with one photo to cut, of which the model is 15 to 45; about 35 seconds once the photos are cut. The worker image is 3.05 GB on disk, up from 2.64: the model is 224 MB, ONNX Runtime the rest.

  What ticket 26 left unproven:

  - Rendering Scene by Scene works. Each Scene is its own `remotion render` from a bundle built with the image, and the clips join with `-c copy` to the exact frame count. The Product's pose at the start of a Scene comes from the layout of the Scene before, which the Scene is told; ticket 12's cache key is the Scene's props file plus the template's version.
  - The cut-out model runs from .NET through ONNX Runtime, with rembg's preprocessing rewritten. It cut the drawn bottle on a plain backdrop cleanly. It was not compared pixel for pixel with rembg, and was not run on the founder's photos, which never reach the model: both came already cut out.

  Left for later tickets:

  - 10: `IRenderQueue.ClaimAsync` takes a job with `FOR UPDATE SKIP LOCKED` and no lease.
  - 12: text limits are still only checked going into the planner. The template sets text that is too long at its smallest size and lets it run off; edited text needs the limits checked before it is saved.
  - 14: `remotion/src/Root.tsx` picks a layout by template and layout name; `StoryboardRules.RenderProblems` refuses every template but Product Showcase.
  - A photo removed in the seconds its cut-out is being made leaves the cut-out's two files behind in storage.
  - One render in one full test run ended Failed, with the reason not captured. It did not happen again in the three full runs after, and the tests now print the worker's log when a render fails. The cause is not known.
  - From the code review, not worth the change now: the stage wording exists in the worker and in the web app; 1080, 1920 and 30 are written in the domain and in `remotion/src/scene.ts`; `RenderJobState.Created` and `GeneratingVideo` are never entered.
