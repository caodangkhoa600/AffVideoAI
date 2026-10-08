# 12: Storyboard editing

**What to build:** A member edits a Storyboard in a basic Scene editor: change on-screen text and narration text, swap the source image, reorder Scenes, adjust a Scene's duration within limits, and regenerate a single Scene. Every change makes a new Storyboard version, and re-rendering only redoes the Scenes that changed.

**Blocked by:** 09

**Status:** ready-for-agent

- [ ] Any edit creates the next Storyboard version; earlier versions stay viewable and unchanged
- [ ] A Scene whose text was changed by a person is marked Manually Edited, and the mark is visible
- [ ] Manually Edited text is not checked against Facts
- [ ] A change that breaks the total duration or references a missing asset is rejected with a reason
- [ ] Regenerating one Scene leaves the other Scenes untouched
- [ ] Rendering a new version reuses the intermediate clips of unchanged Scenes, shown by a test in which only the changed Scene is rendered again
- [ ] Each Rendered Video shows which Storyboard version it came from
