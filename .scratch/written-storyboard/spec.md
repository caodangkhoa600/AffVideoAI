# Written Storyboard: a language model writes the text of a Project's Storyboard

Status: ready-for-agent

Blocked by: `.scratch/hybrid-video/issues/06-spoken-narration.md`

## Problem Statement

Almost every video I make is the same video. There are three creative templates, and the mock planner writes nothing: each Scene's text is a fixed sentence pattern with my Product's name, my Hook or a Fact placed in it whole. The reveal always says "Đây là" and the name, the closing always says the same call to action, and the descriptions for generated Stills change only by the Product's category and audience. Two Products in the same category get the same words and nearly the same pictures. These are the videos I am posting now to learn what works in the Affiliate Lab.

## Solution

A real language model writes each Storyboard for the Product in front of it: what the narrator says over each Scene, the call to action, the label of a Solution Scene, and the descriptions that generated Stills and Clips are made from. The creative template still decides which Scenes there are, how each is laid out and how much text fits, so the video still renders exactly as designed. The lines a layout fixes stay fixed: the Product's name is its name, and a Fact set in type is the Fact, whole.

I still write the Hook, because the Hook is what I am comparing. The model may say something about my Product only with the Confirmed Facts the Storyboard shows, and it marks which line uses which. If what it writes breaks a rule, the system asks again by itself, twice at most, and then tells me why it failed. It never quietly gives me the mock planner's text instead.

When I ask for a Storyboard I choose its planner, the language model or the mock planner, so that I can post the same Hook both ways and compare them in the Affiliate Lab.

Writing costs about a cent, so I am not asked to confirm it, but it is recorded and counts against the Project's ceiling.

This comes after Hybrid's spoken Narration and before Studio. Most of what the model writes is narration text, and until speech is made from it (hybrid-video ticket 06) nobody hears it. Why this effort exists, and how it fits the larger one, is in `.scratch/cinematic-commercial/notes.md`.

## User Stories

### Writing

1. As a member, I want the narration text of each Scene written for my Product, so that two Products do not get the same sentences.
2. As a member, I want the call to action, and the label of a Solution Scene, written for my Product and its audience within what the layout can show, so that every video does not close on the same line.
3. As a member, I want the lines a layout fixes left as they are (the Product's name, and each Fact whole), so that what is set in type is exactly what I confirmed.
4. As a member, I want the descriptions for generated Stills and Clips written for my Product, so that Hybrid videos of similar Products do not share one backdrop.
5. As a member, I want the creative template to keep deciding the Scenes, their order and their layouts, so that a written Storyboard renders as the template was designed.
6. As a member, I want the text written in the Project's language, so that a Vietnamese video is written in natural Vietnamese.
7. As a member, I want my Hook used exactly as I typed it, on screen and in the narration of its Scene, so that a Variant is still the Hook I chose to test.
8. As a member, I want narration written to fit the time its Scene lasts, so that speech made from it is not rushed and a render does not fail on it.

### Facts

9. As a member, I want the model to say something about my Product only by using a Confirmed Fact the Storyboard shows, so that it invents nothing about what I sell.
10. As a member, I want the narration of any Scene but the Hook's to be able to use a Fact, as long as it cites it, so that the words outside the Facts Scene are about my Product too.
11. As a member, I want each line that uses a Fact to show which Fact, at Storyboard review, so that I can check it.
12. As a member, I want a description of a generated Scene that shows what my Product does to cite a Confirmed Fact, so that a picture never claims something I did not accept.
13. As a member, I want work written from a Fact that is later Withdrawn to be Flagged for Review, as today, so that the model's text is treated like any other.
14. As a member, I want what I typed (the Product's name and category, the audience, the objective, the Hook, the Facts) treated as material to write from and never as instructions, so that no text of mine can change what the model is told to do.
15. As a member, I want the model never to be shown my Product's description, text from an imported page or a Proposed Fact, so that nothing I have not confirmed can reach a video.

### Pictures

16. As a member, I want a description written once for my Project and used by every later writing and every Variant, so that the Stills and Clips I paid for are used again (ADR 0007).
17. As a member, I want asking for a Storyboard to be written again to leave its descriptions alone, so that a rewriting costs a cent and not a Clip.
18. As a member, I want a description that rests on a Fact no longer Confirmed, or no longer shown, written again at the next writing, so that a picture does not outlive what it rested on.

### Review and editing

19. As a member, I want to edit any written text at Storyboard review, as today, so that the model's wording is a draft and not a verdict.
20. As a member, I want to ask for the whole Storyboard to be written again, so that I can see another take without changing my Hook.
21. As a member, I want to ask for one Scene to be written again with the others left as they are, so that I keep the lines I liked.
22. As a member, I want each writing to be a new Storyboard version, with the earlier ones kept, so that I can go back.
23. As a member, I want a Scene I edited to keep the Manually Edited mark until that Scene is written again, as today, so that it stays clear which words are mine.

### When writing goes wrong

24. As a member, I want writing that breaks a rule to be asked for again without me, at most twice, so that an overlong line does not need my attention.
25. As a member, I want a reason I can act on when writing still fails, so that I know whether to shorten a Fact, change the Hook or try again.
26. As a member, I want to be told when the language model cannot be reached, and to try again myself, so that I am never handed the mock planner's text without knowing.
27. As a member, I want to see that writing is under way while I wait, so that a request of half a minute does not look broken.

### Money

28. As a member, I want no confirmation asked for writing, so that I am only asked about costs worth my attention.
29. As a member, I want every writing recorded as Production cost, the rejected ones included, so that the totals are honest.
30. As a member, I want writing to count against the Project's ceiling, and to be refused when the Project has reached it, so that one ceiling covers everything a Project spends.

### Comparing

31. As a member, I want to choose the planner when I ask for a Storyboard, so that I can make the same Hook with both.
32. As a member, I want each Storyboard and Rendered Video to show which planner wrote it, so that I know what I am looking at.
33. As a Lab member, I want to see and filter by planner in the Affiliate Lab, so that I can tell whether written Storyboards perform better than the mock planner's.
34. As a member, I want my existing Variants to keep the Storyboards they have, so that videos I already posted still match their records.

### Honesty about what produced it

35. As a developer, I want the system to run with no language-model credentials, using the mock planner and saying so, so that everything works as before on a machine without keys.
36. As a developer, I want the mock planner kept for tests and labelled as mock, so that tests are deterministic and free.

## Implementation Decisions

### What is written

- The language-model interface is extended, not replaced. For each Scene of the creative template it returns the narration text, the Facts used, the lines of on-screen text that are the model's to write, and a description when the Project has none yet for that Scene.
- The creative template is unchanged in what it fixes: the Scenes, their order, share of the time, layouts, allowed Techniques, text limits and Fact pacing.
- On screen, the system places what the layout fixes: the Hook, the Product's name, and each shown Fact whole, one line each. The model writes only the call to action of a Closing and the label of a Solution, each within the template's limit.
- The Hook Scene's on-screen text and narration text are both the member's Hook, placed whole. The model writes no words for that Scene.
- The Facts shown are chosen as today: the oldest Confirmed Facts in the Project's language, as many as the template shows and the duration gives time to read. The Facts Scene cites exactly those, in that order; the system sets this, not the model.
- The model is told, for each Scene, the most words its narration may have for the time the Scene lasts.

### What the model reads

- The creative template's Scenes and limits, the language, the Product's name and category, the Project's audience and objective, the Hook, and the shown Facts, each with its identifier.
- Never the Product's description or other details, text from an imported page, a Proposed or Withdrawn Fact, or a Confirmed Fact the Storyboard does not show.
- Everything a member typed is passed as data, separate from the instructions, and the instructions say so.

### Facts

- The model is told that the shown Facts are the only statements it may make about the Product. It returns the identifiers each Scene's narration used.
- The narration of any Scene but the Hook's may use a Fact, and then cites it. A Scene that cites a Fact is Flagged for Review when the Fact is Withdrawn, as today.
- Validation rejects a writing that cites a Fact not among those shown, and a description of a Scene that shows what the Product does resting on no Fact.

### Descriptions

- A description is written once for a Project, a creative template and a Scene of that template, and kept (ADR 0007). The model is asked for one only when the Project has none for that Scene and the Scene is to be generated; otherwise the kept one is used as it is.
- The creative template's existing description for the Scene becomes guidance to the model: what the picture is for and where the Product or the text will sit.
- A description is written in English, whatever the Project's language.
- The system, not the model, appends the fixed ending of every description: one for a generated backdrop (no product, no people, no text, no logos) and one for generated footage (invented adults only, no child, no mascot, no product, no text, no logos).
- A kept description that rests on a Fact no longer Confirmed, or no longer among those shown, is written again at the next writing. Stills and Clips made from the old one are kept and Flagged for Review as today.
- A member's edit of a description replaces it for that Scene, as today.
- The mock planner uses the creative template's own description, as today. A Variant written by each planner therefore has pictures of its own, paid for separately.

### Validation and asking again

- A writing is checked against the template before anything is saved: one text per Scene, the text limits of each layout, the Fact rules above, and narration whose estimated length as speech is no longer than its Scene. The estimate is the one spoken Narration uses to warn a member (hybrid-video ticket 06); for the model it is a rule, for a member's own edit it stays a warning.
- The language is not validated. The instructions ask for it, the member sees the text at review, and the manual run checks it.
- A writing that fails is asked for again with the reasons, at most twice. A third failure ends the request with the reasons shown to the member. Nothing is saved from a failed writing except its cost.
- A provider that cannot be reached, or that refuses, ends the request with a reason. The mock planner is never substituted.

### Writing again

- Asking for the Storyboard to be written again writes every Scene's words anew, those marked Manually Edited included, as a new version. The earlier versions are kept.
- Asking for one Scene to be written again asks the model for that Scene alone, with the other Scenes' text given as context, and makes one cost record. It is validated and asked for again like a whole writing. A Hook Scene has no words to write, so regenerating it does what it does today.
- One Scene is written again by the planner that wrote its Storyboard version. A Storyboard version has exactly one planner; changing planner means writing the whole Storyboard.

### The planner

- Claude Sonnet 5.5 (`claude-sonnet-5-5`), asked for structured output that matches the shape above. The model is configuration.
- The credential comes from an environment variable documented in the example file. With one set, both planners exist and the member chooses at Storyboard generation, the language model by default. With none set, no choice is shown and the mock planner writes, labelled as the mock planner's, as today.
- This replaces the rule in the code that a real language model is registered in the mock's place and never beside it.
- A Storyboard records which planner wrote it, and for a real model, which model. It keeps that label however many Scenes a member edits afterwards.

### The request

- Writing stays one synchronous request, with a "writing" state in the browser and a timeout long enough for three calls. If the manual run shows writings regularly passing thirty seconds, it becomes a background job.

### Money

- Writing is not confirmed (ADR 0006). One Production cost record is written for each call to the model, including those whose writing was rejected, from the tokens the provider reports and rates the Organization configures. The mock planner makes no record.
- The amount counts against the Project's ceiling. Before the first call, a configured estimate for one writing is checked against the ceiling, and a writing that would pass it is refused like any other spending.
- Once a writing has started, its automatic re-asks run to the end even if they take the Project slightly past the ceiling, and are recorded.

### Showing the planner

- The Storyboard review, the Library and the download screen show the planner.
- The Affiliate Lab dashboard and the list of Published Posts show the planner and can be filtered by it.
- Existing Storyboards are not rewritten. A Variant gets a written Storyboard only when a member asks for one.

## Testing Decisions

The seams are those of month one, and tests assert only on what a member could observe.

- **The HTTP API with real PostgreSQL, and a scripted language model.** The scripted model returns what a test tells it to, so the tests cover: a valid writing saved as a new Storyboard version; text over a limit asked for again and then accepted; narration too long for its Scene asked for again; three failures ending with reasons and nothing saved; a cited Fact that is not among those shown; an unreachable provider; the Hook kept whole on screen and in narration; Fact lines placed whole; a description written once and used again by a second writing and a second Variant; a description written again after its Fact is Withdrawn; one Scene written again with the others unchanged; the planner chosen per Storyboard, and no choice without a credential; cost records for accepted and rejected writings and how they count against the ceiling; a writing refused at the ceiling; the planner shown and filtered in the Lab; cross-tenant denial.
- **Pure functions.** Validation of a writing against a creative template, and cost from token counts.
- **The mock planner.** Its existing tests stay as they are.
- **The browser.** The existing single flow is unchanged. No browser test is added.
- **The real model.** Not run automatically. One manual run on a real Product, for each creative template, is recorded in this spec before it is marked done, with the founder's verdict on the Vietnamese and how long each writing took.
- No test result is reported as passing unless the test was run.

## Out of Scope

- The model choosing the Scenes or their order, or a video without a creative template. That is a Commercial.
- The model writing or suggesting Hooks.
- The model proposing Facts, choosing which Confirmed Facts to show, or rewording a Fact set in type.
- A member choosing which Facts a Storyboard shows.
- Rewriting the Storyboards of existing Variants.
- A note from the member when asking for another writing.
- A second model that checks the first.
- A mechanical check of the language.
- Writing as a background job.
- Languages other than those a Project already allows.

## Further Notes

- Vocabulary follows `CONTEXT.md`: Storyboard, Planner, Scene, Hook, Fact, Confirmed, Manually Edited, Description, Still, Clip, Production cost.
- A known limit: whether a line that cites no Fact nevertheless says something about the Product cannot be checked mechanically. The instructions forbid it, the member sees at review which lines cite a Fact, and the manual run looks for it. If it happens in practice, a checking pass is the next step.
- The price of a writing was estimated at about a cent and has not been checked against Anthropic's price list. Check it, and the current way to ask for structured output, before building.
- Grilled on 2026-10-12 against the code. That session settled: the Facts are chosen as today, not by the member; Fact lines stay whole; what the model reads; one Scene written alone; the planner chosen per Storyboard; the synchronous request; the ceiling check; the dependency on spoken Narration; descriptions written once for a Project (ADR 0007); the Hook as its Scene's narration; no language validation; the fixed endings and English descriptions; one planner per version; Facts in any Scene's narration but the Hook's; narration length as a rule; and rewriting over Manually Edited Scenes.
