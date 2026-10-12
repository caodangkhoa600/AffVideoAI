# 01: Studio provider test

**What to build:** A throwaway script, outside the application, like the Hybrid provider test in `prototypes/provider-test/`. For one real Product that has a front, a back and an angled photo, it makes: a reference picture of one invented adult and one of a setting; four Stills of that person in that setting doing different things, each generated from the two references; one more Still in which the person holds the Product, generated from the references and the Product's photos; and a Clip from two of the Stills, one of them the held Product, with the same references given to the video model. The founder looks at them and gives a verdict. This is a prototype: it is judged by eye and then discarded.

It answers two questions, and the Studio spec is not written until it has:

1. Does a Character stay the same person across four Scenes in one Setting?
2. Is a held Product's label correct and readable, in the Still and in the Clip?

If the second answer is no, the Generated Product leaves the first version of the Commercial and the Product appears only in the pack shot.

**Blocked by:** None. Needs from the founder: the fal.ai key, and a Product with the three photos.

**Status:** ready-for-agent

- [ ] The script prints an estimate and generates nothing paid without an explicit confirmation
- [ ] The models it calls and their inputs were read from the vendors' own documentation; start with Nano Banana Pro for Stills and Kling O3 Pro reference-to-video for Clips
- [ ] Reference pictures, five Stills and two Clips are produced for a real Product, in 16:9 and with at least one Still and Clip in 9:16
- [ ] The held-Product Still and frames of its Clip are shown beside the real photo
- [ ] The founder has given a verdict on each of the two questions
- [ ] The estimate and the actual cost of the run are written in the comments; the budget is about $15
- [ ] What was learned (how many references each model needed, refusals, how long generation takes, how short a Clip can be asked for) is written in the comments for the Studio spec

## Comments

- 2026-10-12: Decided in the Phase 3 grilling session; see `../notes.md`. No evidence was found, from vendors or independent tests, that any model keeps a held bottle's label correct, which is why this comes before the spec.
