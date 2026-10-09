# 05: Product assets

**What to build:** A member uploads several photos and a logo to a Product, sees them on the Product page, and removes them. Files are stored through the storage abstraction and are only reachable through the API.

**Blocked by:** 04

**Status:** done

- [x] Photos and a logo can be uploaded, viewed and removed from the Product page
- [x] An upload is validated by decoding its content, not by its name or declared type
- [x] Uploads over the size or dimension limit, or that are not decodable images, are rejected with a clear reason
- [x] Accepted images are re-encoded to a normalised format before storage
- [x] Storage keys are namespaced by Organization
- [x] A file is served only after the API authorises the request
- [x] A member of another Organization is refused the file by identifier, and a guessed storage path does not work

## Comments

2026-10-09, implemented. `dotnet test` passed 110 tests against PostgreSQL and MinIO in containers. The stack was rebuilt with `docker compose up --build --wait` and migrated, and the Product page was driven in headless Chrome as the seeded Owner: three files were chosen at once, of which one was kept and two were refused with their reasons next to their names (not an image; over 20 MB); a logo was uploaded, the button became "Replace logo", every image drew, and the logo was removed after confirming. A 15 MB PNG went through the web app's origin to the API in its Linux container in about two seconds.

Things that differ from what the ticket or spec might lead a reader to expect:

- The limits are mine, not the spec's: a file of at most 20 MB, at most 6000 pixels on each side, and JPEG, PNG or WebP only. A GIF, an SVG or a HEIC photo straight from an iPhone is refused. Say so if any of these should change; they are constants on `ProductAsset`.
- The normalised format is PNG. It is lossless, so the stored pixels are exactly what the upload decodes to, and it keeps a logo's transparency. The cost is size: a 12-megapixel photo is 15 to 25 MB as a PNG, and the Product page shows the full file, as there are no thumbnails.
- The PNG keeps the photo's colour space (a phone's Display P3 stays Display P3) and is turned upright where the camera noted a rotation. Everything else in the file is dropped, including where and with what it was taken. An animation keeps its first frame, and a 16-bit image becomes 8-bit.
- A Product has at most one logo, and uploading another replaces it. It has at most 30 photos; the list of assets is therefore not paged. Two photos uploaded at the same instant can both pass that check, so the 30 is a guard and not a guarantee.
- A size over the limit is a 400 that names the file, like the other refusals. A request far over the limit (beyond 21 MB) is cut off by the server before the API can answer with a reason; the web app refuses such a file itself before sending it.
- A file is read at `GET /api/v1/products/{productId}/assets/{assetId}/content`. The API streams it; there are no short-lived links. A browser may keep the image but must ask again before showing it, so each showing is authorised.
- "A guessed storage path does not work" is tested two ways: the object's real address in MinIO answers 403 to someone without the storage's credentials, and the API has no endpoint that takes a storage key.
- An asset can be added to and removed from an archived Product. Editors and Owners can do the same things. Nothing about assets is written to the audit log: the spec's "deletion" was read as the deletion of a Rendered Video (ticket 11).
- Removing an asset deletes its file. No Storyboard exists yet to refer to one; the ticket that adds Storyboards has to decide what removing a used asset means.
- If the record is saved and the file's deletion then fails, or the reverse on upload, a file is left in storage with nothing pointing at it. It is logged and unreachable. Nothing sweeps such files up.
- The seeded AirBeat X1 has no photo.
- Images are decoded with SkiaSharp (MIT). ImageSharp was the other candidate and was passed over for its licence.
- The web app's upload passes through the Next proxy, which forwards only 10 MB of a request by default; `next.config.ts` raises that to 21 MB with an option Next marks experimental.
- The web app's pages still have no automated test (ticket 17).
