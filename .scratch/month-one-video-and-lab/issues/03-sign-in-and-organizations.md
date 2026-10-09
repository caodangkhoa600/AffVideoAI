# 03: Sign in and Organizations

**What to build:** A member signs in with email and password, lands in their Organization, and signs out. An Owner can add an Editor. A seeded demonstration Organization exists. This ticket establishes the tenant isolation mechanism that every later ticket relies on, and the audit log.

**Blocked by:** 02

**Status:** done

- [x] Sign in and sign out work in the web app; unauthenticated requests to protected pages and endpoints are refused
- [x] The session is an HttpOnly, Secure, SameSite cookie; nothing is kept in browser storage
- [x] State-changing requests without a valid anti-forgery token are refused
- [x] Sign-up is closed: accounts come from seed data or from an Owner adding a member
- [x] Roles are Owner and Editor; an Editor is refused when managing members or Organization settings
- [x] Tenant-owned records carry their Organization, and the data-access layer scopes every query to the caller's Organization
- [x] A test with two Organizations proves a member of one is refused the other's member list and settings by identifier
- [x] Adding a member is written to the audit log with actor, Organization, action and time
- [x] The seed creates a demonstration Organization with an Owner whose credentials are documented in the README

## Comments

2026-10-09, implemented. `dotnet test` passed 47 tests against PostgreSQL and MinIO in containers. The stack was rebuilt with `docker compose up --build --wait`, migrated and seeded, and the web app was driven in headless Chrome: an unauthenticated visit to `/members` went to the sign-in page and back after signing in as the seeded Owner, the Owner added an Editor and renamed the Organization, signed out, and the Editor signed in and was shown neither form. The browser held two cookies, both HttpOnly and Secure, and nothing in local or session storage.

Things that differ from what the ticket or spec might lead a reader to expect:

- Seeding is its own command, `docker compose run --rm seed`, not part of `migrate`.
- A member belongs to exactly one Organization and an email can be used once across all of them. The identity system's own lookups (finding a member by email at sign-in, checking an email is unused) are the one place that looks across Organizations; everything else goes through the filtered context. One consequence: an Owner who tries to add an email that belongs to a member of another Organization is told it is taken, so they learn that the email is in use somewhere.
- Another Organization's record is answered with 404, the same as one that does not exist, not 403.
- The check that refuses a write for another Organization (`AffiVideoDbContext.RefuseWritesForAnotherOrganization`) has no test. Nothing reaches it over HTTP, because the read filter stops such a request first; it is a second line behind the filters, which the tests in `OrganizationIsolationTests.cs` do cover (they fail with the filters switched off).
- The web app's pages have no automated test. The spec keeps the browser to one Playwright test, which is ticket 17.
- The Owner chooses the Editor's password, at least 12 characters. Nothing is emailed.
- The audit log can be read at `GET /api/v1/organizations/{id}/audit-log` by an Owner. It has no page in the web app.
- An Editor can read the member list and the Organization's name; only adding members, changing settings and the audit log are refused.
- The API marks every cookie Secure whatever the scheme of the request, because the web app reaches it over plain HTTP inside Docker. Chrome, Edge and Firefox accept that from `http://localhost`; Safari does not.
- Five wrong passwords lock a member out for five minutes (the identity system's default). This allows anyone who knows an email to lock that member out.
- The role and Organization live in the session cookie. Nothing changes them yet; the ticket that does must end the member's open sessions.
- The keys protecting cookies are kept in the database (a `DataProtectionKeys` table) so that rebuilding the API does not sign everyone out.
- The seeded Organization has no sample Product (user story 11); ticket 04 adds it with Products.
- The web app's protected pages are gated in `web/src/proxy.ts`, which asks the API whether the cookie is a live session on each page request. shadcn/ui, React Hook Form and Zod came in with this ticket.
