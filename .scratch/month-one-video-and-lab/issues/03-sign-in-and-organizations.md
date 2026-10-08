# 03: Sign in and Organizations

**What to build:** A member signs in with email and password, lands in their Organization, and signs out. An Owner can add an Editor. A seeded demonstration Organization exists. This ticket establishes the tenant isolation mechanism that every later ticket relies on, and the audit log.

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] Sign in and sign out work in the web app; unauthenticated requests to protected pages and endpoints are refused
- [ ] The session is an HttpOnly, Secure, SameSite cookie; nothing is kept in browser storage
- [ ] State-changing requests without a valid anti-forgery token are refused
- [ ] Sign-up is closed: accounts come from seed data or from an Owner adding a member
- [ ] Roles are Owner and Editor; an Editor is refused when managing members or Organization settings
- [ ] Tenant-owned records carry their Organization, and the data-access layer scopes every query to the caller's Organization
- [ ] A test with two Organizations proves a member of one is refused the other's member list and settings by identifier
- [ ] Adding a member is written to the audit log with actor, Organization, action and time
- [ ] The seed creates a demonstration Organization with an Owner whose credentials are documented in the README
