# 06: Spoken Narration

**What to build:** A Variant's Narration can be spoken from each Scene's narration text by a fake speech provider. The voice is chosen in the Project's brief. The member hears each Scene's speech at Storyboard review, and a render mixes it under the right Scenes. It does not depend on generated Scenes and works in Product Lock too.

**Blocked by:** 01

**Status:** needs-triage

- [ ] The voices on offer are a short list held in configuration, each checked by ear for Vietnamese, and not the provider's whole library; the list starts with ElevenLabs v3, voice Sarah, which the founder chose in ticket 01
- [ ] A member can play a sample of each voice before choosing
- [ ] The Project's brief has a voice, defaulting to the Organization's; an Owner sets the Organization's default
- [ ] Speech is made per Scene, under the same estimate and confirmation as other generation
- [ ] Only Scenes whose narration text, language or voice changed are spoken again
- [ ] The member can listen to each Scene's speech before rendering
- [ ] A Scene whose text looks too long for it shows a warning at Storyboard review, before anything is paid
- [ ] At render, speech longer than its Scene is sped up by at most 10 percent; beyond that the job fails with a reason that names the Scene
- [ ] Each Scene's speech starts with its Scene in the Rendered Video
- [ ] An uploaded recording is used in place of generated speech; with neither, the video is silent
- [ ] Generated speech is levelled and mixed with Music as an upload is
- [ ] No Rights confirmation is asked for generated speech
- [ ] Each piece of speech has a Production cost record
- [ ] The Render job calls no provider; a render whose speech is missing fails validation with a reason
