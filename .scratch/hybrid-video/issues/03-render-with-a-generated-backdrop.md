# 03: A render with a generated backdrop

**What to build:** A render of the Storyboard version draws the approved Still behind the Product's real Cut-out in that Scene, with the creative template moving the Product and slowly moving the backdrop. No Clip is made for a backdrop. This is the first Hybrid video that differs from a Product Lock one. Paid generation as a step before rendering is ADR 0003.

**Blocked by:** 02

**Status:** done

- [x] The Storyboard version refers to the Still its Scene uses
- [x] The rendered Scene shows the Still behind the Cut-out, with the Product's shadow on it; the Product's pixels are the photo's
- [x] The backdrop moves slowly, by the template's doing, for the whole Scene whatever its length
- [x] A photo with no usable Cut-out is handled as today, on a Card over the Still
- [x] The MP4 is still 1080x1920 H.264 with AAC at the target duration
- [x] The Render job calls no provider and writes no generation cost
- [x] A render of a Storyboard version whose generated Scene has no approved Still fails validation with a reason
- [x] The per-Scene cache key includes the Still, so only that Scene is rendered again when the Still changes

## Comments

2026-10-12. Built test-first at the two seams of the spec, as the founder confirmed them: `StoryboardRules.RenderProblems` as a pure function, and the HTTP API with the worker, real PostgreSQL, real object storage and the fake image provider. No browser test was added.

Decided by the founder before building:

- Approving a Still makes the next Storyboard version, and that version refers to the Still. A version is not changed, and a render does not pick a Still for itself.

Decided while building, and not discussed beforehand:

- A Still belongs to a Project and its approval names no Variant, so approval makes the next version of every Variant of the Project whose newest version describes that picture. A version made afterwards (planned again, edited, a Scene regenerated, another Variant) refers to the approved Still too, for as long as its Scene describes the same picture. Describing another picture, or asking for another Still, leaves the next version referring to none.
- A version keeps the Still it was made with: asking for another leaves it rendering as before, and only a version made from then on needs the new Still. (The founder left this to the builder after the code review, which found that failing such a version left a Variant with nothing to render and no way back.)
- The backdrop is pushed in on by 8 percent over the Scene, whatever its length, under a light wash of the template's own ground so that the type reads on any picture (white at 18 percent in Product Showcase, near-black at 40 percent in Luxury Cinematic). These are to be judged by eye on a real Still, in ticket 09 at the latest.
- In its last 0.3 seconds the Still fades to the template's own ground. The Scene after is drawn on its own and is handed nothing of the Still, which is what keeps it from being drawn again when the Still changes; the fade is what stops the backdrop cutting at the join.
- Generated footage in a Hybrid Storyboard fails validation with "cannot be rendered yet". That is ticket 04.
- The cache key had no pure seam worth a test: `SceneClips.KeyOf` already hashes whatever images it is given. That the Still is among them is shown through the API, by which Scenes a render says it drew.
- The fake image provider's stripes are darker than they were, so that the Still can be told from the studio backdrop in a frame.
- The web page moves to the new version when a Still is approved. It was type-checked and linted, and not exercised in a browser.

2026-10-12, after the code review of this ticket against e5c86d6.

- An approval and the making of a Storyboard version each hold the Project's row while they decide, as a confirmation does, so a version made at the moment of an approval cannot miss the Still.
- What the page and the README said, that rendering costs nothing, was wrong: each Attempt has its Production cost. They now say a render generates nothing.
- Luxury Cinematic is rendered with a Still in the tests, and the Still giving way at the end of its Scene is asserted.

Left as the review found them: the Product's shadow on the Still has no assertion of its own (it is part of the Product's layer, which is drawn over the Still); only one Scene length is rendered; and the wash over the Still, which the ticket did not ask for.
