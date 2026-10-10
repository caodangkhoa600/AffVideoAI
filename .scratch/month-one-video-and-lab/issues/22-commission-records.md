# 22: Commission records

**What to build:** A Lab member records what the affiliate report says for a period: orders, confirmed orders, Commission and refunds, attached to an affiliate link or to a Product. Commission appears per Published Post only when that post has an affiliate link no other post uses.

**Blocked by:** 19

**Status:** done

- [x] A Commission record has a period, a source, a currency, and is attached to exactly one affiliate link or one Product
- [x] Amounts are stored as decimals with an explicit currency
- [x] Refunds and adjustments can be recorded and reduce the net figure
- [x] A Published Post shows Commission only when its affiliate link is used by no other Published Post
- [x] When a link is shared, Commission is shown at link and Product level with a note that it cannot be split by post
- [x] Commission is never divided between Published Posts by views or clicks
- [x] Tests cover both the unique-link and shared-link cases

## Comments

- 2026-10-10: Built. Decisions made while building, for the founder to overrule:
  - The source is free text: the report as the member names it ("Shopee Affiliate"). It is not a fixed list.
  - Commission is required and may be zero. Refunds and adjustments are amounts of zero or more that are taken off; an adjustment that adds Commission cannot be recorded. Orders and confirmed orders may be left empty, which is unknown.
  - A record is not edited. A wrong one is deleted and recorded again, and the deletion is in the audit log. This differs from Performance Snapshots, where a newer one wins: a record with the wrong period or the wrong link has nothing newer to be replaced by.
  - The same source, currency and period for the same link or Product is refused, so a report is not counted twice. Periods that only overlap are accepted without a warning.
  - Product level is the records attached to the Product plus those of every affiliate link that only Published Posts of that Product carry. A link carried by posts of two Products counts for neither, and one no post carries counts for none. An amount recorded for both a link and its Product is counted twice.
  - A Published Post with a link of its own and nothing recorded shows "no Commission recorded yet"; one with a shared link or no link shows no figure at all.
  - The pages were typechecked and linted but not clicked through in a browser.
