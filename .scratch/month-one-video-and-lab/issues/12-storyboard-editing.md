# 12: Storyboard editing

**What to build:** A member edits a Storyboard in a basic Scene editor: change on-screen text and narration text, swap the source image, reorder Scenes, adjust a Scene's duration within limits, and regenerate a single Scene. Every change makes a new Storyboard version, and re-rendering only redoes the Scenes that changed.

**Blocked by:** 09

**Status:** done

- [x] Any edit creates the next Storyboard version; earlier versions stay viewable and unchanged
- [x] A Scene whose text was changed by a person is marked Manually Edited, and the mark is visible
- [x] Manually Edited text is not checked against Facts
- [x] A change that breaks the total duration or references a missing asset is rejected with a reason
- [x] Regenerating one Scene leaves the other Scenes untouched
- [x] Rendering a new version reuses the intermediate clips of unchanged Scenes, shown by a test in which only the changed Scene is rendered again
- [x] Each Rendered Video shows which Storyboard version it came from

## Comments

- 2026-10-09, implemented. `dotnet test` passed 502 tests, 46 of them new: the API in-process against PostgreSQL and MinIO in containers, with the worker rendering in its own. The web app passed its typecheck and lint. The stack was rebuilt with `docker compose up --build --wait` and migrated, and in headless Chrome as the seeded Owner, on a 20-second Variant of the AirBeat X1: the Scene editor opened on version 1; with half a second missing the page said so and saving was refused in the API's words; with the reveal renamed, moved after the Facts and a second moved between them, saving made version 2 with that Scene marked Manually Edited; version 1 was still there and as it had been; "Regenerate this Scene" on the marked Scene made version 3 with the planner's text and no mark; rendering version 3 said all four Scenes were drawn, and rendering version 2 after it said only Scene 3 was. The Project made for the check was deleted. One line of the editor changed after that check (a refusal now clears when the draft changes) and was not seen in the browser again.

  The tests were written after the code they cover for the HTTP seam, not before: the rules were written test-first as pure functions, the endpoints and the clip cache were not. Each new test was run and passed; none of the HTTP tests was seen to fail first.

  How each box was checked:

  - By a test at the HTTP seam: an edit making the next version with the earlier ones unchanged, from the newest version and from an older one; the Manually Edited mark, its staying through later edits, and text sent back unchanged not counting; an edit refused around a Withdrawn Fact and accepted once the Scene's text is the member's own; the total duration, a photo the Product does not have, another Product's, the logo and a small one, each refused with the reason; reorder, another photo and other durations in one edit; regenerating one Scene with the others identical; another Organization and someone not signed in refused.
  - By a test that renders: version 1 draws Scenes 1 to 4, version 2 with the reveal's text changed draws Scene 2 alone, version 1 again draws none; the frames of the reused Scenes are identical between the videos and those of the edited Scene are not; each Rendered Video names its Storyboard version.
  - By tests on pure functions: what an edit makes of Scenes, and what each layout holds.
  - In the browser, by hand, once: the editor, the mark on the page, the version on the Rendered Video. No automated test covers the page (ticket 17).

  What an edit is:

  - `POST …/storyboards/{version}/edits` names every Scene of that version once, in the order the next version plays them, each with what to change: `onScreenText`, `narrationText`, `assetId`, `durationMs`. `POST …/storyboards/{version}/scenes/{position}/regenerate` plans one Scene again. Both answer 201 with the new version, or 409 with every reason and nothing made.
  - The version made is always the Variant's next, whichever version was edited.

  Things that differ from what the ticket or spec might lead a reader to expect, each of them mine to have decided and the founder's to overrule:

  - A Scene whose text changes loses its list of Facts entirely, even when a line is still a Fact's text word for word. That is the glossary's "no longer traceable to Facts", taken literally. The consequence: a member can add a full stop to a Scene that shows a Withdrawn Fact, and the new version shows the claim with nothing linking it to the Fact.
  - A new version is refused while any Scene still lists a Fact that is no longer Confirmed, even one the edit did not touch. The way out is to regenerate that Scene or rewrite it.
  - The Hook's Scene has to stay first, so that the Hook is on screen within two seconds. The other three can be put in any order.
  - "Within limits" for a duration is a minimum for each layout, 2 seconds and 2.5 for the closing, with the maximum left to the total. The numbers are mine, read off the template's timing.
  - A layout sets a fixed number of lines: one, two for the closing, one to three for the Facts. A call to action holds 30 characters and narration 500. These numbers are mine too.
  - A Facts Scene made shorter is refused when its lines could no longer be read in the time, by the same pacing that decides how many Facts a Storyboard shows.
  - An edit that changes nothing is refused. Regenerating a Scene that comes out the same still makes a version, as generating again does.
  - A regenerated Scene keeps its duration and takes the photo its layout would be planned with now, not the one it had.
  - Regenerating the Hook, the reveal or the closing needs no Confirmed Fact. Regenerating the Facts needs one.
  - The same layout checks now run on a generated Storyboard as well, which ticket 09 asked for. Nothing that was planned before is refused by them.
  - A Rendered Video says which Scenes were drawn for it (`drawnScenePositions`), and the page shows it. The ticket did not ask for it; it is what the reuse test reads, and without it reuse is invisible.
  - Moving a Scene or changing its photo also redraws the Scene after it, which starts from where that one leaves the Product. Changing text or a duration redraws that Scene alone.
  - Clips are kept only once the video they were drawn for has passed its checks, so a job that fails leaves nothing to reuse and its retry draws everything again.

  Left for later tickets:

  - 13: a Manually Edited Scene lists no Facts, so withdrawing a Fact finds nothing to flag in it. If such Scenes should still be flagged, the Facts a Scene was written from have to be kept beside the mark.
  - 14: `StoryboardRules.LayoutProblems` and the editor's line labels know the four layouts of Product Showcase. The line counts are in the domain and again in `scene-editor.tsx`.
  - 16: `drawnScenePositions` is the count of Scenes a render actually drew, for the cost record.
  - Nothing deletes a kept Scene clip, not with the photo, the Project or the Rendered Video. They are at `organizations/{id}/scene-clips/`.
  - A clip's key covers the bundled templates, the Scene's data and its images. How Remotion is started (`--crf` and the rest) is covered only by `ClipVersion` in `Remotion.cs`, which has to be raised by hand when those change.
  - A version keeps the planner label of the version it was edited from. With one planner that is always right; with a second, a Scene regenerated by it would sit in a version labelled with the first.
  - The closing layout bobs the Product by up to 10 pixels, which its resting pose leaves out, so a closing moved before another Scene of the same photo can jump by that much at the cut. It could not be put there before this ticket.
  - From the code review, not worth the change now: the edit endpoint repeats the answer the other two share; `ScopedStoryboards` now plans, edits, regenerates and saves, and `CheckedAndSavedAsync` takes ten arguments; the Variant page sends three requests of the same shape.
