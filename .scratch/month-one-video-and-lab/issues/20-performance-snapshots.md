# 20: Performance Snapshots

**What to build:** A Lab member types in the running totals for a Published Post (views, likes, comments, shares, clicks) with the time they were read. The post shows its current figures and its views over time.

**Blocked by:** 19

**Status:** done

- [x] A Performance Snapshot records running totals, the moment they apply to, and its source as manual entry
- [x] Any metric can be left empty, and empty is stored as unknown, not zero
- [x] The current figure for a Published Post is its latest snapshot
- [x] A wrong figure is corrected by adding a newer snapshot; existing snapshots are never overwritten
- [x] A snapshot with totals lower than the previous one is accepted with a warning
- [x] The Published Post page shows views over time from its snapshots
- [x] Each figure shows its source and when it was recorded

## Comments

- 2026-10-10: Built. Decisions made while building, for the founder to overrule:
  - "Latest" is by the moment the totals apply to, then by when they were entered. A snapshot entered today about last Monday is history, not the current figure; two snapshots about the same moment are settled by the one entered later, which is how a correction for that moment wins.
  - The current figure is the latest snapshot as a whole. If it leaves likes empty, the current likes are unknown, even when an older snapshot knew them.
  - "Lower than the previous one" is told per metric, and only for a metric both snapshots know. The previous snapshot is the latest about an earlier moment, so a correction about the same moment is not warned about. It is worked out when read, not stored, so entering a snapshot about an earlier moment can make the one after it lower.
  - Refused: a snapshot with no figure at all, a figure below zero, and a moment more than five minutes in the future.
  - Views over time is a line from zero with the list of points under it. No chart library was added.
