# 07: Projects and Variants

**What to build:** A member creates a Project from one Product with audience, language, target duration and objective, then adds Variants to it, each with a creative template and a Hook. A Variant can be duplicated with a different Hook.

**Blocked by:** 04

**Status:** done

- [x] A Project can be created from a Product and lists its Variants
- [x] Target duration is limited to 15–30 seconds; language is Vietnamese
- [x] A Variant requires a creative template and a Hook
- [x] Duplicating a Variant asks for a new Hook and creates a separate Variant
- [x] A Variant's identifier never changes
- [x] Projects can be listed, opened and deleted with confirmation
- [x] A member of another Organization is refused Projects and Variants by identifier

## Comments

2026-10-09, implemented. `dotnet test` passed 178 tests against PostgreSQL and MinIO in containers. The stack was rebuilt with `docker compose up --build --wait` and migrated, and the pages were driven in headless Chrome as the seeded Owner: "New Project" on the AirBeat X1 page opened the form with the Product chosen and its target audience filled in; 40 seconds and an empty objective were each refused next to their field; the Project was created and showed its brief; a Variant with no Hook was refused, one was added, and duplicating it was refused with its own Hook and then made a second Variant with another identifier while the first kept its own, also after a reload; the list showed the Project with "2 Variants"; deleting asked first, did nothing on Cancel, and on "Yes, delete" left an empty list and a page saying there is no such Project. That run left one `project.deleted` entry in the local audit log and nothing else.

Things that differ from what the ticket or spec might lead a reader to expect:

- A Project has no name. It is shown by its Product's name and its objective. Say so if a name is wanted.
- The limits are mine: audience and objective are each free text of at most 500 characters, and a Hook is at most 200.
- The brief cannot be edited ("fixed once"): there is no PUT or PATCH for a Project. Nor is there one, or a DELETE, for a Variant; a Variant only goes when its Project is deleted.
- A Project accepts only `vi`. English stays available for Facts.
- All three creative templates can be chosen now, though only the looks of ticket 14 will exist later. Ticket 08 plans only Product Showcase, so it has to refuse the other two with a reason until ticket 14.
- Creating a Project from a Product that does not exist, or that is another Organization's, is a 400 naming `productId`, not a 404: the identifier is in the body.
- Duplicating refuses only the Hook the duplicated Variant has (however spaced or capitalised), as a 400 naming `hook`. Two Variants of one Project can still have the same creative template and Hook if one is added twice, or if a duplicate is given a third Variant's Hook. Refusing that everywhere needs a rule and a unique index; say so if it is wanted before the Affiliate Lab compares Hooks.
- The API lets a Project be made from an archived Product, as it lets Facts be added to one. The web form offers only active Products.
- Deleting a Project deletes its Variants in the database with it, and is logged as `project.deleted` with the Project as its subject. Adding a Variant is not logged. Tickets 08 and 18 must decide what deleting means once a Variant has Storyboards, or is in a Campaign with results: "a Variant keeps its identifier for life" suggests refusing the deletion then, not cascading further.
- When one Project is deleted several times at the same moment, one request is answered 204 and logged and the rest 404. A Variant added to a Project at the moment it is deleted is answered 404.
- The API now reads an enum in a request body only by name: `"creativeTemplate": 0` is a 400. This is a setting for the whole API; no other request body has an enum yet.
- The Project page shows the oldest 200 Variants and says so when there are more.
- The seed adds no Project.
- Left from the code review as not worth the change now: the Hook forms for adding and duplicating share a shape, the `<select>` styling is repeated on three pages, and the test files each repeat three small helpers, as the earlier ones do.
- The web app's pages still have no automated test (ticket 17).
