# 16: Production cost records

**What to build:** Every render writes a cost record, and a member sees the production cost of each Rendered Video and the total for a Product.

**Blocked by:** 09

**Status:** ready-for-agent

- [ ] Each render attempt, successful or failed, writes a record with provider, Technique counts, duration, attempt number, estimated amount and currency
- [ ] Rates come from configuration and the record stores the version of the rates used; local rendering defaults to zero
- [ ] Amounts are labelled as estimated, never as billed
- [ ] The Rendered Video page shows its production cost, including failed attempts that led to it
- [ ] The Product page shows total production cost across its Rendered Videos
- [ ] Pure-function tests cover the cost calculation
