# 14: Luxury Cinematic and Problem–Solution templates

**What to build:** A member can choose Luxury Cinematic or Problem–Solution for a Variant and get a Storyboard and Rendered Video that look and read clearly different from Product Showcase: their own Scene structure, Vietnamese sentence patterns, layouts, typography and motion. Each is a set of Remotion components beside Product Showcase (ADR 0002).

**Blocked by:** 09

**Status:** done

- [x] Both creative templates can be selected when creating a Variant
- [x] Each produces a Storyboard with its own Scene structure and wording from the same Confirmed Facts
- [x] Each has a different layout in each Scene, and shows the Hook fully within the first two seconds
- [x] Each only scales, moves, rotates and fades the Product
- [x] Luxury Cinematic uses restrained motion and little on-screen text
- [x] Problem–Solution opens with a customer problem and uses only Confirmed Facts for the solution
- [x] Each renders to a valid MP4 that passes the same ffprobe checks as Product Showcase
- [x] Creative templates are versioned, and a Storyboard records the template version it used
- [x] Three Variants of one Product, one per template, are visibly different when rendered

## Comments

- 2026-10-10, implemented. `dotnet test` passed 525 tests, up from 508, with the worker rendering in its own container. The Remotion project and the web app passed their typechecks, and the web app its lint. Nobody has watched the videos play: frames of each were looked at, at rest and in the middle of moves, and whether they are good enough to post is for the founder's eye, as in ticket 26.

  The planning tests were written at the HTTP seam before the code. The render tests were written after the layouts had been drawn and looked at as stills, since what they measure (which rows the Hook is set in) is decided by the layout.

  What each creative template is:

  - Luxury Cinematic: three Scenes, Hook, Facts and Closing, as 6 : 8 : 6. The Product is lit out of the dark with the Hook small under it; then seen close on its own deep colour over one or two Facts; then set on a pale plinth over its name and "Khám phá ngay". Type is the Light weight of Be Vietnam Pro, added to the bundle, and every word fades in. Nothing slides, pops or overshoots.
  - Problem–Solution: four Scenes, Hook, Solution, Facts and Closing, as 4 : 4 : 7 : 5. The Hook is the problem in white capitals on a dark frame, with no Product. The studio backdrop pushes it away and the Product comes up under a "Giải pháp" tag and its name. The Facts are ticked off one under another and stay. The closing is the Product's colour edge to edge with "Xem giải pháp ngay" in a white pill.
  - Solution is a new layout (`SceneLayout.Solution`) with two lines, a label and the Product's name. Only Problem–Solution has it.

  How each box was checked:

  - Selected when creating a Variant: the web app already offered all three; what changed is that generating and rendering no longer refuse two of them.
  - Own Scene structure and wording from the same Facts: tests at the HTTP seam for each template, and one that plans all three for one Product and finds no two with the same structure or closing words.
  - A different layout in each Scene, and the Hook within two seconds: the rendered frames late in each Scene differ from one another by far more than a tenth of their pixels, and the Hook's rows at two seconds are what they are late in its Scene. Luxury Cinematic's words fade in closer together the more there are, so the last has arrived at 1.9 seconds whatever the Hook's length; Problem–Solution keeps the limit of 13 words, the last in place at 1.92.
  - Only scales, moves, rotates and fades the Product: both templates draw it only through the shared `Product` component, which does nothing else. Not tested beyond that.
  - Restrained motion and little text: fewer Scenes, two Facts at most, 90 characters a Fact, tested as limits. That the motion is restrained is by reading the code and the frames.
  - Opens with a problem, only Confirmed Facts for the solution: the Hook's Scene is planned as text animation; a Proposed Fact stays out; the patterns around the Facts are a label and a call to action and say nothing about the Product.
  - Valid MP4: the worker keeps a file only after the same ffprobe checks, and all three completed.
  - Versioned: each definition has a version, recorded on the Storyboard and kept through an edit and a regeneration, tested for both new templates. The Remotion components are not kept by version: a Storyboard is drawn with the components the worker has now.
  - Visibly different: three Variants of one Product rendered, and frames from the opening, the middle and the closing compared between every pair.

  From the code review: the Problem–Solution Hook fades out over its Scene's last 0.2 seconds, so its Scene's shortest duration went from 2 to 2.5 seconds; a word from the glossary used with another meaning was replaced; the mock planner now refuses a creative template it has no call to action for, where it would have used Product Showcase's. The standards half of the review ran; the spec half stopped on a usage limit, and the boxes above were checked by hand in its place.

  Left as it is:

  - The Scene editor offers a third line in any Facts Scene. Luxury Cinematic holds two, and the API refuses the third with the reason.
  - A grey Product gives Luxury Cinematic a dark Hook and a nearly as dark Facts Scene. The layouts differ; the colours barely do.
  - Long text was fitted by calculation, not rendered: the stills and tests used a Hook of 60 characters and short names and Facts.
