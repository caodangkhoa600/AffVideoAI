# Cinematic commercial: decisions so far

Notes from two grilling sessions on 2026-10-12 about the founder's change request "AffiVideo AI V2: Cinematic Commercial Engine". The first settled the whole effort and Phase 2. The second settled Phase 3 (Studio) and added a piece of work before it: a real language model that writes a Project's Storyboard. Nothing of Phase 3 has been built.

- Phase 2 is specified in `.scratch/hybrid-video/spec.md` and is being built; its tickets show how far.
- The language model for Projects is specified in `.scratch/written-storyboard/spec.md`.
- Phase 3 has no spec yet. It has two tickets in `issues/`: a throwaway provider test, whose result decides what the first version contains, and a check of Kling's terms.

Vocabulary follows `CONTEXT.md` (Fact, Narration, Rights confirmation, Hybrid, Generated Product, Technique, Generated backdrop, Generated footage, Still, Clip, Production cost, and from the second session Commercial, Character, Setting, Creative direction).

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

## What the code had at the start of 2026-10-12

- Product Lock works end to end, and the founder judged it good enough to post on 2026-10-08. Phase 1 is complete.
- The only language model is the deterministic mock planner. It writes nothing: each Scene's text is a fixed sentence pattern with the Product's name, the Hook or a Fact placed in it whole.
- There are three creative templates (Product Showcase, Luxury Cinematic, Problem-Solution). Each fixes the Scenes, their order, their share of the time and their layout, and carries the descriptions for its Stills.
- A Project is 15 to 30 seconds and output is 1080x1920 only.
- A Storyboard is text plus Product photos. It has no characters or settings.
- Reusable unchanged: the job queue (Lease, Attempt, idempotency key), the per-Scene cache, Production cost records, approval, and Remotion plus FFmpeg assembly.

## Decisions for the whole effort

- **Scope of work now.** Vocabulary and overall shape are agreed for everything. There is no `docs/CINEMATIC_UPGRADE_PLAN.md`.
- **Order of work.** Finish Hybrid, then the language model for Projects, then Studio.
- **Why now.** Cinematic quality is for the tool that will be sold. It does not wait for the affiliate experiment: Product Lock videos get posted now so that Hybrid videos can later be compared against a real baseline in the Affiliate Lab.
- **Words.** Render Mode stays Product Lock and Hybrid. Basic, Pro and Studio are price-list names and are not domain terms. What Studio makes is a Commercial. "Shot" stays an avoided word for Scene.
- **Facts.** Text or pictures may say something about the Product only by using a Confirmed Fact. Story lines that say nothing about the Product are free. Whatever writes the text marks which lines use a Fact, and the member sees that at review. A generated Scene that shows what the Product does must cite a Confirmed Fact and fails validation without one.
- **Voice.** Always a narrator over the footage. Nobody on screen speaks, in any phase, so nothing needs lip-sync.
- **Money.** The member sees an estimate and confirms before any picture, video, speech or music is generated. Writing text is not confirmed: it is recorded as Production cost and counts against the ceiling (ADR 0006). A paid generation of a picture, video, speech or music is never retaken automatically; the system retries only a technical failure that was not billed. A retake is the member's action and counts against the ceiling. The budget while building was $100 a month and is $200 a month while Studio is built.
- **The Product in generated footage.** In Product Lock and Hybrid the Product is always its photo. A Commercial may generate the Product, as a defined exception (ADR 0004).

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
- **Format.** 9:16 only. 16:9 arrives with the Commercial.
- **Tests.** Fake providers return fixed files and are labelled as fake in the data and on screen, like the mock planner. A real provider is never replaced by a fake without the member being told. One manual run against real providers is recorded before the spec is marked done.

## Decisions for the language model in Projects

The founder's objection: with three templates and fixed sentence patterns, almost every video is the same. Specified in `.scratch/written-storyboard/spec.md`.

- **When.** Before Studio, not with it. The sameness affects the videos being posted now for the Affiliate Lab, and the Fact rule is the hardest part of Studio's writing, so it is proven here first.
- **What it writes.** Each Scene's narration text and on-screen text, the call to action, and the descriptions for Stills and Clips, all inside the creative template. The template still decides the Scenes, the layouts and the text limits. The model does not choose the structure.
- **What the member keeps.** The Hook. A Variant is its template and its Hook and is the unit compared in the Lab, so a Hook the model wrote would empty the experiment. The model uses only the Confirmed Facts the member chose and marks which line uses which.
- **Invalid writing.** The system asks again at most twice, with the reason, then fails with the reason. It never falls back to the mock planner, and neither does an unreachable provider.
- **Model.** Claude Sonnet 5.5, returning structured output. The same integration writes a Commercial's creative direction later.
- **Lab.** A Storyboard records which planner wrote it. The Lab shows that and can filter by it. Existing Variants keep their Storyboards.

## Decisions for Phase 3 (Studio)

- **What it is.** A Commercial: a thing of its own beside Project, belonging to one Product (ADR 0005). It reuses Scene, Still, Clip, Narration and Production cost. It has no creative template, no Variant and no Render Mode. It is written fresh by the language model each time; a second treatment is a second Commercial.
- **Who makes it.** The same members as today, who may not own the brand. A logo or a slogan appears only if the member uploads it with a Rights confirmation, and a slogan that says something about the Product needs a Confirmed Fact. No brand's property is generated.
- **Shape.** 30 seconds. 16:9 or 9:16, chosen when the Commercial is created and fixed after. 8 to 12 Scenes of 2 to 5 seconds each. The last Scene is a pack shot of the Product's real photo.
- **First version.** Consistent people in one setting, a person holding the Product, and the pack shot. CGI demonstrations wait for a second version, because tying a picture to a Fact without overstating it is a design problem of its own.
- **The brief.** A Product, the audience (starting from the Product's), an objective, the format, a voice, the Confirmed Facts that may be used, and an optional line of the member's own idea. Vietnamese, like a Project. No Hook: the opening is written by the model and edited in the creative direction.
- **Creative direction.** What the model writes first, and the member approves: an idea in two or three sentences, the Characters and the Setting in words, the Scenes in order (what happens, who is in it, how long, whether it shows a Generated Product, which Facts it uses), and the narration. The member can edit any line, add, remove or reorder Scenes, or ask for it to be written again with a note. Each rewriting is a new version and the old ones are kept. The model cannot break the shape above or exceed two Scenes with a Generated Product.
- **Characters and Setting.** One Setting and at most three Characters, each a description plus a reference picture the member approves before any Scene is generated. They belong to their Commercial; reuse across Commercials comes later. Invented adults only. No children in the first version, and the model is told not to write any: no vendor documents that ordinary depictions of children are allowed, and Google blocks them outright in reference modes.
- **Consistency.** Decided at the Still. A Scene's Still is generated from the reference pictures of the Characters in it and of the Setting, plus the Product's real photos when the Scene shows a Generated Product. Its Clip is made from that Still, with the same references given to the video model. The models are configuration, starting with Nano Banana Pro for Stills and Kling O3 Pro for Clips.
- **Four stages.** Each has its own estimate and confirmation, and opens only when the one before is approved: (1) the creative direction, in words; (2) reference pictures of the Characters and Setting; (3) a Still for each Scene; (4) Clips, speech and music. Assembly stays free (ADR 0003).
- **Generated Product.** At most two Scenes of a Commercial, named in the creative direction. Each carries the mark permanently and is accepted by itself beside the real photo. The Product needs a front, a back and an angled photo. There is no separate limit on tries; the ceiling is the limit. At any time the member can switch such a Scene to a version without the Product, so one bad Scene never blocks a Commercial. The pack shot guarantees the real Product is seen.
- **Narration.** Belongs to the Commercial and is split into sentences, each lying over one or more adjacent Scenes. Speech is made per sentence, so editing one re-speaks only that one. It is sped up by at most 10 percent to fit, and beyond that the member is told which sentence. A sentence about the Product cites a Confirmed Fact.
- **Text on screen.** Only on the end card (the Product's name, a call to action, a slogan if uploaded) and on a logo card if a logo was uploaded. Remotion draws it. No text is ever in a generated picture, and there are no captions in the first version.
- **Sound.** One piece of Music for the whole Commercial, generated in stage 4 from a description the model writes, or uploaded with a Rights confirmation; an upload wins. No sound effects in the first version, because placing each on its frame is separate work.
- **Money.** One pass is about $6: reference pictures $0.60, ten Stills $1.50, about 32 seconds of Clips $3.60, speech and music under $0.20. With retakes, $12 to $18. The ceiling is on the Commercial, $20 by default, changeable per Organization, and every stage shows what has been spent against it.
- **Changing a finished Commercial.** The member regenerates the Still or Clip of one Scene, with an estimate and confirmation, and assembles again for free. The earlier video is kept and the new one is another version. Other Scenes reuse their Clips through the per-Scene cache.
- **Quality review.** A member's approval, plus cheap automatic checks before the member is shown anything: a Clip is long enough and the right shape, speech fits, no black frames. No model judges pictures yet.
- **What comes out.** A Rendered Video. It always records generated footage and generated speech, and carries the Generated Product mark if a Scene does. The Library, download, Published Posts and the Affiliate Lab work unchanged. Where the Lab groups by creative template, Commercials are a group of their own.
- **Access.** Behind a flag on the Organization, like the Affiliate Lab (ADR 0001). Only the founder's Organization has it until Kling's terms are settled and one Commercial has been posted.
- **Before the spec.** A throwaway provider test of about $15 (`issues/01-studio-provider-test.md`) answers whether a Character holds across four Scenes in one Setting, and whether a held bottle's label is right in the Still and in the Clip. If the label fails, the Generated Product leaves the first version and the Product appears only in the pack shot.
- **Before selling.** Kling's terms on commercial use are read by the founder (`issues/02-kling-commercial-terms.md`). This does not block building. If Kling cannot be used, the video model changes to Wan 3.0 or Seedance by configuration.

## Left open on purpose

- CGI demonstrations: how a Fact is tied to a picture, and how a member judges that the picture does not overstate it.
- Children as Characters.
- Sound effects.
- Characters and Settings saved for the Organization and used by another Commercial.
- Durations beyond 30 seconds.
- A model that judges the quality of pictures.
- Phase 5: credits, plans and payments.

## Prices and capabilities found on 2026-10-12

Read from the vendors' own pages or from fal.ai's, unless marked. Check them again before building.

| What | Model | Found |
|---|---|---|
| Stills from references | Nano Banana Pro | 14 references (up to 5 people, 6 objects); about $0.15 an image on fal.ai; 16:9 and 9:16 |
| Clips from references | Kling O3 Pro, on fal.ai | Up to 7 images or elements for people, objects and a scene together; 3 to 15 seconds; both formats; $0.112 a second without audio |
| Clips, alternatives | Wan 3.0; Seedance 2.0 | Wan: 10 reference images, $0.05 to $0.20 a second. Seedance: 9 images, about $0.30 a second at 720p, and reported by a third party to reject reference pictures with realistic faces |
| Clips from references | Veo 3.1 | 3 references, 8 seconds only, adults only in reference modes; its fal.ai reference endpoint is marked for removal on 2026-10-22 |
| Sound effects | ElevenLabs | About $0.12 a minute, described as royalty-free |
| Music | Eleven Music | About $0.15 a minute direct; commercial use from the Starter plan. fal.ai's two pages give $0.60 and $0.80 |
| Speech, 30 seconds | ElevenLabs | A few cents at most |

Not confirmed:

- Whether a held bottle keeps a correct, readable label. No vendor claims it and no credible independent test was found.
- Whether any vendor other than Google blocks ordinary depictions of children.
- The wording of Kling's clauses on commercial use, and whether they bind output obtained through fal.ai. A summary of its consumer terms says advertising use needs written permission.
- Kling 4.0, reported as due in October 2026 with 15 references, 30 seconds and 4K.
- Licence terms for Stable Audio and Suno from the vendors themselves.

## ADRs written

- `docs/adr/0003-a-render-never-spends-money.md`: paid generation is a separate, confirmed step before the Render job.
- `docs/adr/0004-the-product-is-generated-only-in-studio.md`: in Hybrid the Product is always its photo; a generated Product exists only in a Commercial, behind the Generated Product mark.
- `docs/adr/0005-a-commercial-is-not-a-project.md`: a Commercial is a thing of its own beside Project.
- `docs/adr/0006-writing-is-recorded-not-confirmed.md`: text from a language model is recorded and counted, without a confirmation.
