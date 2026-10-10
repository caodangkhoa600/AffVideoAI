# 27: Commission record corrections

**What to build:** The decisions the founder made after ticket 22 was reviewed, applied to Commission records before the dashboard (ticket 23) reads them. Nothing here may attribute Commission more precisely than the report it came from.

**Blocked by:** 22

**Status:** done

- [x] An adjustment is signed: a negative one takes Commission away and a positive one adds it. Net Commission is Commission less refunds, plus adjustments
- [x] A Product shows two figures, what is recorded for the Product and what is recorded for its affiliate links, and never one total of both
- [x] A Published Post with an affiliate link of its own shows everything recorded for the link, and says how many of those records cover days before it was published
- [x] Records for the same affiliate link or Product, in the same currency, whose periods overlap are accepted and flagged wherever they are shown
- [x] An affiliate link carried by Published Posts of two Products is shown for the link only, and the page says so
- [x] The name of the report a record came from is called its report. Its source is how the figures got in, which is manual entry
- [x] The pages, the README and the test names say "Published Post", not "post"
- [x] A refused amount is not called a price
- [x] Tests cover the double count that is no longer made, a Product-level record staying off a Published Post, and the overlap flag

## Comments

- 2026-10-10: Written from the review of ticket 22 and the grilling that followed it. Correcting a record stays as it is: delete it, which is audited, and record it again.
- 2026-10-10: Built. Decisions made while building, for the founder to overrule:
  - Two records overlap when they are for the same affiliate link or Product, in the same currency, and share at least one day, whatever their reports. The flag is worked out when read, so deleting one of the two clears it on the other.
  - A record "covers days before publication" when its period starts before the day the Published Post was published. One that starts on that day does not.
  - The source of a Commission record is always manual entry for now. The records made before this ticket keep their report name, and are given that source.
  - An affiliate link says which Products its Published Posts are of, so the page can say whether it counts among a Product's links or for the link only.
  - A refused Commission amount says "Enter an amount of zero or more". The commission amount on a shortlisted Product still uses the price wording: it was not part of this ticket.
