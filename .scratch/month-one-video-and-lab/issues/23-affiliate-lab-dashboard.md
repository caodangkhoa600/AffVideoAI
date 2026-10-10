# 23: Affiliate Lab dashboard

**What to build:** A Lab member compares performance by Product, creative template, Hook and Campaign, and sees Commission per Product and per Campaign. It is a set of plain tables, with no charts. The dashboard is honest about missing data and small samples.

**Blocked by:** 20, 22

**Status:** done

- [x] Views, likes, comments, shares and clicks are totalled by Product, creative template, Hook and Campaign from the latest snapshot of each Published Post
- [x] Click-through rate is shown only for groups where every post has both views and clicks recorded; otherwise it shows as not available
- [x] Conversion rate is shown only where orders are recorded at a level that matches the group
- [x] Unknown metrics are shown as unknown, never as zero
- [x] Groups below a configurable minimum number of posts or views are marked as too small to compare
- [x] Nothing is labelled a winner or as better than another group
- [x] Commission is shown per Product and per Campaign
- [x] Every figure shows its source
- [x] Tests at the HTTP seam check the figures for a seeded set of posts, including the missing-data and small-sample cases

## Comments

- 2026-10-08: Reduced to make room for the look work (tickets 25 and 26): tables only, and production cost and estimated profit are left out because every video this month is rendered locally at a cost of zero.
- 2026-10-10: Implemented in commits `5db361f` and `1c4d93b`. The Lab dashboard shows tables by Product, creative template, Hook and Campaign, with source details, unknown values and configurable small-sample thresholds. Product-level Commission stays at Product level; Campaign totals use only affiliate-link records attributable to that Campaign. Docker-backed dashboard HTTP tests passed 2/2; .NET build, web typecheck and targeted lint passed.
