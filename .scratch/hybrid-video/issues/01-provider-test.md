# 01: Provider test

**What to build:** A throwaway script, outside the application, that runs one real Product through real providers: a Still for a generated backdrop and one for generated footage, each kept Still as a 5-second Clip from three video models, the real cut-out over each backdrop Clip, and a few lines of Vietnamese narration in several voices. The founder watches and listens and gives a verdict on which providers to build on. This is a prototype: it is judged by eye and ear and then discarded.

**Blocked by:** None. Needs from the founder: a fal.ai key, and a key for ElevenLabs, Google Cloud Text-to-Speech or both.

**Status:** done

- [x] The script prints an estimate and generates nothing paid without an explicit confirmation
- [x] The models it calls and their inputs were read from the vendors' own documentation
- [x] Stills, Clips and speech are produced for a real Product
- [x] The real cut-out is shown over each generated backdrop
- [x] The founder has given a verdict: which image model, which video model, which voice, or that none is good enough
- [ ] The actual cost of the run is written in the comments, next to the estimate
- [x] What was learned (Clip lengths, how long generation takes, refusals, how the Product sits on a backdrop) is written in the comments for tickets 02 to 06
- [x] The open question in the spec is answered: what happens when a Scene is longer than the Clip it uses

## Comments

- 2026-10-12: The prototype is in `prototypes/provider-test/`. Its free paths were run: the estimate, the refusal to generate without `--yes`, the messages for a missing key, and the compose step, which was tried on a synthetic clip with the founder's iPhone cut-out from the day-one look test and gave a 1080x1920 H.264 file. No provider has been called, because there are no keys, so nothing about the requests themselves is proven. The test file in `input/test.json` describes a desk backdrop and a man walking in a city, for the iPhone.
- Read from vendor documentation on 2026-10-12:
  - fal.ai's queue is plain HTTP: submit to `queue.fal.run/<endpoint>` with `Authorization: Key …`, poll the `status_url` it returns, read the `response_url`. File inputs may be a Base64 data URI.
  - Nano Banana 2 takes `aspect_ratio: "9:16"`. Listed at $0.08 an image.
  - MiniMax H3 Max Turbo ($0.025 a second) and Kling 2.5 Turbo Pro ($0.07 a second) have no aspect-ratio input: the Clip follows the Still. Kling 3 Pro ($0.14 a second) names its image `start_image_url`, generates audio unless told not to, and accepts reference images, which Studio will need.
  - ElevenLabs speaks Vietnamese with Flash v2.5, v3 and v4, and not with Multilingual v2.
  - Google Chirp 3 HD supports `vi-VN`, with a speaking rate from 0.25 to 2.0 and no custom pronunciations.
- The estimate for a full run with two Stills is about $2.50.
- 2026-10-12, first real run, for the iPhone. Keys for fal.ai and ElevenLabs; Google was not tried.
  - Speech: four files from ElevenLabs (Flash v2.5 and v3, voices Roger and Sarah, the first two of the account, which are English-language voices). No verdict yet.
  - Stills: two from Nano Banana 2, estimated $0.16. Both kept to the description: no Product, no text. The founder rejected the backdrop Still and kept the footage Still.
  - Clips: the footage Still on three models, estimated $1.18. H3 Max Turbo gave 768x1344, 5.18 s, with an audio track of its own. Kling 2.5 Turbo Pro and Kling 3 Pro each gave 1076x1924, 5.04 s, no audio (Kling 3 was asked for none). Each took one to three minutes.
  - **Verdict on video: Kling 3 Pro.**
  - Learned: Kling returns nearly the output size, so scaling up from 720p does not arise with it. A Clip is about 5 seconds unless more is asked for; Kling 3 takes 3 to 15. A provider may attach audio that has to be removed.
  - **Verdict on speech: ElevenLabs v3, voice Sarah, was the best of the four.** The founder wants members to choose the voice.
  - Backdrop, second try: a dark stone tabletop seen straight on, estimated $0.08 for the Still, which the founder accepted, and $0.70 for one Clip on Kling 3 Pro (1076x1924, 5.04 s). The iPhone cut-out was put over the Clip with FFmpeg, with no shadow. In frames taken every second the backdrop hardly changes: the description asked for a still camera and only the lights moving. No verdict on it yet.
  - Estimated spend so far: $2.12 on fal.ai.
  - **Verdict on the backdrop: a generated video of it is not worth posting.** The founder saw a phone standing still in the middle with no motion and no sound. A second preview, free, moved the Product and the camera with FFmpeg and added the chosen narration. Decision: a generated backdrop is a Still, and the creative template supplies the movement; a Clip is made only for generated footage. A backdrop then costs about $0.08, not $0.78. The spec, the glossary and tickets 03 to 05 were changed to match.
  - A Scene longer than its Clip: the question now arises only for generated footage, and a Clip is asked for at its Scene's length, since Kling 3 makes 3 to 15 seconds. This was the agent's proposal and the founder has not been asked about it directly.
  - No image model was compared: Nano Banana 2 was the only one tried, and its Stills were accepted.
  - Still open: the actual charge on fal.ai. The box stays unticked until the founder reads it from the dashboard.
