# 04: Generated footage

**What to build:** A second kind of generated Scene: a whole Scene filled with a Clip in which the Product does not appear, such as a setting or a person. Its approved Still becomes a Clip, made by a fake video provider under the same estimate, confirmation and ceiling, and the member watches the Clip before rendering. A Hybrid video has at most two generated Scenes.

**Blocked by:** 03

**Status:** done

- [x] Each creative template states which of its Scenes may be generated and with which Technique
- [x] The planning engine assigns a generated Technique to at most two Scenes, the Hook Scene first
- [x] A Clip is made only from an approved Still, and is asked for at its Scene's length
- [x] The member can watch a Clip before rendering
- [x] Audio that a provider attaches to a Clip is removed
- [x] Each Clip has a Production cost record
- [x] A member of another Organization is refused a Clip by identifier, including its file
- [x] A render of a Storyboard version whose generated-footage Scene has no Clip fails validation with a reason
- [x] A generated-footage Scene renders the Clip full frame with the Scene's type over it and no Product
- [x] The template's descriptions ask for invented adults only and for no Product
- [x] A generated Scene whose description depicts what the Product does must cite a Confirmed Fact; validation rejects it otherwise
- [x] Withdrawing that Fact marks the Storyboard versions and Rendered Videos that used the Clip as Flagged for Review
- [x] The three creative templates each have descriptions for their generated Scenes

## Comments

2026-10-12. Built test-first at the two seams of the spec: pure functions in the domain, and the HTTP API with the worker, real PostgreSQL, real object storage and the fake image and video providers. No browser test was added. The ticket was `needs-triage` when it was taken up: it was the next one unblocked, and nobody triaged it first.

Decided while building, and not discussed beforehand. The first four are the founder's to confirm or change.

- Which Scenes may be generated. Product Showcase and Luxury Cinematic: a backdrop for the Hook, footage for the Facts. Problem-Solution: footage for the Hook, which has no Product in it, and for the Facts. A Hybrid video with both providers therefore has its Facts set over footage, with no Product in that Scene.
- "Depicts what the Product does" is the creative template's to say, Scene by Scene (`ShowsWhatTheProductDoes`): the Facts Scenes, whose footage is seen under the Facts. Such a generated Scene has to cite a Fact. Nothing reads a description for what it says, so a description a member writes for a Hook is not checked: like text a member writes, it is the Organization's own. One consequence: the text of a Facts Scene with footage cannot be replaced by a person's own words, which cite no Fact (409).
- The descriptions ask for invented adults only and no Product in the template's own words. A member who edits a description replaces that sentence too. Nothing appends it to what the provider is told.
- A member approves a Clip, as they approve a Still, and that approval makes the Storyboard version that refers to it. The ticket says only that the member watches it; ADR 0003 says "an approved Clip".
- One confirmation covers the Stills and the Clips. A Clip is a row from the confirmation on, `WaitingForStill` until a member approves its Still, which queues it. It counts against the ceiling while a Still is on its way for it.
- A Still and a Clip record the Facts their Scene cited. A Scene has the Project's Still or Clip of its picture only when it cites the same Facts, and a Clip only when it is at least as long as the Scene. Withdrawing a Fact then flags every version and Rendered Video that used the Clip through the flags that already exist; nothing new is written when a Fact is withdrawn.
- Another Still is refused once a Clip was made from the first: it would be paid for and shown nowhere. Asking for a Clip again is ticket 05.
- A Scene of generated footage keeps the photo it was planned with and does not draw it, as Problem-Solution's Hook always has. The Scene is cut to and from: the Scene after it is handed nothing of it and starts as a first Scene does. The wash over the footage, so that type reads on it, differs by template and is to be judged by eye on real footage, in ticket 09 at the latest.
- The fake video provider is in the worker and makes its file with FFmpeg, a test pattern with a tone, as long as it was asked for. The spec says a fake returns fixed files; a fixed file could not show that a Clip is asked for at its Scene's length, or that sound is removed.
- A real provider makes whole seconds, and a Scene lasts tenths. The interface asks for "at least this long", and a render plays the Clip from its start for as long as the Scene. Rounding up is ticket 09's.
- A Clip's cost record is in `GenerationCostRecords`, beside a Still's. It is not yet in a Product's Production cost total; that is ticket 07.
- With an image provider and no video provider, Hybrid plans what it did before this ticket: a backdrop only.
- The page shows each Clip with its state and, once made, the video to play, and was type-checked and linted. It was not exercised in a browser.

2026-10-12, after the code review of this ticket against 44bf30d.

- From the moment the provider has made a Clip, a failure in the worker fails it as charged for. Before, a timeout while storing it could send it back to the queue as if nothing had been charged.
- A Clip that waits is counted against the ceiling only while a Still is on its way for it. When that Still was not made, the Clip goes on waiting uncounted, and is counted and checked against the ceiling again when its Still is confirmed again. Of two Clips of one picture that wait, only the longer is made.
- A Clip starts from a Still only while it waits for one.
- A render fetches only the Stills it draws: a Scene of footage no longer needs the file of the Still its Clip was made from.
- Problem-Solution's Hook description no longer says what the Product is bought to end.
- Luxury Cinematic is rendered with footage in the tests, a Clip retried after an uncharged failure is tested, and "take" is out of the README and the page.

Left as the review found them: a Clip whose Still waits for review after its Scene's description was changed stays counted until that Still is approved or another is asked for; a Clip already queued is still made after its Fact is withdrawn (the work that uses it is flagged); a render of a footage Scene still fails when the photo the Scene was planned with has been removed; that the Product is absent from a footage Scene is asserted as "the Clip is where Product Lock has the Product", not by looking for the Product; a Clip whose worker stops, and one made and not kept, have no test of their own (the Still's twins do); how much `ClipQueue` and `ClipWorker` repeat of the Still's; what makes two Scenes ask for the same picture is written in more than one place; and the same failure of `StillWorker` to count a timeout after the provider answered as charged for, which this ticket did not touch.
