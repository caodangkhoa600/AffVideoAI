# 17: Create video wizard, dashboard and end-to-end test

**What to build:** A guided flow takes a member from Product, to creative direction, to Storyboard review, to render, to preview, to approve and download, without needing to know the underlying concepts. The home dashboard shows recent Projects, recent Rendered Videos and running jobs, with a quick create button. One browser test covers the whole flow. The work stops here for the founder to try the workflow end to end.

**Blocked by:** 11, 12

**Status:** ready-for-agent

- [ ] The wizard covers: add or pick a Product and photos, Confirm Facts, choose creative template, Hook and duration, review and edit the Storyboard, render, preview, approve and download
- [ ] Leaving the wizard and returning resumes at the same step
- [ ] Every control in the wizard does something real; there are no decorative controls
- [ ] The dashboard shows recent Projects, recent Rendered Videos and jobs in progress
- [ ] A Playwright test runs sign in, create Product, upload image, Confirm Facts, generate Storyboard, render, preview, approve and download, with no paid AI
- [ ] The README documents the demonstration flow
- [ ] The founder has run the flow on their own Product and given feedback
