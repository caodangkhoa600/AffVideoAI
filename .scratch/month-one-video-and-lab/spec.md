# Month one: one-image-to-video workflow and the Affiliate Lab

Status: ready-for-agent

## Problem Statement

I want to earn affiliate commission from short vertical product videos, and later sell the tool that makes them. Today, making each video by hand is slow, AI video tools change how the product looks and invent claims about it, and I have no organised way to tell which product, creative template or Hook actually earns money. I also don't yet know whether a video made from a single product photo, with no paid AI, is good enough to post at all.

## Solution

AffiVideo runs on my own machine and takes me from a Product photo and a few Confirmed Facts to a finished 15–30 second 1080x1920 MP4 that I review, approve and download, with no paid AI service involved. The Product's appearance is never altered (Product Lock), and AI-written text only ever uses Facts I have Confirmed.

With the Affiliate Lab enabled on my Organization, I group Variants into Campaigns, record each Published Post, enter or import its performance and my Commission, and compare Products, creative templates and Hooks on a dashboard that is honest about missing data and small samples.

The month starts with a one-day throwaway render of a real Product so I can judge the look before the rest is built.

## User Stories

### Day-one render test

1. As the founder, I want a throwaway render of a real Product I intend to promote, so that I can judge on day one whether a Product Lock video is good enough to post.
2. As the founder, I want that test to use my real product photos and Vietnamese Facts, so that the judgement reflects real material and not a placeholder.
3. As the founder, I want the work to stop for my verdict after the test render, so that up to a week can be redirected to the look of the videos if it resembles a slideshow.

### Signing in and Organizations

4. As a member, I want to sign in with email and password, so that my work is private.
5. As a member, I want to sign out, so that nobody else at my machine can use my session.
6. As a member, I want my session kept in a secure cookie that scripts cannot read, so that it cannot be stolen by a script on the page.
7. As an Owner, I want everything I create to belong to my Organization, so that it is kept apart from every other Organization.
8. As an Owner, I want a member of another Organization to be refused when they request my Products, assets, Storyboards, Rendered Videos or Lab data by identifier, so that isolation does not depend on the screen hiding things.
9. As an Owner, I want to add an Editor to my Organization, so that someone else can produce videos with me.
10. As an Editor, I want to do all creative work but not manage members or Organization settings, so that responsibilities are clear.
11. As a developer, I want a seeded demonstration Organization with a sample Product, so that I can try the whole flow straight after starting the system.
12. As an Owner, I want sensitive actions (member changes, Fact confirmation and withdrawal, approval, deletion) recorded with who and when, so that I can later see what happened.

### Products and Facts

13. As a member, I want to create a Product with name, category, brand, description, optional price and currency, original URL, optional affiliate URL, target audience and tags, so that everything about it is in one place.
14. As a member, I want to edit and archive a Product, so that my list stays current.
15. As a member, I want to list, search and filter my Products, so that I can find one quickly.
16. As a member, I want to upload several photos and a logo for a Product, so that videos have real material to work from.
17. As a member, I want an upload that is too large, is not really an image, or cannot be decoded to be rejected with a clear reason, so that I know what to fix.
18. As a member, I want to add a Fact to a Product in the language of the video, so that scripts can use it.
19. As a member, I want a new Fact to start as Proposed, so that nothing reaches a script by accident.
20. As a member, I want to Confirm a Fact, with my name and the time recorded, so that it becomes usable in scripts.
21. As a member, I want to record where a Fact came from, so that I can check it again later.
22. As a member, I want to change a Fact by withdrawing it and creating a new Proposed one, so that past videos keep an honest record of what they said.
23. As a member, I want Storyboards and Rendered Videos that used a Withdrawn Fact to be Flagged for Review and kept, so that I can decide what to do about each one.
24. As a member, I want to save a product URL without the system fetching or scraping it, so that I stay within marketplace rules.

### Projects, Variants and Storyboards

25. As a member, I want to create a Project from one Product with audience, language, target duration and objective, so that the brief is fixed once.
26. As a member, I want to add several Variants to a Project, each with a creative template and a Hook, so that I can compare creative ideas.
27. As a member, I want to choose between Luxury Cinematic, Product Showcase and Problem–Solution, so that the three Variants of a Product look and read differently.
28. As a member, I want a Storyboard generated for a Variant without any paid AI, so that the workflow works with no credentials.
29. As a member, I want generated text to use only Confirmed Facts, so that the video never claims something I did not accept.
30. As a member, I want each Scene to show which Facts it used, so that every claim is traceable.
31. As a member, I want generated content to be labelled as coming from the mock planner, so that I am never misled about what produced it.
32. As a member, I want Scene durations to add up to the target duration, so that the video is the length I asked for.
33. As a member, I want a Storyboard that cannot be generated (for example, no Confirmed Facts or no usable image) to fail with a reason, so that I know what is missing.
34. As a member, I want to edit a Scene's on-screen text and narration text, so that I can fix wording.
35. As a member, I want an edited Scene marked Manually Edited, so that it is clear the text is mine and no longer traced to Facts.
36. As a member, I want to change a Scene's source image, its order and its duration within allowed limits, so that I can adjust the cut without a full editor.
37. As a member, I want every edit to produce a new Storyboard version, so that I can see and return to earlier versions.
38. As a member, I want to regenerate a single Scene, so that I do not lose the rest of the Storyboard.
39. As a member, I want to duplicate a Variant with a different Hook, so that setting up a Hook experiment is quick.

### Rendering, preview and export

40. As a member, I want to render a Storyboard version in Product Lock, so that the Product's logo, shape, colours and details are exactly as in my photo.
41. As a member, I want the result to be a real 1080x1920 H.264/AAC MP4 of the target duration, so that it uploads cleanly to short-video platforms.
42. As a member, I want Vietnamese text with full diacritics to render correctly, so that captions are readable and correct.
43. As a member, I want rendering to happen in the background, so that I can keep working.
44. As a member, I want to see the current stage of a job (queued, validating, planning, rendering, ready for review, failed), so that I know what is happening without a made-up percentage.
45. As a member, I want a failed job to show a reason I can act on, so that I can fix the input or retry.
46. As a member, I want to cancel a job that is queued or running, so that I do not wait for something I no longer want.
47. As a member, I want pressing the render button twice to start one job, so that I never get duplicates.
48. As a member, I want a job interrupted by a crash to be picked up again and retried a limited number of times, so that work is neither lost nor repeated forever.
49. As a member, I want only changed Scenes re-rendered after an edit, so that corrections are fast.
50. As a member, I want to preview a Rendered Video in the browser, so that I can review it before approving.
51. As a member, I want to approve a Rendered Video, so that it is marked as fit to publish.
52. As a member, I want to download an approved Rendered Video, so that I can post it by hand.
53. As a member, I want a video with no narration to still play everywhere, so that silence is never a playback problem.
54. As a member, I want to upload my own narration or music for a video and confirm I hold the rights, so that it can have sound without a text-to-speech service.
55. As a member, I want uploaded audio levelled and mixed at a volume I choose, so that it sounds consistent.
56. As a member, I want a library of Rendered Videos I can search, filter, sort, preview, download and delete with confirmation, so that I can manage what I have made.
57. As a member, I want the production cost of each Rendered Video recorded, so that profit can be worked out later.
58. As a member, I want a dashboard showing recent Projects, recent Rendered Videos and running jobs, with a quick create button, so that I can pick up where I left off.

### Create video wizard

59. As a member, I want a guided flow from Product, to creative direction, to Storyboard review, to render, to preview, to approve and download, so that I can make a video without knowing the underlying concepts.
60. As a member, I want to leave the wizard and come back to the same step, so that a long render does not trap me on one page.

### Affiliate Lab

61. As an Owner of an Organization without the Affiliate Lab, I want its pages and data to be unreachable, so that the Lab stays internal for now.
62. As a Lab member, I want to shortlist Products with research notes and the commission rate or amount when known, so that I can choose what to promote.
63. As a Lab member, I want to create a Campaign and add Variants from several Products to it, so that one experiment is tracked together.
64. As a Lab member, I want each Variant to keep a stable identifier, so that results stay attached to the right creative idea.
65. As a Lab member, I want to record a Published Post for an approved Rendered Video with platform, account, date, URL and affiliate link, so that I know exactly what is live where.
66. As a Lab member, I want the same Rendered Video on three accounts to be three Published Posts, so that each post's results are separate.
67. As a Lab member, I want to be stopped from recording a Published Post for a video that is not approved, so that only reviewed videos are tracked as live.
68. As a Lab member, I want to enter a Performance Snapshot by hand (views, likes, comments, shares, clicks) as running totals with the time and source, so that I can track a post without any platform integration.
69. As a Lab member, I want to import Performance Snapshots from a CSV file in a documented format, so that I can update many posts at once.
70. As a Lab member, I want a CSV import to report which rows were accepted and which were rejected and why, so that I can correct the file.
71. As a Lab member, I want a wrong figure corrected by adding a newer snapshot, so that the history stays intact.
72. As a Lab member, I want to record Commission, orders, confirmed orders and refunds at the level my affiliate report gives them (a link or a Product), so that nothing is attributed more precisely than the data allows.
73. As a Lab member, I want Commission shown per Published Post only when that post has its own affiliate link, so that I never see invented attribution.
74. As a Lab member, I want to see performance by Product, by creative template, by Hook and by Campaign, so that I can see what works.
75. As a Lab member, I want views over time for a Published Post, so that I can see how it grew.
76. As a Lab member, I want click-through and conversion rates shown only when the underlying click and order data exists, so that I am not shown a rate built on nothing.
77. As a Lab member, I want each figure labelled with its source (typed in by hand or imported), so that I know how far to trust it.
78. As a Lab member, I want small samples marked as such, with no Variant declared a winner on a slight difference, so that I do not draw false conclusions.
79. As a Lab member, I want Commission, production cost and estimated profit per Product and per Campaign, so that I can see whether the experiment pays.
80. As the founder, I want a written plan for a 10-Product, 30-Variant experiment, so that I can run it with consistent rules, including one affiliate link per Published Post where the programme allows.

### Running the system

81. As a developer, I want one command to start the database, object storage, API, worker and web app locally, so that setup is trivial.
82. As a developer, I want database changes applied through explicit migrations, so that the schema is never changed destructively at startup.
83. As a developer, I want every environment variable documented in an example file with no secrets committed, so that configuration is clear and safe.
84. As a developer, I want health checks on every service, so that I can tell what is down.
85. As a developer, I want the web app's API types generated from the API's published description, so that the two cannot drift apart.

## Implementation Decisions

### Shape of the system

- One repository rooted at this directory, named AffiVideo in code. Three deployables: a Next.js web app, an ASP.NET Core API on .NET 10, and a .NET Worker Service. They share domain, application, infrastructure and contracts libraries. It is a modular monolith; no microservices.
- PostgreSQL through Entity Framework Core with explicit migrations. S3-compatible object storage, MinIO locally. Everything runs through Docker Compose on the developer's machine; there is no server deployment this month.
- FFmpeg, ffprobe, Node, Remotion and its Chrome Headless Shell exist only inside the worker's container image. Nothing depends on them being installed on the host.
- Web stack: App Router, strict TypeScript, Tailwind, shadcn/ui, TanStack Query, React Hook Form and Zod. Light mode only. UI copy is English; video content is Vietnamese.
- The API is REST under a versioned prefix, documents itself with OpenAPI, returns problem-details errors, and supports pagination and filtering on list endpoints. The web app's client types are generated from the OpenAPI description.

### Day-one render test

- A throwaway script outside the application renders one real Product to MP4 with FFmpeg. It is a prototype: judged by eye, then discarded, with only the learning carried forward. The look work that followed it (tickets 25 and 26) changed how Scenes are drawn; see Rendering.
- If the founder judges the look too weak, up to a week goes to background cut-out, generated backgrounds and motion-graphics templates before the foundation, and the Affiliate Lab scope shrinks to fit. A paid image-to-video provider is the fallback only if that week does not get there.

### Identity and tenancy

- Cookie-based sessions using the platform's built-in identity system, with HttpOnly, Secure, SameSite cookies and anti-forgery protection on state-changing requests. The web app reaches the API through its own origin so cookies are same-site. No tokens in browser storage.
- Roles are Owner and Editor. Sign-up is closed: accounts come from seed data or from an Owner adding a member.
- Every tenant-owned record carries its Organization. Isolation is enforced in the data-access layer for all queries and checked again on every write, and never relies on the web app filtering.
- Object storage keys are namespaced per Organization. Files are only reachable through the API, which authorises the request and then streams the file or issues a short-lived link.
- The Affiliate Lab is a flag on an Organization (ADR 0001). Lab endpoints refuse Organizations without the flag.
- An audit log records sensitive actions with actor, Organization, action and time.

### Products and Facts

- A Fact has text, language, source, a state of Proposed, Confirmed or Withdrawn, and who confirmed it and when. Its text is immutable; changing a Fact withdraws it and creates a new Proposed Fact.
- A Storyboard stores a copy of the text of each Fact it used along with the Fact's identifier. Withdrawing a Fact marks every Storyboard version and Rendered Video that used it as Flagged for Review.
- Product URLs are stored as text only. Nothing fetches them this month, so there is no scraping and no server-side request to user-supplied addresses. An import-provider interface is defined with no implementation.
- Uploads are checked by decoding the content rather than trusting the file name or declared type, are limited in size and dimensions, and are re-encoded to a normalised image before use.

### Creative planning

- Provider interfaces exist for the language model, video generation, text-to-speech, image processing, rendering, object storage, publishing and analytics import. This month only the mock language model, the local renderer and object storage have implementations.
- The mock planner is deterministic: for a given Product, Confirmed Facts, creative template, Hook and duration it always produces the same Storyboard, by slotting Fact text into Vietnamese sentence patterns belonging to the creative template. It does no translation. Its output is labelled as mock in the data and on screen. There is no silent fallback from a real provider to the mock.
- A creative template defines the Scene structure, the proportion of the duration each Scene gets, and a layout, typography and motion for each Scene. Its look is a set of Remotion components (ADR 0002), with a different layout in each Scene. Three exist: Luxury Cinematic, Product Showcase and Problem–Solution. Templates and prompt texts are versioned.
- The planning engine is a rules-based function from (available assets, creative template, duration, Render Mode) to a Technique per Scene. In Product Lock it may only choose static image, image motion and text animation. Hybrid exists as a value but selects nothing generative until a real provider is added.
- A Storyboard version is immutable. Any edit, reorder, duration change or Scene regeneration creates the next version. Validation rejects a Storyboard whose Scene durations do not sum to the target, that references an asset the Product does not have, or whose generated text cites a Fact that is not Confirmed.
- Editing a Scene's text sets Manually Edited on that Scene. Manually Edited text is not checked against Facts.
- Product descriptions and any other free text are passed to planners as data, never as instructions.

### Rendering

- Output is 1080x1920, H.264 video, AAC audio, MP4, 15–30 seconds, in BT.709 colour and tagged as such.
- Remotion draws each Scene and FFmpeg joins the Scenes, adds audio and probes the result (ADR 0002). Each Scene is rendered to an intermediate file, cached under a key derived from everything that affects it, so only changed Scenes are re-rendered. The Product carries on from where the previous Scene left it, so that key includes the pose the Scene starts from.
- The Product is cut out of its photo by a model that runs locally in the worker, with no paid service, and stands on a soft shadow on the light studio backdrop the founder chose. A cut-out that fails its checks is not used; the photo goes uncut onto a card instead. Only the photo's transparency is decided: the Product's own pixels are never repainted, and a template may only scale, move, rotate and fade it.
- The Hook is fully on screen within the first two seconds.
- Remotion and FFmpeg are each started with an argument list built by the application, with no shell. User-supplied text reaches a template as input data, never as code, and is never interpolated into a command line or a filter expression without escaping. Members cannot supply template code.
- A typeface with full Vietnamese diacritic coverage and an open licence is bundled in the worker image.
- With no audio supplied, the video carries a silent audio track. A member may attach an uploaded narration and an uploaded music track after attesting to holding the rights; audio is loudness-normalised and mixed at a chosen music volume.
- Temporary files are removed when a job ends, whether it succeeds, fails or is cancelled.
- A Rendered Video belongs to exactly one Storyboard version and moves from ready for review to approved. Preview is available before approval; download and recording a Published Post require approval.
- Each render writes a cost record with provider, technique counts, duration, attempts and an estimated amount in an explicit currency, using rates held in configuration rather than in code. Local rendering uses a configurable rate that defaults to zero.

### Background jobs

- The queue is a PostgreSQL table. A worker claims a job by taking a time-limited lease in a way that lets several workers run without taking the same job, renews the lease while working, and a job whose lease expires is returned to the queue.
- Job states: created, queued, validating, planning, generating assets, generating video, rendering, quality review, completed, failed, cancelled. Only defined transitions are allowed. Progress shown to the user is the current state.
- Submitting a render carries an idempotency key, so a repeated submission returns the existing job.
- Retries are limited in number with exponential backoff. Failures store structured details: stage, a category, a human-readable message and the technical detail.
- Cancellation is cooperative: a cancelled job stops at the next stage boundary or kills the running FFmpeg process.
- Worker concurrency is bounded by configuration. The web app polls for job state; there are no web sockets.

### Affiliate Lab

- A Campaign references Variants and does not own them. A Variant keeps its identifier for life.
- A Published Post is one approved Rendered Video on one account at one URL, with platform, account, publication date and an optional affiliate link. Platform accounts and affiliate links are records of their own so they can be reused and reported on.
- A Performance Snapshot holds running totals for one Published Post at one moment from one named source (manual or CSV import). The current figure for a post is its latest snapshot; history is never overwritten.
- Commission records hold orders, confirmed orders, commission and refunds for a period, attached to an affiliate link or to a Product, with source and currency. A Published Post shows a commission figure only when its affiliate link is used by no other post. Otherwise the figure appears only at link or Product level.
- CSV import accepts a documented column layout, validates every row, applies valid rows, and returns a per-row report of what was rejected and why. Re-importing the same file does not create duplicates.
- The dashboard aggregates by Product, creative template, Hook and Campaign. A rate is shown only when both its numerator and denominator come from recorded data. Groups below a configurable minimum sample are marked as too small to compare, and nothing is labelled a winner. Estimated profit is Commission minus recorded production cost, at Product and Campaign level.
- Publishing is manual. A publishing-provider interface is defined with no implementation.

### Documentation kept alongside the code

- A README with working start-up, migration and test commands; an example environment file; and a running record of implementation status, decisions and next steps.
- The affiliate experiment plan for 10 Products and 30 Variants.

## Testing Decisions

A good test here drives the system the way a member does and asserts on what a member could observe: an HTTP response, a stored file, a row visible through the API, the properties of an MP4. Tests do not assert on internal classes, queue rows or FFmpeg arguments, so the internals can be reworked freely.

- **Primary seam: the HTTP API, with the worker running against real PostgreSQL and real object storage in containers.** Almost everything is tested here: sign-in, Product and Fact lifecycle, Storyboard generation and editing, submitting a render and waiting for it to finish, preview and download, approval, and all Affiliate Lab behaviour including CSV import and dashboard figures. Cross-tenant denial is tested here for every kind of tenant-owned resource, including files, by attempting access as a member of a second Organization. Job behaviour (duplicate submission, cancellation, retry after a simulated crash, recovery of an expired lease) is tested here through job state as the API reports it.
- **The Rendered Video is asserted through the same seam.** A test downloads the MP4 the system produced and inspects it with ffprobe: it exists, is playable, is 1080x1920, has H.264 video and an AAC track, and its duration is within a small tolerance of the target. A further check confirms no temporary files remain after the job. These tests run inside a container that has FFmpeg.
- **Second seam: the planning and validation functions, as pure functions.** Technique selection per Render Mode, Scene duration allocation, Storyboard validation, Fact state transitions, job state transitions and cost calculation have many small cases that are cheaper to cover directly than through HTTP. They are tested by input and output only.
- **Third seam: the browser, for one flow only.** A single Playwright test covers sign in, create Product, upload image, Confirm Facts, generate Storyboard, render, preview, approve and download, using the mock planner.
- Migrations are tested by applying them to an empty database as part of the integration run.
- There is no prior art; the repository is empty. The first tests written at the HTTP seam set the pattern for the rest.
- No test result is reported as passing unless the test was run.

## Out of Scope

- Real language-model, image-to-video and text-to-speech providers. Their interfaces exist; only mocks or no implementation stand behind them.
- Hybrid rendering that actually uses a generative Technique, and any 3D upload, preview or rendering.
- Credits, plans, subscriptions, the usage ledger, usage limits and payments.
- Landing page, pricing page, public sign-up, onboarding, and the Admin and Viewer roles.
- Feature Explainer, Minimal Commerce and Comparison creative templates.
- Bundled music, automatic subtitles timed to speech, and voice selection.
- Importing product details from a URL, and any scraping.
- Publishing to social platforms and importing analytics from platform APIs.
- Deployment to a server, a production reverse proxy, and web sockets.
- Languages other than Vietnamese for video content.
- Business documents other than the affiliate experiment plan.
- CSV import of Performance Snapshots, and charts, production cost and estimated profit on the Affiliate Lab dashboard. These were removed after the day-one verdict to make room for work on the look.
- A full timeline video editor.

## Further Notes

- Vocabulary follows `CONTEXT.md`. In particular: Fact states are Proposed, Confirmed and Withdrawn; a Variant is a creative template plus a Hook; Render Mode is Product Lock or Hybrid and Technique is per Scene.
- The work stops for the founder at two points: after the day-one render test, and when the one-image-to-MP4 workflow runs end to end.
- The day-one test needs two or three photos of a real Product and its name and three or four Facts in Vietnamese, supplied by the founder. AirBeat X1 remains as fictional seed and test data.
- Second verdict, 2026-10-08, on the ticket 26 render: good enough to post. Scenes are drawn with Remotion (ADR 0002). Remotion is free for a company of up to 3 people; beyond that it costs $0.01 per render with a $100 a month minimum.
- Day-one verdict, 2026-10-08: improve the look. Up to a week goes to product cut-out, designed backgrounds, and motion, layout and type, before the foundation. User stories 69, 70 and 79 and the parts of the Affiliate Lab decisions that describe CSV import and estimated profit are deferred.
- Two details were decided while writing this spec and not discussed beforehand: download requires approval (preview does not), and the application's interface is in English while video content is Vietnamese.
