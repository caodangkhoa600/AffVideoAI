# 08: Storyboard generation

**What to build:** A member generates a Storyboard for a Variant with no paid AI. The mock planner builds Scenes from the Product's Confirmed Facts using the Product Showcase creative template, and the planning engine assigns each Scene a Technique allowed in Product Lock. The member sees the Scenes, their text, their Technique and which Facts each used.

**Blocked by:** 05, 06, 07

**Status:** ready-for-agent

- [ ] Generating a Storyboard creates version 1 with ordered Scenes
- [ ] The same inputs always produce the same Storyboard
- [ ] Generated text uses only Confirmed Facts; Proposed and Withdrawn Facts never appear
- [ ] Each Scene records the Facts it used, including a copy of their text
- [ ] Scene durations sum exactly to the Project's target duration
- [ ] In Product Lock only static image, image motion and text animation are assigned
- [ ] Generation fails with a clear reason when the Product has no Confirmed Facts or no usable image
- [ ] The Storyboard is labelled as produced by the mock planner, in the data and on screen
- [ ] Free text from the Product is treated as data and cannot alter the planner's behaviour
- [ ] Pure-function tests cover Technique selection, duration allocation and Storyboard validation
- [ ] The language-model, video-generation, text-to-speech and image-processing provider interfaces exist; only the mock planner is implemented
