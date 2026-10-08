# 22: Commission records

**What to build:** A Lab member records what the affiliate report says for a period: orders, confirmed orders, Commission and refunds, attached to an affiliate link or to a Product. Commission appears per Published Post only when that post has an affiliate link no other post uses.

**Blocked by:** 19

**Status:** ready-for-agent

- [ ] A Commission record has a period, a source, a currency, and is attached to exactly one affiliate link or one Product
- [ ] Amounts are stored as decimals with an explicit currency
- [ ] Refunds and adjustments can be recorded and reduce the net figure
- [ ] A Published Post shows Commission only when its affiliate link is used by no other Published Post
- [ ] When a link is shared, Commission is shown at link and Product level with a note that it cannot be split by post
- [ ] Commission is never divided between Published Posts by views or clicks
- [ ] Tests cover both the unique-link and shared-link cases
