# 02: A Still for a generated backdrop

**What to build:** In a Hybrid Variant, the Hook Scene can have a generated backdrop. The member sees the Scene's description, which comes from the creative template with the Product's category and audience slotted in, and may edit it. They see an estimate, confirm, and get a Still, made in the background by a fake image provider. They approve it or ask for another. Nothing is rendered differently yet.

**Blocked by:** 01

**Status:** done

- [x] The Technique list has generated backdrop and generated footage and no longer has image-to-video
- [x] In Hybrid the planning engine gives the Hook Scene generated backdrop when the template allows it and an image provider exists; in Product Lock it never does
- [x] With no provider configured, Hybrid plans exactly what it does today and generation says it is unavailable
- [x] The description comes from the creative template; a member's edit replaces it for that Scene
- [x] Nothing is generated until the member has seen the estimate and confirmed
- [x] A Still is made on the existing queue, with Lease, Attempt and idempotency as a Render job has, and its state is shown
- [x] A member approves a Still or asks for another; another is a new paid generation
- [x] A Project has a ceiling on generation, $5 by default; a generation that would pass it is refused with the reason
- [x] A technical failure that was not billed is retried without the member; nothing else is
- [x] A refusal by the provider is shown as a failure with a reason
- [x] Each Still, rejected ones included, has a Production cost record
- [x] What the fake provider made is labelled as fake in the data and on screen
- [x] A member of another Organization is refused a Still by identifier, including its file

## Comments

2026-10-12. Built test-first at the two seams of the spec: pure functions in the domain, and the HTTP API with the worker, real PostgreSQL, real object storage and the fake image provider. No browser test was added.

Decided while building, and not discussed beforehand:

- "The existing queue" is read as the same behaviour, not the same table: Stills wait in the `Stills` table and are served by the worker under the render queue's lease, attempts and waits. A render job is bound to a Storyboard version and has stages a Still does not go through.
- A Variant has the Render Mode, chosen when it is added. The create video wizard still makes Product Lock Variants only.
- The audience placed in a description is the Project's, from its brief, not the Product's target audience.
- Only Product Showcase and Luxury Cinematic allow a generated backdrop on the Hook. Problem-Solution's Hook has no Product in it.
- A Scene has the Project's Still whose description is the same text. A member who edits the description needs a new Still.
- The confirmation carries the total the member saw, and is refused when that is no longer the estimate.
- A Still's cost record is in a table of its own (`GenerationCostRecords`). It is not yet in a Product's Production cost total; that is ticket 07.
- A Hybrid Storyboard still cannot be rendered. That is ticket 03.

2026-10-12, after the code review of this ticket against 85c12e0. The founder decided the first two; the rest are fixes.

- A Still whose worker stopped is failed, counted as charged for, and not made again without the member. It no longer goes back to the queue.
- The audience in a description stays the Project's.
- A picture the provider made that the worker could not store fails the Still as charged for. Before, it was tried again as if nothing had been charged.
- Asking for another carries the amount the member saw and is refused when that is not what another costs now.
- A confirmation and a request for another hold the Project's row while they decide, so requests arriving together cannot pay for one Scene twice or pass the ceiling between them.

Left as the review found them: the words "spent" and "take" in the API and on the page, which the glossary says to avoid and the spec uses; the cost record's own table and name; and how much `StillQueue` and `StillWorker` repeat of the render queue and worker.
