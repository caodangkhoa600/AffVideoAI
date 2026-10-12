# 03: A render with a generated backdrop

**What to build:** A render of the Storyboard version draws the approved Still behind the Product's real Cut-out in that Scene, with the creative template moving the Product and slowly moving the backdrop. No Clip is made for a backdrop. This is the first Hybrid video that differs from a Product Lock one. Paid generation as a step before rendering is ADR 0003.

**Blocked by:** 02

**Status:** needs-triage

- [ ] The Storyboard version refers to the Still its Scene uses
- [ ] The rendered Scene shows the Still behind the Cut-out, with the Product's shadow on it; the Product's pixels are the photo's
- [ ] The backdrop moves slowly, by the template's doing, for the whole Scene whatever its length
- [ ] A photo with no usable Cut-out is handled as today, on a Card over the Still
- [ ] The MP4 is still 1080x1920 H.264 with AAC at the target duration
- [ ] The Render job calls no provider and writes no generation cost
- [ ] A render of a Storyboard version whose generated Scene has no approved Still fails validation with a reason
- [ ] The per-Scene cache key includes the Still, so only that Scene is rendered again when the Still changes
