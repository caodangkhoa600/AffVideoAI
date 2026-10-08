# 02: Walking skeleton

**What to build:** One command starts PostgreSQL, object storage, the API, the worker and the web app locally, and the web app shows a status page proving it can talk to the API, which in turn can reach the database and object storage. The test harness for the HTTP seam exists and has its first passing test.

**Blocked by:** 01 (the founder's verdict decides whether this starts now or after work on the look).

**Status:** ready-for-agent

- [ ] The repository is initialised with git at this directory, with AffiVideo as the name in code
- [ ] A single Docker Compose command starts all five services, each with a health check
- [ ] The first database migration is applied by an explicit command, never automatically at startup
- [ ] The worker image contains FFmpeg, ffprobe and a typeface with full Vietnamese diacritics and an open licence
- [ ] The web app's API types are generated from the API's OpenAPI description
- [ ] The status page shows the API, database and object storage as reachable
- [ ] An integration test starts the API against real PostgreSQL and object storage in containers and passes
- [ ] An example environment file lists every variable; no secrets are committed
- [ ] The README has start-up, migration and test commands that were actually run
