# Hybrid video: generated backdrops, generated footage and spoken Narration

Status: ready-for-agent

## Problem Statement

A Product Lock video is good enough to post, but every Scene is my Product on a studio backdrop with moving type, and it has no voice unless I record one myself. I want videos that look and sound closer to a produced advertisement, and eventually a tool that makes a full commercial. AI video tools can supply the footage, but they change how the Product looks, they cost money on every attempt including the bad ones, and I do not yet know whether their footage or their Vietnamese voices are good enough to post.

## Solution

Hybrid becomes real. A Hybrid video may have up to two Scenes with generated pictures: a generated backdrop behind my real Product, or generated footage of a setting or a person with no Product in it. Wherever the Product appears it is still its photo. The video can also be narrated by a generated Vietnamese voice speaking each Scene's narration text.

Nothing paid happens without my say-so. At Storyboard review I see what generation will cost, confirm once, and approve a cheap Still of each generated Scene. A backdrop is that Still, moved by the template. Generated footage is a Clip made from its Still, which is the expensive step. Stills and Clips are made once and used by every Variant of the Project. Rendering itself stays free.

The work starts with a throwaway test of real providers on a real Product, so that I can judge the footage by eye and the voices by ear before the rest is built.

This is the second of five phases toward a cinematic commercial. The decisions for the whole effort, and what is open for the later phases, are in `.scratch/cinematic-commercial/notes.md`.

## User Stories

### Provider test

1. As the founder, I want a throwaway test that runs one real Product through two or three video models and two or three Vietnamese voices, so that I can judge them before anything is built.
2. As the founder, I want the work to stop for my verdict after that test, so that the providers are chosen by what I saw and heard.
3. As the founder, I want each candidate's capabilities and prices confirmed against the vendor's own documentation, so that the choice does not rest on comparison blogs.

### Generated Scenes

4. As a member, I want to choose Hybrid for a Variant, so that some of its Scenes can have generated pictures.
5. As a member, I want at most two Scenes of a Hybrid video to be generated, the Hook Scene first, so that the cost is bounded and spent where it matters most.
6. As a member, I want a generated backdrop behind my real Product, so that the Scene looks produced while the Product stays exactly as in my photo.
7. As a member, I want a Scene of generated footage with no Product in it, so that the video has a setting or a person as well as the Product.
8. As a member, I want the Product never to be generated in a Hybrid video, so that its logo, shape and colours are never wrong.
9. As a member, I want the description of each generated Scene to come from the creative template and my Product's category and audience, so that I do not have to write prompts.
10. As a member, I want to edit that description before generating, so that I can steer the picture.
11. As a member, I want a generated Scene that shows what my Product does to require a Confirmed Fact, so that a picture never claims something I did not accept.
12. As a member, I want generated footage to show invented adults only, so that no real person, mascot or child appears.

### Stills and Clips

13. As a member, I want a Still of each generated Scene before anything else is made, so that I reject a bad composition for cents.
14. As a member, I want to approve a Still, or ask for another, so that only a picture I accept becomes a Clip.
15. As a member, I want an approved Still to be the backdrop itself, with the template moving the Product and the camera, so that a backdrop costs cents and never looks like a frozen video.
44. As a member, I want a Clip made only for generated footage and only from an approved Still, so that the expensive step starts from something I chose.
16. As a member, I want to see the Clip before rendering, so that I know what the video will contain.
17. As a member, I want to ask for another take of a Clip, so that I can replace one I do not like.
18. As a member, I want a Still or Clip used by every Variant of its Project, so that a Hook experiment pays for its footage once.
19. As a member, I want to pick a saved Still or Clip of my Organization for another Project, so that good footage is not paid for twice.
20. As a member, I want a generation that the provider refused or that failed to show a reason I can act on, so that I know whether to change the description or try again.

### Spoken Narration

21. As a member, I want Narration spoken from each Scene's narration text in Vietnamese, so that a video has a voice without my recording one.
22. As a member, I want to choose the voice in the Project's brief, with my Organization's default already selected, so that every Variant of a Product has the same narrator.
23. As a member, I want to hear each Scene's speech at Storyboard review, so that I can fix the wording before rendering.
24. As a member, I want only the Scenes whose narration text changed to be spoken again, so that an edit is quick and cheap.
25. As a member, I want a warning at Storyboard review when a Scene's narration text looks too long for the Scene, so that I can shorten it before paying.
26. As a member, I want speech that slightly overruns its Scene to be sped up by no more than a tenth, so that small overruns do not stop me.
27. As a member, I want a render to fail with a reason naming the Scene when its speech still does not fit, so that I know which text to shorten.
28. As a member, I want a recording I upload to be used in place of generated speech, so that my own voice always wins.
29. As a member, I want no Rights confirmation asked for generated speech, so that I am only asked about what I uploaded.

### Money

30. As a member, I want an estimate of what generation will cost before anything paid starts, so that I can decide whether it is worth it.
31. As a member, I want to confirm once before paid generation begins, so that nothing is spent by accident.
32. As a member, I want a ceiling on what a Project may spend on generation, so that retakes cannot run away.
33. As an Owner, I want to change my Organization's ceiling, so that it fits what I am prepared to spend.
34. As a member, I want to be refused, with the reason, when a generation would pass the ceiling, so that I know why nothing happened.
35. As a member, I want a paid generation never retaken unless I ask, so that I only pay for takes I chose.
36. As a member, I want a generation that failed for a technical reason and was not billed to be tried again without me, so that provider hiccups do not need my attention.
37. As a member, I want rendering never to spend money, so that I can render as often as I like.
38. As a member, I want the cost of every Still, Clip and piece of speech recorded, rejected ones included, so that Production cost is honest.
39. As a Lab member, I want a Clip's cost counted once for its Product and shown once, labelled as shared, on a Campaign, so that cost is never invented by splitting it.

### Telling viewers

40. As a member, I want each Rendered Video to show whether it contains generated footage or generated speech, so that I know what I am posting.
41. As a Lab member, I want a reminder to switch on the platform's label when I record a Published Post for such a video, so that I follow the platform's rules.

### Honesty about what produced it

42. As a member, I want pictures and speech that came from a fake provider labelled as such, so that I am never misled about what produced them.
43. As a developer, I want the system to run with no provider credentials, with generation unavailable and saying so, so that Product Lock works exactly as before.

## Implementation Decisions

### Provider test

- A throwaway script outside the application, like the day-one render test. It is judged by eye and ear and then discarded; only the choice of providers and what was learned carry forward.
- Images and video go through one aggregator, so that using a different model for a Scene is configuration. Speech comes from whichever vendor wins the listening test.

### Techniques and planning

- The Technique list gains generated backdrop and generated footage, and loses image-to-video. Product Lock allows neither new Technique.
- In Hybrid, the planning engine may assign a generated Technique to at most two Scenes, taking the Hook Scene first. Each creative template states which of its Scenes may be generated and with which Technique.
- With no real provider configured, Hybrid assigns nothing generated, as today.
- A creative template carries a description for each Scene that may be generated. The Product's category and audience are slotted in as data. A member's edited description replaces it for that Scene. Templates and their descriptions are versioned.
- A generated Scene whose description depicts what the Product does must cite a Confirmed Fact. Validation rejects it otherwise. Withdrawing that Fact flags the work that used the Clip, as it does for text.
- Descriptions ask for invented adults only. A provider's refusal is shown to the member as a failure with a reason.

### Stills and Clips

- A Still and a Clip are records of their own, owned by the Organization, stored under its namespace and reachable only through the API.
- A generated backdrop is an approved Still, drawn behind the Cut-out by the creative template, which supplies all movement. No Clip is made for a backdrop: the provider test showed that a generated video of a backdrop adds almost nothing over a picture, at about ten times the cost.
- A Clip is made only for generated footage, from exactly one approved Still, and is asked for at its Scene's length.
- A Still or Clip belongs to the Project it was made for and is used by every Variant of that Project. Another Project uses it only when a member picks it.
- A Storyboard version refers to the Still or Clip each generated Scene uses. Choosing a different one creates the next Storyboard version, like any edit.
- Clips are scaled to the output size at render. The chosen video model returns nearly that size already.
- The per-Scene cache key includes the Still or Clip a Scene uses.

### Narration

- Speech is made per Scene from its narration text, with the Project's voice. It is kept under a key derived from the text, the language and the voice, so unchanged Scenes are not spoken again.
- The voice is part of the Project's brief and defaults to the Organization's. Like the rest of the brief, it is fixed when the Project is created.
- A Variant's Narration is its uploaded recording if it has one, otherwise its generated speech, otherwise silence.
- Length is estimated from the text at Storyboard review and the member is warned. At render, speech longer than its Scene is sped up by at most 10 percent; beyond that the job fails with a reason that names the Scene.
- Generated speech is loudness-normalised and mixed with Music as an uploaded recording is.

### Paid generation

- Paid generation is a step of its own, before rendering (ADR 0003). It runs in the background on the existing queue, with the same Lease, Attempt and idempotency behaviour, and its state is the progress the member sees.
- The member is shown an estimate, from configured rates, and confirms once. That confirmation covers the Stills, Clips and speech in the estimate.
- A Project has a ceiling on generation, $5 by default, set on the Organization. A generation that would pass it is refused with the reason.
- Only a technical failure that was not billed is retried automatically. Another take is always the member's action and counts against the ceiling.
- The Render job spends nothing. Submitting a render for a Storyboard version whose generated Scenes lack an approved Still or Clip, or whose speech is missing, fails validation with a reason.
- One Production cost record is written for each Still, Clip and piece of speech, including rejected ones, with provider, model, an estimated amount and currency. A Clip's cost is never split across the videos that use it. Product totals count it once. A Campaign shows it once, labelled as shared.

### Providers

- New interfaces for image generation and for video generation from a Still; the existing video-generation interface, which starts from a Product photo, is replaced. The text-to-speech interface gains a voice.
- Each has a real implementation and a fake that returns fixed files. What a fake produced is labelled as fake in the data and on screen. A real provider is never replaced by a fake without the member being told.
- Credentials come from environment variables documented in the example file. With none set, generation is unavailable and says so.
- The language model stays the mock planner.

### Disclosure

- A Rendered Video records whether it contains generated footage and whether it contains generated speech. The Library, the preview and the download screen show it. Recording a Published Post for such a video shows a reminder to switch on the platform's label. Nothing is drawn into the picture.

## Testing Decisions

The seams are those of month one, and tests assert only on what a member could observe.

- **The HTTP API with the worker, real PostgreSQL and real object storage, and fake providers.** Covers the whole flow: estimate, confirmation, Still, approval, Clip, speech, render, and the properties of the resulting MP4. Also covers the ceiling, refusal without confirmation, another take, reuse of a Clip across Variants, an upload winning over generated speech, speech that does not fit, cost records and how they total, the disclosure marks, and cross-tenant denial for Stills, Clips and speech.
- **Pure functions.** Which Scenes Hybrid may generate, the two-Scene limit, validation of a generated Scene's Fact, the speech-length estimate and the speed-up limit, and cost calculation.
- **The browser.** The existing single flow is unchanged. No browser test is added.
- **Real providers.** Not run automatically. One manual run against the chosen providers, on a real Product, is recorded in this spec before it is marked done.
- No test result is reported as passing unless the test was run.

## Out of Scope

- A generated Product, in any Scene. That is Studio (ADR 0004).
- Characters or settings that stay consistent across Scenes, reference-guided generation, and storyboard images for a whole video.
- A real language model, creative direction, and scripts with a story.
- Sound effects, Music that is not uploaded, and people speaking on screen.
- 16:9, and durations beyond 30 seconds.
- More than two generated Scenes in a video.
- Credits, plans, a usage ledger and payments.
- Children, real people and brand mascots in generated footage.

## Further Notes

- Vocabulary follows `CONTEXT.md`: Technique, Generated backdrop, Generated footage, Still, Clip, Narration, Production cost.
- The work stops for the founder once: after the provider test.
- The budget while building is $100 a month across all providers.
- Prices gathered on 2026-10-12 are in the notes file. They come from third-party pages and are rough.
- Provider test, 2026-10-12 (ticket 01): the founder chose Kling 3 Pro for Clips and ElevenLabs v3 with the voice Sarah for speech, wants members to choose the voice, and found a generated video of a backdrop not worth posting. Backdrops became Stills as a result. Stills came from Nano Banana 2.
- Decided while writing this spec and not discussed beforehand: paid generation uses the existing queue; a Storyboard version refers to its Clips, so choosing another take makes a new version; the voice is fixed with the brief; refusal at the ceiling is a hard stop and not a warning; no browser test is added. Also decided that way, after the provider test: a Clip is asked for at its Scene's length, since the chosen model makes 3 to 15 seconds.
