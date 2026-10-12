# Written Storyboard: a language model writes the text of a Project's Storyboard

Status: ready-for-agent

## Problem Statement

Almost every video I make is the same video. There are three creative templates, and the mock planner writes nothing: each Scene's text is a fixed sentence pattern with my Product's name, my Hook or a Fact placed in it whole. The reveal always says "Đây là" and the name, the closing always says the same call to action, and the descriptions for generated Stills change only by the Product's category and audience. Two Products in the same category get the same words and nearly the same pictures. These are the videos I am posting now to learn what works in the Affiliate Lab.

## Solution

A real language model writes each Storyboard for the Product in front of it: what the narrator says over each Scene, the lines set in type, the call to action, and the descriptions that generated Stills and Clips are made from. The creative template still decides which Scenes there are, how each is laid out and how much text fits, so the video still renders exactly as designed.

I still write the Hook, because the Hook is what I am comparing. The model may say something about my Product only with the Confirmed Facts I chose, and it marks which line uses which. If what it writes breaks a rule, the system asks again by itself, twice at most, and then tells me why it failed. It never quietly gives me the mock planner's text instead.

Writing costs about a cent, so I am not asked to confirm it, but it is recorded and counts against the Project's ceiling. In the Affiliate Lab I can see which planner wrote each video and compare the two.

This comes after Hybrid and before Studio. Why, and how it fits the larger effort, is in `.scratch/cinematic-commercial/notes.md`.

## User Stories

### Writing

1. As a member, I want the narration text of each Scene written for my Product, so that two Products do not get the same sentences.
2. As a member, I want the on-screen text of each Scene written for my Product, within what its layout can show, so that it reads well and still fits.
3. As a member, I want the call to action written for my Product and its audience, so that every video does not close on the same line.
4. As a member, I want the descriptions for generated Stills and Clips written for my Product, so that Hybrid videos of similar Products do not share one backdrop.
5. As a member, I want the creative template to keep deciding the Scenes, their order and their layouts, so that a written Storyboard renders as the template was designed.
6. As a member, I want the text written in the Project's language, so that a Vietnamese video is written in natural Vietnamese.
7. As a member, I want my Hook used exactly as I typed it, so that a Variant is still the Hook I chose to test.

### Facts

8. As a member, I want the model to say something about my Product only by using a Confirmed Fact I chose, so that it invents nothing about what I sell.
9. As a member, I want each line that uses a Fact to show which Fact, at Storyboard review, so that I can check it.
10. As a member, I want a description of a generated Scene that shows what my Product does to cite a Confirmed Fact, so that a picture never claims something I did not accept.
11. As a member, I want work written from a Fact that is later Withdrawn to be Flagged for Review, as today, so that the model's text is treated like any other.
12. As a member, I want what I typed (the Product's name, the Hook, the Facts) treated as material to write from and never as instructions, so that no text of mine or of an imported page can change what the model is told to do.

### Review and editing

13. As a member, I want to edit any written text at Storyboard review, as today, so that the model's wording is a draft and not a verdict.
14. As a member, I want to ask for the Storyboard to be written again, so that I can see another take without changing my Hook.
15. As a member, I want each writing to be a new Storyboard version, with the earlier ones kept, so that I can go back.
16. As a member, I want a Scene I edited to keep the Manually Edited mark, as today, so that it stays clear which words are mine.

### When writing goes wrong

17. As a member, I want writing that breaks a rule to be asked for again without me, at most twice, so that an overlong line does not need my attention.
18. As a member, I want a reason I can act on when writing still fails, so that I know whether to shorten a Fact, change the Hook or try again.
19. As a member, I want to be told when the language model cannot be reached, and to try again myself, so that I am never handed the mock planner's text without knowing.

### Money

20. As a member, I want no confirmation asked for writing, so that I am only asked about costs worth my attention.
21. As a member, I want every writing recorded as Production cost, the rejected ones included, so that the totals are honest.
22. As a member, I want writing to count against the Project's ceiling, so that one ceiling covers everything a Project spends.

### Comparing

23. As a member, I want each Storyboard and Rendered Video to show which planner wrote it, so that I know what I am looking at.
24. As a Lab member, I want to see and filter by planner in the Affiliate Lab, so that I can tell whether written Storyboards perform better than the mock planner's.
25. As a member, I want my existing Variants to keep the Storyboards they have, so that videos I already posted still match their records.

### Honesty about what produced it

26. As a developer, I want the system to run with no language-model credentials, using the mock planner and saying so, so that everything works as before on a machine without keys.
27. As a developer, I want the mock planner kept for tests and labelled as mock, so that tests are deterministic and free.

## Implementation Decisions

### What is written

- The language-model interface is extended, not replaced. For each Scene of the creative template it returns the on-screen text, the narration text, the Facts used, and, where the template allows the Scene to be generated, a description for its Still or Clip.
- The creative template is unchanged in what it fixes: the Scenes, their order, share of the time, layouts, allowed Techniques, text limits and Fact pacing. Its existing description for a generated Scene becomes guidance given to the model: what the picture is for and where the Product or the text will sit.
- The Hook Scene's text is the member's Hook, placed whole. The model is not asked to write it.
- The closing's call to action is written by the model within the template's limit for it.
- The system, not the model, appends the fixed ending of every description of generated footage: invented adults only, no child, no mascot, no Product, no text, no logos.
- A member's edit of a description replaces it for that Scene, as today.

### Facts

- The model is given the Confirmed Facts chosen for the brief, each with its identifier, and is told that these are the only statements it may make about the Product. It returns the identifiers each Scene used.
- Validation rejects a writing that cites a Fact not in the brief, a Facts Scene that cites none, and a generated Scene that shows what the Product does without a Fact.
- Everything a member typed is passed as data, separate from the instructions, and the instructions say so.

### Validation and asking again

- A writing is checked against the template before anything is saved: one text per Scene, the text limits of each layout, the Fact rules above, and the language.
- A writing that fails is asked for again with the reasons, at most twice. A third failure ends the request with the reasons shown to the member. Nothing is saved from a failed writing except its cost.
- A provider that cannot be reached, or that refuses, ends the request with a reason. The mock planner is never substituted.

### The model

- Claude Sonnet 5.5 (`claude-sonnet-5-5`), asked for structured output that matches the shape above. The model is configuration.
- The credential comes from an environment variable documented in the example file. With none set, the mock planner writes, and its Storyboards are labelled as the mock planner's, as today.
- A Storyboard records which planner wrote it, and for a real model, which model.

### Money

- Writing is not confirmed (ADR 0006). One Production cost record is written for each call to the model, including those whose writing was rejected, from the tokens the provider reports and rates the Organization configures.
- The amount counts against the Project's ceiling.

### Showing the planner

- The Storyboard review, the Library and the download screen show the planner.
- The Affiliate Lab dashboard and the list of Published Posts show the planner and can be filtered by it.
- Existing Storyboards are not rewritten. A Variant gets a written Storyboard only when a member asks for one.

## Testing Decisions

The seams are those of month one, and tests assert only on what a member could observe.

- **The HTTP API with real PostgreSQL, and a scripted language model.** The scripted model returns what a test tells it to, so the tests cover: a valid writing saved as a new Storyboard version; text over a limit asked for again and then accepted; three failures ending with reasons and nothing saved; a cited Fact that is not in the brief; an unreachable provider; the Hook kept whole; a written description used for a Still; cost records for accepted and rejected writings and how they count against the ceiling; the planner shown and filtered in the Lab; cross-tenant denial.
- **Pure functions.** Validation of a writing against a creative template, and cost from token counts.
- **The mock planner.** Its existing tests stay as they are.
- **The browser.** The existing single flow is unchanged. No browser test is added.
- **The real model.** Not run automatically. One manual run on a real Product, for each creative template, is recorded in this spec before it is marked done, with the founder's verdict on the Vietnamese.
- No test result is reported as passing unless the test was run.

## Out of Scope

- The model choosing the Scenes or their order, or a video without a creative template. That is a Commercial.
- The model writing or suggesting Hooks.
- The model proposing Facts, or choosing which Confirmed Facts to use.
- Rewriting the Storyboards of existing Variants.
- A note from the member when asking for another writing.
- A second model that checks the first.
- Languages other than those a Project already allows.

## Further Notes

- Vocabulary follows `CONTEXT.md`: Storyboard, Scene, Hook, Fact, Confirmed, Manually Edited, Still, Clip, Production cost.
- A known limit: whether a line that cites no Fact nevertheless says something about the Product cannot be checked mechanically. The instructions forbid it, the member sees at review which lines cite a Fact, and the manual run looks for it. If it happens in practice, a checking pass is the next step.
- The price of a writing was estimated at about a cent and has not been checked against Anthropic's price list. Check it, and the current way to ask for structured output, before building.
- Decided while writing this spec and not discussed beforehand: the Hook Scene's text is the Hook placed whole; the system appends the fixed ending of a description; the template's own description becomes guidance to the model; with no credential the mock planner writes, as today; a writing that would pass the ceiling is refused like any other spending; the planner is also shown in the Library and on the download screen; asking for another writing takes no note from the member.
