# 05: Product assets

**What to build:** A member uploads several photos and a logo to a Product, sees them on the Product page, and removes them. Files are stored through the storage abstraction and are only reachable through the API.

**Blocked by:** 04

**Status:** ready-for-agent

- [ ] Photos and a logo can be uploaded, viewed and removed from the Product page
- [ ] An upload is validated by decoding its content, not by its name or declared type
- [ ] Uploads over the size or dimension limit, or that are not decodable images, are rejected with a clear reason
- [ ] Accepted images are re-encoded to a normalised format before storage
- [ ] Storage keys are namespaced by Organization
- [ ] A file is served only after the API authorises the request
- [ ] A member of another Organization is refused the file by identifier, and a guessed storage path does not work
