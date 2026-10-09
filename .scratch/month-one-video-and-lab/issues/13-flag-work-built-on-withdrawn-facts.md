# 13: Flag work built on Withdrawn Facts

**What to build:** When a member withdraws a Fact, every Storyboard version and Rendered Video that used it is marked Flagged for Review. The member sees the flag wherever that work appears and can clear it after reviewing. Nothing is deleted.

**Blocked by:** 09

**Status:** done

- [x] Withdrawing a Fact flags every Storyboard version that recorded using it and every Rendered Video made from those versions
- [x] Flagged work shows which Withdrawn Fact caused the flag and the text it used
- [x] Flagged work is still viewable and downloadable
- [x] A member can clear the flag; clearing records who and when in the audit log
- [x] A new Storyboard generated after the withdrawal does not use the Withdrawn Fact
- [x] The library can be filtered to Flagged for Review

## Comments

- 2026-10-09, implemented. `dotnet test` passed 508 tests, 6 of them new: the API in-process against PostgreSQL and MinIO in containers, with the worker rendering in its own. The web app passed its typecheck and lint. The pages were not opened in a browser: the banner, the "Flagged" on a version's button, the clear button and the library's filter are checked by the typechecker only.

  The tests were written before the code at the HTTP seam, one slice at a time: flags on Storyboard versions, clearing them, then flags, the filter and clearing on Rendered Videos. What came out of the code review (naming the Facts when clearing, reading again after a clearing made at the same moment) was coded first and tested after.

  How each box was checked, all by tests at the HTTP seam:

  - Withdrawing flags the versions and the videos: three versions of one Variant, two of which used the Fact and one rewritten by hand; a Fact changed by replacement; two Withdrawn Facts on one version; a Rendered Video and the version it came from.
  - Which Fact and the text: the Fact's identifier, the text as the Scene kept it, and when it was withdrawn, on the version, on the video, in the lists and one at a time.
  - Still viewable and downloadable: an approved video stays approved, plays and downloads the same bytes after it is flagged. A flagged version renders.
  - Clearing: by an Editor, with `storyboard.flag-cleared` or `rendered-video.flag-cleared` in the audit log under their name; a second clearing refused; twelve at once recorded once; another Organization and someone not signed in refused.
  - A new Storyboard after the withdrawal rests on what is still Confirmed and is not flagged (this was already true and tested in ticket 08).
  - The library: `flagged=true`, `flagged=false`, and together with state and search.

  How it works, which is not what "withdrawing a Fact flags" suggests:

  - Nothing is written when a Fact is withdrawn. Work is Flagged for Review when a Scene's own record of a Fact it used points at a Fact that is Withdrawn and nobody has cleared that flag. Only clearings are stored (`ClearedFlags`: which Fact, on which version or video, by whom, when). So nothing can be missed by a withdrawal and a render happening at the same moment, and Facts withdrawn before this ticket flag their work without a migration of data.

  Things that are mine to have decided and the founder's to overrule:

  - A Manually Edited Scene lists no Facts, so it is never flagged, even when its text still says what the Withdrawn Fact said. That is ticket 12's note, left as the glossary has it. It falls short of story 23 read strictly ("that used a Withdrawn Fact"): a member can add a full stop to a Scene and the version made is beyond the reach of any later withdrawal. Flagging those needs the Facts a Scene was written from kept beside the mark.
  - A version and each video rendered from it carry their own flag, and each is cleared for itself. Clearing a version's flag does not clear its videos'.
  - A video rendered from a version whose Fact is already Withdrawn is flagged from the start, even after the version's flag was cleared.
  - Clearing names the Facts whose flags the member saw, and only those are cleared. A Fact withdrawn after the page was loaded keeps its flag.
  - A Fact withdrawn after a clearing flags the same work again, for that Fact.
  - Flagged work can still be approved and downloaded. Nothing asks for the flag to be cleared first.
  - One audit log entry for one clearing, naming the version or the video. Which Fact's flag it was is in `ClearedFlags`, which is deleted with the video or the Project; the audit log entry then says only that something was cleared.
  - The library can also be narrowed to what is not flagged, which the ticket did not ask for.

  Left for later tickets:

  - 17: no automated test covers the pages. In the Scene editor a flagged version shows no banner, only "Flagged" on its version button. The banner does not say which Scene used the Fact; the Scene's own "Facts used" list does.
  - The Variant page shows the newest render of a version only, so an older flagged video of the same version is seen in the library alone.
  - 19: whether a Rendered Video that is Flagged for Review may be recorded as a Published Post is not decided here.
  - From the code review, not worth the change now: the two queries in `ReviewFlags` differ only in where they start; the two `ClearFlagAsync` and the two endpoints that call them have one shape; `StoryboardFlagClearing` is the third record of the "this or the reason" shape; a `ClearedFlag` names its subject as one of two nullable identifiers, held to exactly one by a check constraint.
