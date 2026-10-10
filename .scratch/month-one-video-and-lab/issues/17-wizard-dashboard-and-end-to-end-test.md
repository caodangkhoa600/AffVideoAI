# 17: Create video wizard, dashboard and end-to-end test

**What to build:** A guided flow takes a member from Product, to creative direction, to Storyboard review, to render, to preview, to approve and download, without needing to know the underlying concepts. The home dashboard shows recent Projects, recent Rendered Videos and running jobs, with a quick create button. One browser test covers the whole flow. The work stops here for the founder to try the workflow end to end.

**Blocked by:** 11, 12

**Status:** ready-for-human

- [x] The wizard covers: add or pick a Product and photos, Confirm Facts, choose creative template, Hook and duration, review and edit the Storyboard, render, preview, approve and download
- [x] Leaving the wizard and returning resumes at the same step
- [x] Every control in the wizard does something real; there are no decorative controls
- [x] The dashboard shows recent Projects, recent Rendered Videos and jobs in progress
- [x] A Playwright test runs sign in, create Product, upload image, Confirm Facts, generate Storyboard, render, preview, approve and download, with no paid AI
- [x] The README documents the demonstration flow
- [ ] The founder has run the flow on their own Product and given feedback

## Comments

- 2026-10-10, implemented, all but the last box, which is the founder's: run the flow in the README ("Create a video: the demonstration flow") on your own Product and say what you found. The status is `ready-for-human` for that.

  `dotnet test` passed 610 tests, up from 608, with the worker rendering in its own container. The Playwright test passed twice, against all five services started by Compose as a project of their own (`affivideo-e2e`, on other ports, since removed), the second time after the changes from the review; a render took about a minute. The web app passed its typecheck, its lint and the check that its API types are current. I looked at screenshots of the dashboard and of the Product, Creative direction and Video steps; nobody has watched a video through the wizard in a browser that plays H.264.

  The test of the new endpoint was written before the endpoint, and first ran after it, so it was never seen to fail. The Playwright test was written after the pages and passed the first time it ran.

  What was built:

  - `/create`, the wizard, in six steps: Product, Photos, Facts, Creative direction, Storyboard, Video. It keeps nothing of its own in the database. Each step is the component the Product's and the Variant's pages already use, on the same records, so the Variant's page was split to share it (`storyboard-versions.tsx`).
  - Where the member is, is in the address (the step and the identifiers of the Product, Project and Variant) and is remembered in the browser's local storage for each member, which is how "Create video" finds its way back.
  - Choosing the creative direction makes the Project and the Variant. The step asks for the audience and the objective too, because a Project cannot be made without them; the audience starts as the Product's.
  - The home page is the dashboard: "Create video", jobs in progress, the five newest Rendered Videos and the five newest Projects.
  - `GET /api/v1/render-jobs/in-progress`: the Organization's render jobs that have not ended, newest first, each with the Product, Variant and Storyboard version it renders. No migration.
  - `web/e2e/create-video.spec.ts`, run with `npm run e2e` against a running system. It draws its own photo, so it needs no file beside it.

  How each box was checked:

  - The wizard covers every step: the Playwright test goes through all of them by the wizard's own controls, adding a Product. Picking an existing Product, editing a Scene and regenerating one are the shared components and were not driven in the wizard.
  - Leaving and returning: the test goes to the dashboard from the Video step, presses "Create video" and is back at the same address. A throwaway check also opened an address with the Project but not the Product and got the right step.
  - No decorative controls: by reading, and by the review. The numbered steps are buttons that lead to their step; the current one is not a button.
  - The dashboard: the test finds its job under "Jobs in progress" while the worker renders, its Project under "Recent Projects", and afterwards its video, approved, with the job gone. The endpoint has two tests at the HTTP seam: what is listed, in what order, a page of it, another Organization seeing none, nobody without a session, a cancelled job gone; and a completed job gone.
  - The Playwright test: run, as above. "Preview" is checked by reading the file the player points at and finding an MP4, not by playing it: the Chromium that Playwright drives may not decode H.264.
  - The README: the flow, how to run the test, and notes under Status.

  From the code review, which ran on both halves:

  - A Hook the creative template's layout cannot hold left the wizard with nowhere to go: the Variant was made, generating was refused, and Next stayed shut. The wizard now holds a Hook to 60 characters, and while there is no Storyboard the step offers "Use another Hook", which duplicates the Variant with the new Hook and carries on with it. Checked by a throwaway browser test with a word too long for its line.
  - The numbered steps let a member reach Creative direction before a photo or a Confirmed Fact. They now lead ahead only once the direction is chosen.
  - After a refused Hook, going back in the browser and submitting again would have made a second Project. The address is now replaced, not added to.
  - An address with the Project but not the Product showed "Loading…" for good. The Product is now the Project's, whatever the address says.
  - The dashboard stopped asking for jobs once there were none, and missed a video when one job ended as another began. It now asks every ten seconds while idle and looks again whenever the jobs are not the ones they were.
  - Wording brought to the glossary: "fit to publish", Hook, photo. The Project's page now uses the shared `useProject`.

  Left as it is:

  - "Create video" always resumes. After a video is approved and downloaded it still leads back to that video's last step until "Start another video" is pressed. Whether it should start afresh is a question for the founder's run.
  - The position is remembered in one browser. Another browser starts at the first step; the work is on its own pages.
  - Using another Hook leaves the Variant with the refused Hook on the Project, since nothing removes a Variant. "Start another video" after choosing a direction likewise leaves that Project.
  - The wizard has no step for narration and music. The Video step links to the Variant's page, where they are added before rendering.
  - Next is not held back for a photo under 400 pixels or a Confirmed Fact in English only; generating the Storyboard says so.
  - The wizard's form repeats some of the rules of the New Project and Variant forms (the duration, the Products list), and the dashboard's three lists share a shape that was not pulled out.
  - The joins from a job to its Product repeat those of the library's query.
  - The Playwright test is not part of `dotnet test` and needs the system started first; nothing runs it automatically.
  - The spec says "No tokens in browser storage". What is stored is identifiers and a step name; the README's sentence was narrowed from "nothing is kept in browser storage" to say so.
