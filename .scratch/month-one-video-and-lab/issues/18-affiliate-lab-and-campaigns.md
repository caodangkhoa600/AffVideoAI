# 18: Affiliate Lab flag and Campaigns

**What to build:** An Organization with the Affiliate Lab enabled gets a Lab area. There a member keeps a shortlist of Products with research notes and the commission rate or amount when known, and creates Campaigns that group Variants from several Products. An Organization without the flag cannot reach any of it. See ADR 0001.

**Blocked by:** 07

**Status:** done

- [x] The flag is set per Organization and is on for the demonstration Organization
- [x] Lab pages are hidden and Lab endpoints refuse an Organization without the flag
- [x] A Product can carry Lab research notes and a commission rate or amount; these use the shared Product, not a separate table of products
- [x] A Campaign can be created, renamed and archived
- [x] Variants from different Products can be added to and removed from a Campaign
- [x] Removing a Variant from a Campaign, or archiving a Campaign, does not delete the Variant
- [x] A member of another Lab-enabled Organization is refused the Campaign by identifier
