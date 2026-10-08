# 23: Affiliate Lab dashboard

**What to build:** A Lab member compares performance by Product, creative template, Hook and Campaign, and sees Commission, production cost and estimated profit per Product and per Campaign. The dashboard is honest about missing data and small samples.

**Blocked by:** 16, 20, 22

**Status:** ready-for-agent

- [ ] Views, likes, comments, shares and clicks are totalled by Product, creative template, Hook and Campaign from the latest snapshot of each Published Post
- [ ] Click-through rate is shown only for groups where every post has both views and clicks recorded; otherwise it shows as not available
- [ ] Conversion rate is shown only where orders are recorded at a level that matches the group
- [ ] Unknown metrics are shown as unknown, never as zero
- [ ] Groups below a configurable minimum number of posts or views are marked as too small to compare
- [ ] Nothing is labelled a winner or as better than another group
- [ ] Commission, production cost and estimated profit are shown per Product and per Campaign, with estimated profit being Commission minus production cost
- [ ] Every figure shows whether it came from manual entry or CSV import
- [ ] Tests at the HTTP seam check the figures for a seeded set of posts, including the missing-data and small-sample cases
