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
- 2026-10-10: Rebased onto ticket 27 and brought in line with what the founder decided when ticket 22 was reviewed:
  - A Product row shows two Commission figures, for its links and for the Product, and never one total of both.
  - Conversion is orders for each click from affiliate links alone. Orders recorded for a Product no longer make a rate, because they include sales no click here led to. It is orders, not confirmed orders.
  - Records from before a link's first Published Post count in Commission, are marked, and take the group's conversion rate away.
  - Campaign Commission is of the links whose every Published Post is of a Variant in the Campaign. The page says not to add two Campaigns together, since a Variant can be in both.
  - "Too small to compare" is on the whole row, Commission included. A row whose views are not all known is too small.
  - A figure's source is how its snapshots got in and the moments they apply to, and for Commission the report and the source. It was a list of record identifiers in a tooltip.
  - A Product is listed only when it has a Published Post or a Commission record.
  - The tests read one seeded set of seven Published Posts of three Products, and cover missing data, small samples, both Commission levels, a link shared across Products, a Variant in two Campaigns, and records from before publication.
