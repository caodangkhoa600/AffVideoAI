# 02: Walking skeleton

**What to build:** One command starts PostgreSQL, object storage, the API, the worker and the web app locally, and the web app shows a status page proving it can talk to the API, which in turn can reach the database and object storage. The test harness for the HTTP seam exists and has its first passing test.

**Blocked by:** 26 (done: Remotion draws the Scenes and FFmpeg joins them, ADR 0002, which decides what the worker image contains).

**Status:** done

- [x] The repository is initialised with git at this directory, with AffiVideo as the name in code
- [x] A single Docker Compose command starts all five services, each with a health check
- [x] The first database migration is applied by an explicit command, never automatically at startup
- [x] The worker image contains FFmpeg, ffprobe, Node, Remotion with its Chrome Headless Shell, and a typeface with full Vietnamese diacritics and an open licence
- [x] The Remotion version is pinned with a committed lock file
- [x] Inside the worker container, with the network off, Remotion renders a frame that uses the typeface
- [x] The web app's API types are generated from the API's OpenAPI description
- [x] The status page shows the API, database and object storage as reachable
- [x] An integration test starts the API against real PostgreSQL and object storage in containers and passes
- [x] An example environment file lists every variable; no secrets are committed
- [x] The README has start-up, migration and test commands that were actually run

## Comments

2026-10-08, implemented. Every box above was checked by running it: `docker compose up --build --wait` brought all five services to healthy, `docker compose run --rm migrate` applied the first migration and created the bucket, the status page showed the API, database and object storage as reachable, `dotnet test` passed 7 tests against PostgreSQL and MinIO in containers, and the worker image rendered a frame in Be Vietnam Pro with `--network none`.

Things that differ from what the ticket or spec might lead a reader to expect:

- MinIO no longer publishes container images (`minio/minio` is gone from Docker Hub and quay.io refuses anonymous pulls). Compose and the tests run Chainguard's build of MinIO, pinned by digest, because that registry only offers `latest`.
- The first migration is empty. No ticket has needed a table yet; it puts the migrate command and the migrations history in place.
- `migrate` also creates the storage bucket, so one explicit command prepares both stores.
- There is no `AffiVideo.Domain` project yet; ticket 03 adds it with the Organization. shadcn/ui, React Hook Form and Zod are not installed yet; the status page needs none of them.
- The worker image has no cut-out model. The ticket does not list it; ticket 09 adds it when it renders a Product.
- Stopping PostgreSQL under the running stack showed the status still reporting the database as reachable, because opening a pooled connection does not touch the server. The probe now runs a query, and a test stops a database container to cover it.
