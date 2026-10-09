# 08: Storyboard generation

**What to build:** A member generates a Storyboard for a Variant with no paid AI. The mock planner builds Scenes from the Product's Confirmed Facts using the Product Showcase creative template, and the planning engine assigns each Scene a Technique allowed in Product Lock. The member sees the Scenes, their text, their Technique and which Facts each used.

**Blocked by:** 05, 06, 07

**Status:** done

- [x] Generating a Storyboard creates version 1 with ordered Scenes
- [x] The same inputs always produce the same Storyboard
- [x] Generated text uses only Confirmed Facts; Proposed and Withdrawn Facts never appear
- [x] Each Scene records the Facts it used, including a copy of their text
- [x] Scene durations sum exactly to the Project's target duration
- [x] Each Scene records which layout of the creative template it uses, so the renderer needs only the Storyboard version and the Product's assets
- [x] Generation fails with a clear reason when the Hook or a Fact is too long for its layout, including a Hook too long to be fully on screen within two seconds
- [x] In Product Lock only static image, image motion and text animation are assigned
- [x] Generation fails with a clear reason when the Product has no Confirmed Facts or no usable image
- [x] The Storyboard is labelled as produced by the mock planner, in the data and on screen
- [x] Free text from the Product is treated as data and cannot alter the planner's behaviour
- [x] Pure-function tests cover Technique selection, duration allocation and Storyboard validation
- [x] The language-model, video-generation, text-to-speech and image-processing provider interfaces exist; only the mock planner is implemented

## Comments

2026-10-09, implemented. `dotnet test` passed 253 tests against PostgreSQL and MinIO in containers, and the web app passed its typecheck and lint. The stack was rebuilt with `docker compose up --build --wait` and migrated, and as the seeded Owner, with a 600 by 800 photo added to the AirBeat X1: a 15-second Product Showcase Variant generated version 1 over the API, and its page in headless Chrome showed the four Scenes with their text, duration and Technique, the three seeded Facts under "Facts used", and the line saying the mock planner produced it; "Generate again" added a version and the earlier one could be chosen and read; a Variant with a 14-word Hook showed the refusal about two seconds under the button. After the review fixes below the two containers were rebuilt and a third version came out with two Facts and a moving closing Scene. The Project made for that run was deleted; the photo is still on the AirBeat X1.

What a Storyboard is:

- `POST /api/v1/projects/{projectId}/variants/{variantId}/storyboards` with no body makes the next version; `GET` on the same path lists versions newest first, and `/{version}` reads one. A refusal is a 409 whose `detail` is the reason.
- A Scene has a position, a layout (`Hook`, `Reveal`, `Facts`, `Closing`), a Technique, a duration in milliseconds, on-screen text as lines, narration text, the asset identifiers it shows and the Facts it used. What the lines are depends on the layout: the Hook; the Product's name; one line for each Fact; the name and then the call to action. Ticket 09's Remotion components take these as their input.
- Durations are whole tenths of a second (three frames at 30 a second), shared 3 : 5 : 7 : 5 as in the ticket 26 look.

Things that differ from what the ticket or spec might lead a reader to expect:

- The limits are mine, estimated from the ticket 26 prototype and not measured with the typeface: a Hook holds 13 words (the prototype's timing: the thirteenth is in place at 1.92 seconds), 60 characters, and words of at most 12; a Fact 120 characters; the Product's name 60. Ticket 09's template has to draw anything inside them, or the numbers change with the template's version.
- A Product name too long for its layout also refuses generation. The ticket names only the Hook and Facts.
- The number of Facts depends on the duration. Up to three are shown, oldest Confirmed first, and fewer when three could not each arrive and be held half a second before leaving: three Facts of twelve words fit 20 seconds and only two fit 15. The member is not told that a Fact was left out, and cannot choose which Facts a Storyboard uses. Say so if either is wanted.
- Only Facts in the Project's language are used, so a Product with Confirmed Facts only in English is refused as having none.
- "Usable image" means a photo, not the logo, of at least 400 pixels on each side. The number is mine.
- Every Scene shows one photo: the newest usable one in the Facts Scene, the oldest elsewhere.
- Every Storyboard is Product Lock. There is no way to ask for Hybrid, which would choose the same Techniques today.
- Product Showcase gets image motion in three Scenes and text animation in the Facts Scene. Static image is only chosen for a Scene shorter than 1.5 seconds, which no Project's duration produces.
- Luxury Cinematic and Problem–Solution are refused with a reason until ticket 14.
- Generating again with nothing changed makes another version with the same Scenes.
- Deleting a Project deletes its Variants' Storyboards with them. Ticket 07 suggested refusing the deletion instead. A Storyboard can be generated again exactly, so cascading loses nothing yet; that stops being true with Rendered Videos (09), flags (13) and Campaigns (18), and those tickets should refuse. This is the founder's call to confirm.
- The narration text is written and stored but nothing speaks it.
- The description, audience, objective, tags and price are not given to the planner at all.

Left for later tickets:

- 09: a photo can be removed after a Storyboard used it, and the version then names an asset that is gone. Rendering must fail with a reason, or removal must be refused. A Fact withdrawn or a photo removed in the instant between planning and saving is stored the same way.
- 09: the text limits are checked on what goes into the planner. A real language model's output would need them checked on what comes out.
- 13: `StoryboardSceneFacts` has an index on the Fact, for finding every version that used one.
- The mock label on the page and the version picker have no automated test (ticket 17).
- Left from the code review as not worth the change now: the planner's inputs (template, duration, Render Mode) travel together through three functions, `GenerateAsync` is long, and a Scene's text and asset lists are arrays as a Product's tags are.
