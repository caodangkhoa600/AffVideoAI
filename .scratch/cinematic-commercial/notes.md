# Cinematic commercial: decisions so far

Notes from a grilling session on 2026-10-12 about the founder's change request "AffiVideo AI V2: Cinematic Commercial Engine". They record what was decided so that the next session can grill Phase 3 (Studio) without repeating it. Nothing here has been built. Phase 2 has a spec, `.scratch/hybrid-video/spec.md`, with no tickets yet.

Vocabulary follows `CONTEXT.md`, which was updated in the same session (Fact, Narration, Rights confirmation, Hybrid, Generated Product, Technique, Generated backdrop, Generated footage, Still, Clip, Production cost).

## The request, in short

The founder wants the premium output to be a fully produced 30-second commercial: generated actors and settings that stay consistent across cuts, the Product in the story, CGI demonstrations, narration, music, and a branded end card. The request names three levels (Basic, Pro, Studio) and five phases:

1. Finish the Product Lock workflow.
2. One real Hybrid workflow.
3. Creative direction, character and setting references, storyboard images, many generated Scenes.
4. Composition, audio, regenerating single Scenes, quality review.
5. Credits and usage tracking for selling it.

## The reference commercial

`docs/references/AQNVjikFPEZelu5b4D1hwU3B5OSBXV5kV1EYXHZdstIx3ifwNLBxC2MSfoxVWFTKegXAFQOO-lOqFfvoTEb6gkW9gy4M91ke3Gilbbyedg.mp4`: 30 seconds, 16:9, 640x360, about 14 cuts. It was viewed as frames only; its sound has not been described by anyone yet.

- A father in a purple apron and cape, a mother and two small children, in one bright white kitchen that stays the same across cuts.
- The father holds the bottle to camera for about 3 seconds with the label readable. The bottle also stands on the counter in the family scene.
- The actor is the figure printed on the bottle's label, so he is the brand's mascot.
- CGI demonstrations: foam forming a heart with lime slices, burnt grease dissolving off an air-fryer basket, gleaming plates and containers.
- A logo card midway, and a pack shot with the slogan at the end.

## What the code has today

- Product Lock works end to end, and the founder judged it good enough to post on 2026-10-08. Phase 1 is complete.
- `IVideoGenerator` and `ITextToSpeech` have no implementation. The only language model is the deterministic mock planner. `PlanningEngine.CanBeMade` returns false for every generative Technique, so Hybrid picks what Product Lock picks.
- No interface exists for image generation, sound effects or reference-guided video.
- A Project is 15 to 30 seconds and output is 1080x1920 only.
- A Storyboard is text plus Product photos. It has no generated images, characters, settings or stored generated video.
- Reusable unchanged: the job queue (Lease, Attempt, idempotency key), the per-Scene cache, Production cost records, approval, and Remotion plus FFmpeg assembly.

## Decisions for the whole effort

- **Scope of work now.** Vocabulary and overall shape are agreed for everything; only Phase 2 is specified in detail, as `.scratch/hybrid-video/spec.md`. There is no `docs/CINEMATIC_UPGRADE_PLAN.md`.
- **Why now.** Cinematic quality is for the tool that will be sold. It does not wait for the affiliate experiment: Product Lock videos get posted now so that Hybrid videos can later be compared against a real baseline in the Affiliate Lab.
- **Words.** Render Mode stays Product Lock and Hybrid. Basic and Pro are price-list names for later and are not domain terms. Studio is not a third Render Mode because it has its own workflow; it gets its name when Phase 3 is designed. "Shot" stays an avoided word for Scene.
- **Facts.** Text or pictures may say something about the Product only by using a Confirmed Fact. Story lines that say nothing about the Product are free. The planner marks which lines use a Fact, and the member sees that at Storyboard review. A generated Scene that shows what the Product does must cite a Confirmed Fact and fails validation without one.
- **Voice.** Always a narrator over the footage. Nobody on screen speaks, in any phase, so nothing needs lip-sync.
- **Money.** The member sees an estimate and confirms before anything paid starts. A paid generation is never retaken automatically; the system retries only a technical failure that was not billed. A retake is the member's action and counts against the ceiling. While building, the budget is $100 a month.
- **The Product in generated footage.** In Product Lock and Hybrid the Product is always its photo. Studio may generate the Product, as a defined exception (see Phase 3).

## Decisions for Phase 2 (Hybrid)

- **What is generated.** Two Techniques: generated backdrop (a Still behind the real Cut-out, moved by the template) and generated footage (a whole Scene with no Product in it, from a Clip). Image-to-video from a Product photo is out because it repaints the Product, and it leaves the Technique list.
- **Still first.** Every generated Scene starts as a Still the member approves. A backdrop is that Still; generated footage is a Clip made from it. This needs an image provider as well as a video provider.
- **Reuse.** A Still or Clip is reused by every Variant of its Project automatically, and by another Project only when a member picks it from the Organization's saved Clips.
- **Limits.** At most two generated Scenes per Hybrid video, the Hook Scene first. The 720p question went away: the chosen model returns nearly the output size.
- **Narration from text is in Phase 2.** Speech is made per Scene from its narration text, so editing one Scene re-speaks only that Scene. A Variant has an upload or generated speech, never both, and an upload wins. The member is warned at Storyboard review from an estimated length. After generating, speech is sped up by at most 10 percent to fit its Scene; beyond that the render fails with a reason naming the Scene. Rights confirmation applies only to uploads.
- **Voice choice.** Picked per Project as part of the brief, with a default on the Organization.
- **A render never spends money.** All paid generation (Stills, Clips, speech) happens at Storyboard review, after one confirmation, and the member sees and hears the results there. The Render job only assembles what is approved, so it stays free and safe to retry.
- **Cost and ceiling.** A Clip's cost is recorded once, on the Clip, and never split across the videos that use it. Product totals count it once; a Campaign shows it once, labelled as shared. The ceiling is on the Project, $5 by default, changeable per Organization.
- **Prompts.** Each creative template carries its own descriptions for Stills, with the Product's category and audience slotted in as data. The member can edit the description before generating. No real language model in Phase 2.
- **Providers.** One aggregator for images and video, so a different model per Scene is configuration. Speech from whichever vendor wins a listening test. Capabilities and prices are confirmed against the vendors' own documentation before building.
- **Throwaway test, done on 2026-10-12** (`.scratch/hybrid-video/issues/01-provider-test.md`). The founder chose Kling 3 Pro for Clips and ElevenLabs v3 with the voice Sarah, and wants members to choose from a short list of voices. A generated video of a backdrop was judged not worth posting, which is why a backdrop is now a Still. Generated footage of a person walking came out well.
- **Disclosure.** A Rendered Video records whether it contains generated footage or generated speech. The Library and the download screen show it, and recording a Published Post reminds the member to switch on the platform's label. Nothing is burned into the picture.
- **Format.** 9:16 only. 16:9 arrives with Studio.
- **Tests.** Fake providers return fixed files and are labelled as fake in the data and on screen, like the mock planner. A real provider is never replaced by a fake without the member being told. One manual run against real providers is recorded before the spec is marked done.

## Decisions already made for Phase 3 (Studio)

- **Generated Product is allowed in Studio only.** The founder wants members to be able to make a video like the reference, including a person holding the Product.
- **Its safeguards.** A Scene that shows a generated Product carries the Generated Product mark permanently. The member accepts each such Scene by itself, beside the real photo. The system refuses to try unless the Product has enough photos (proposed: front, back and one angle).
- **People.** Invented adults only. No likeness of a real person or of a brand's mascot, and no uploaded faces. Children are allowed only if the chosen provider's terms permit it; this is checked when Studio providers are picked.
- **A real language model** arrives with Studio's creative direction.

## Open for the Phase 3 session

- What Studio is called, and whether it is a kind of Project, a separate thing beside Project, or something else.
- How characters and settings are described, approved and kept consistent across Scenes, and what they are called.
- How a creative direction is produced and approved, and what a member can edit in it.
- Whether Studio keeps creative templates and Variants, or a commercial is written fresh each time.
- How the Generated Product exception works in practice: which Scenes may use it, and what happens when the result is rejected repeatedly.
- CGI demonstrations: how a Fact is tied to a picture, and how a member judges that the picture does not overstate the Fact.
- Durations beyond 30 seconds, and 16:9.
- Sound effects and Music that is not uploaded.
- The ceiling and estimate for a commercial with about ten Clips.
- Regenerating one Scene of a finished commercial, and what "quality review" means beyond a member's approval.
- Which providers, and their terms on generated people.

## Prices found on 2026-10-12

These come from third-party comparison pages that disagree with each other. They are rough and must be checked against vendors' own price lists.

| What | Rough price |
|---|---|
| Cheap video models (Veo 3.1 Lite, Runway Gen-4 Turbo) | $0.03 to $0.05 per second |
| Mid-range (Kling 3.0, Runway Gen-4.5, Veo 3.1 Fast) | $0.08 to $0.17 per second |
| Top tier (Veo 3.1 Standard with audio) | $0.40 to $0.75 per second |
| Reference-guided generation, 3 to 7 reference images | $0.60 to $1.70 per clip |
| Text-to-speech for 30 seconds of narration | a few cents at most |

Models return 4 to 8 second clips, so more footage is paid for than is used. A Hybrid video with two or three generated Scenes is roughly $0.50 to $2 for one take each at mid-range prices. A commercial like the reference, with about ten Clips, is roughly $4 to $9 for one take each and $10 to $25 with two or three takes per Clip; at top-tier prices, $60 to $110.

## ADRs written

- `docs/adr/0003-a-render-never-spends-money.md`: paid generation is a separate, confirmed step before the Render job.
- `docs/adr/0004-the-product-is-generated-only-in-studio.md`: in Hybrid the Product is always its photo; a generated Product exists only in Studio, behind the Generated Product mark.
