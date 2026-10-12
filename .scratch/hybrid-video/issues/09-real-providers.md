# 09: Real providers

**What to build:** Real implementations of image generation, video generation and speech for the providers the founder chose in ticket 01, behind the interfaces the fakes already satisfy. Then one manual run on a real Product, from Storyboard review to an approved Rendered Video, recorded in the spec.

**Blocked by:** 05, 06

**Status:** needs-triage

- [ ] Images and video go through one aggregator; the model for each is configuration
- [ ] Credentials are environment variables, documented in the example file, and never committed
- [ ] With no credentials, generation is unavailable and says so; a real provider is never replaced by a fake without the member being told
- [ ] A billed failure and an unbilled technical failure are told apart, and only the second is retried
- [ ] A provider's refusal reaches the member with its reason
- [ ] Automated tests never call a real provider
- [ ] One manual run on a real Product is recorded in the spec: what was generated, what it cost against the estimate, and the founder's verdict on the video
