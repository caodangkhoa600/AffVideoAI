# 20: Performance Snapshots

**What to build:** A Lab member types in the running totals for a Published Post (views, likes, comments, shares, clicks) with the time they were read. The post shows its current figures and its views over time.

**Blocked by:** 19

**Status:** ready-for-agent

- [ ] A Performance Snapshot records running totals, the moment they apply to, and its source as manual entry
- [ ] Any metric can be left empty, and empty is stored as unknown, not zero
- [ ] The current figure for a Published Post is its latest snapshot
- [ ] A wrong figure is corrected by adding a newer snapshot; existing snapshots are never overwritten
- [ ] A snapshot with totals lower than the previous one is accepted with a warning
- [ ] The Published Post page shows views over time from its snapshots
- [ ] Each figure shows its source and when it was recorded
