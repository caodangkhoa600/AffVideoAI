# 04: Products

**What to build:** A member creates, edits, archives, lists, searches and filters Products, with name, category, brand, description, optional price and currency, original URL, optional affiliate URL, target audience and tags.

**Blocked by:** 03

**Status:** done

- [x] A Product can be created, viewed, edited and archived from the web app
- [x] The list supports search by name, filter by category and status, and pagination
- [x] Invalid input returns field-level validation errors shown next to the fields
- [x] Price is stored as a decimal with an explicit currency
- [x] Product URLs are stored as text and never fetched
- [x] A member of another Organization is refused read, edit and archive of the Product by identifier
- [x] The seed adds the fictional AirBeat X1 Product to the demonstration Organization

## Comments

2026-10-09, implemented. `dotnet test` passed 81 tests against PostgreSQL and MinIO in containers. The stack was rebuilt with `docker compose up --build --wait`, migrated and seeded, and the web app was driven in headless Chrome as the seeded Owner: the list showed the AirBeat X1; a Product was created, edited and archived; search, the category filter and the status filter narrowed the list; mistakes were reported next to their fields, both the ones the form catches and one only the API refused.

Things that differ from what the ticket or spec might lead a reader to expect:

- Products are at `/api/v1/products`, not under `/organizations/{id}`. The Organization is the caller's own, from the session.
- Name, category, brand, description, original URL and target audience are all required, because the spec marks only price, affiliate URL and (by implication) tags as optional. Say so if brand or target audience should be optional.
- A URL must be a full `http://` or `https://` address. It is checked for shape only and stored as typed, less any spaces around it. Nothing in the system requests it; the Product page shows it as a link the member can follow in their own browser.
- A price has at most 13 digits before the decimal point and 2 after (`numeric(15,2)`), so it survives a browser, where every number is a double. A finer price is refused, not rounded. Price and currency come together or not at all; the currency is three capital letters and is not checked against the ISO 4217 list.
- Archiving cannot be undone from the app or the API: the ticket asks for archive only. The web app asks once more before archiving. An archived Product can still be read and edited.
- The list shows every status unless asked; the web app asks for Active by default. Search matches any part of the name in any letter case, but does not ignore Vietnamese diacritics ("binh" does not find "Bình"). The category filter is an exact match, and `GET /api/v1/products/categories` lists the ones in use for the web app's filter.
- Category is free text, so "Audio" and "audio" are two categories. The web app's category filter also offers the categories of archived Products, so with the status on Active a category can come up empty.
- The spec's "import-provider interface ... defined with no implementation" is not in this ticket's list and no ticket owns it. It is not built.
- Tags are trimmed, blanks dropped, and a repeat in another letter case kept once. At most 20, each at most 50 characters.
- A 400 now names fields in camelCase (`originalUrl`), as requests do, for every endpoint. It was `Name` for the Organization's name before.
- A request that cannot be read (a status that does not exist, a page that is not a number) is a 400 in every environment. In Development it used to be a 500.
- The seed adds whatever is missing, so a database seeded before this ticket gains the AirBeat X1 when `seed` is run again. The Product has a fixed identifier, so renaming or archiving it does not bring a second one. Its details are in Vietnamese and say it is fictional.
- Editors and Owners can do the same things with Products. Nothing about Products is written to the audit log.
- The web app's pages have no automated test (ticket 17 is the one browser test). The filters are not kept in the URL, so they reset when the list is opened again.
