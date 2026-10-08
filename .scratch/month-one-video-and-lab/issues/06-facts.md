# 06: Facts

**What to build:** A member adds Facts to a Product, each in one language with an optional source. A Fact starts as Proposed, can be Confirmed, and can be Withdrawn. Changing a Fact withdraws it and creates a new Proposed one.

**Blocked by:** 04

**Status:** ready-for-agent

- [ ] A new Fact is Proposed and shows its language and source
- [ ] Confirming a Fact records who confirmed it and when
- [ ] A Fact's text cannot be edited in place; "edit" withdraws the old Fact and creates a new Proposed one
- [ ] Only the transitions Proposed to Confirmed, Proposed to Withdrawn and Confirmed to Withdrawn are allowed
- [ ] The Product page shows Facts grouped by state
- [ ] Confirming and withdrawing are written to the audit log
- [ ] A member of another Organization is refused reading or changing the Facts
- [ ] The seed gives AirBeat X1 three Confirmed Facts in Vietnamese
