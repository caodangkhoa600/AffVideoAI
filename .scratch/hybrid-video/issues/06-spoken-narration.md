# 06: Spoken Narration

**What to build:** A Variant's Narration can be spoken from each Scene's narration text by a fake speech provider. The voice is chosen in the Project's brief. The member hears each Scene's speech at Storyboard review, and a render mixes it under the right Scenes. It does not depend on generated Scenes and works in Product Lock too.

**Blocked by:** 01

**Status:** done

- [x] The voices on offer are a short list held in configuration, each checked by ear for Vietnamese, and not the provider's whole library; the list starts with ElevenLabs v3, voice Sarah, which the founder chose in ticket 01
- [x] A member can play a sample of each voice before choosing
- [x] The Project's brief has a voice, defaulting to the Organization's; an Owner sets the Organization's default
- [x] Speech is made per Scene, under the same estimate and confirmation as other generation
- [x] Only Scenes whose narration text, language or voice changed are spoken again
- [x] The member can listen to each Scene's speech before rendering
- [x] A Scene whose text looks too long for it shows a warning at Storyboard review, before anything is paid
- [x] At render, speech longer than its Scene is sped up by at most 10 percent; beyond that the job fails with a reason that names the Scene
- [x] Each Scene's speech starts with its Scene in the Rendered Video
- [x] An uploaded recording is used in place of generated speech; with neither, the video is silent
- [x] Generated speech is levelled and mixed with Music as an upload is
- [x] No Rights confirmation is asked for generated speech
- [x] Each piece of speech has a Production cost record
- [x] The Render job calls no provider; a render whose speech is missing fails validation with a reason

## Comments

- 2026-10-13: Built test-first against the fake speech provider. Decided with the founder before building:
  - A version with no speech at all, and no uploaded narration, renders silent, as before. Once any Scene of it has speech, a Scene whose narration text was never spoken fails the render while validating, with a reason that names the Scene.
  - Nobody approves speech. It is used as soon as it is made; to have it said otherwise, the member changes the narration text.
  - How long narration text will take is estimated from its words at a pace for its language (4 words a second for Vietnamese, where a word is a syllable) and a pause at each mark inside it. The numbers are a guess until ElevenLabs has been listened to in ticket 09. The written-Storyboard spec uses the same estimate as a rule for the model.
  - A voice's sample is a recorded file named in configuration, so playing it asks no provider for anything.
- What the list of voices holds is a setting. With none configured there is one voice, `sarah`. Which ElevenLabs model and voice id `sarah` stands for is ticket 09's to wire: nothing here calls ElevenLabs.
- Not done here: the Rendered Video does not yet record that it contains generated speech (ticket 08), and no real voice has been heard. The web pages for speech and voices pass typecheck and lint and were not opened in a browser.
- The first render after the worker image is rebuilt twice stayed in `GeneratingAssets` until the tests gave up, and passed when run again on the same image. The cause is not known.
