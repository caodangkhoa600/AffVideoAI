# 06: Facts

**What to build:** A member adds Facts to a Product, each in one language with an optional source. A Fact starts as Proposed, can be Confirmed, and can be Withdrawn. Changing a Fact withdraws it and creates a new Proposed one.

**Blocked by:** 04

**Status:** done

- [x] A new Fact is Proposed and shows its language and source
- [x] Confirming a Fact records who confirmed it and when
- [x] A Fact's text cannot be edited in place; "edit" withdraws the old Fact and creates a new Proposed one
- [x] Only the transitions Proposed to Confirmed, Proposed to Withdrawn and Confirmed to Withdrawn are allowed
- [x] The Product page shows Facts grouped by state
- [x] Confirming and withdrawing are written to the audit log
- [x] A member of another Organization is refused reading or changing the Facts
- [x] The seed gives AirBeat X1 three Confirmed Facts in Vietnamese

## Comments

2026-10-09, implemented. `dotnet test` passed 139 tests against PostgreSQL and MinIO in containers. The stack was rebuilt with `docker compose up --build --wait`, migrated and seeded, and the AirBeat X1 page was driven in headless Chrome as the seeded Owner: the three seeded Facts showed under Confirmed with the Owner's email and the time; an empty Fact was refused next to its field; a Fact was added and appeared under Proposed, was confirmed, was edited (the old one moved to Withdrawn, the new one appeared under Proposed) and the new one was withdrawn after confirming. That run left two Withdrawn test Facts on the local AirBeat X1; `docker compose down -v` is the only way to be rid of them.

Things that differ from what the ticket or spec might lead a reader to expect:

- A Fact's language is `vi` or `en` and nothing else. The ticket says only "one language"; the list is mine, and is `ContentLanguages` in the domain. Say so if another language is wanted.
- The limits are mine too: a Fact's text and its source are each at most 500 characters. The source is free text, not a URL.
- Editing is `POST /api/v1/products/{productId}/facts/{factId}/replace`. It withdraws the Fact and adds the new Proposed one in one save. There is no PUT or PATCH for a Fact. The new Fact does not record which Fact it took the place of.
- A change of state that is not allowed is a 409 with the reason, including confirming a Fact that is already Confirmed and withdrawing one that is already Withdrawn. Neither is quietly accepted, unlike archiving an archived Product.
- When two members change one Fact at the same moment, one change is saved and the other is answered 409 and leaves nothing in the audit log. A test sends sixteen confirmations at once; without the guard thirteen to sixteen of them were recorded.
- The audit log gets `fact.confirmed` and `fact.withdrawn`, each with the Fact as its subject. An edit is logged as the withdrawal of the old Fact. Adding a Fact is not logged.
- A Withdrawn Fact that was Confirmed keeps who confirmed it and when. Who withdrew a Fact is only in the audit log; the Fact itself keeps when.
- The seeded Facts are Confirmed in the demonstration Owner's name, and the seed writes three `fact.confirmed` entries to the audit log as if the Owner had done it. A seeded Fact that a member has withdrawn is not added again, so such a database has fewer than three Confirmed.
- The list of Facts is paged and takes `?state=`. The Product page asks for each state separately and shows the oldest 200 of each, saying so when there are more.
- Facts can be added to and changed on an archived Product. Editors and Owners can do the same things.
- Nothing is Flagged for Review when a Fact is withdrawn: nothing uses Facts yet (ticket 13).
- The transition rule has its own small tests (`FactStateTests`), in the API test project because there is no other.
- Left from the code review as not worth the change: on the Product page, a refusal after someone else changed the Fact first (409) is not shown, because the list reloads and the Fact moves to the group it is really in; and the Facts form checks only for an empty Fact itself and leaves the rest to the API.
- The web app's pages still have no automated test (ticket 17).
