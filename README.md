# AffiVideo

AffiVideo turns a Product photo and a few Confirmed Facts into a short vertical
marketing video. The vocabulary is in [CONTEXT.md](CONTEXT.md), the decisions in
[docs/adr](docs/adr), and the current spec and tickets in
[.scratch/month-one-video-and-lab](.scratch/month-one-video-and-lab).

## What runs

| Service    | What it is                                              | Address on this machine         |
| ---------- | ------------------------------------------------------- | ------------------------------- |
| `web`      | Next.js web app (`web/`)                                | http://localhost:3000           |
| `api`      | ASP.NET Core API (`src/AffiVideo.Api`)                  | http://localhost:8080           |
| `worker`   | .NET worker with FFmpeg and Remotion (`src/AffiVideo.Worker`, `remotion/`) | none             |
| `postgres` | PostgreSQL 17                                           | localhost:5432                  |
| `minio`    | S3-compatible object storage                            | http://localhost:9000, console on 9001 |

The only things needed on the host are Docker, and the .NET 10 SDK and Node 22
for running tests and tools. FFmpeg, Remotion and Chrome Headless Shell exist
only inside the worker image.

## Start

```sh
cp .env.example .env
docker compose up --build --wait
docker compose run --rm migrate
docker compose run --rm seed
```

`up --wait` returns once all five services pass their health checks. The first
build downloads Chrome Headless Shell and takes a few minutes.

`migrate` applies the database migrations and creates the storage bucket. It is
the only thing that changes the schema; nothing does so at startup. Run it again
after pulling changes that add a migration.

`seed` creates the demonstration Organization, its Owner and a fictional sample
Product, the AirBeat X1. It adds only what is missing, so run it again after
pulling changes that add to the seed; otherwise running it again changes
nothing.

Then open http://localhost:3000 and sign in (see [Sign in](#sign-in)).
http://localhost:3000/status needs no sign-in and shows the API, the database
and object storage as reachable.

If a port is taken, change it in `.env`. `docker compose down` stops everything
and keeps the data; `docker compose down -v` also deletes it.

## Run the apps on the host

For working on the code, run only the database and storage in Docker and the
three apps on the host:

```sh
docker compose up -d --wait postgres minio
dotnet run --project src/AffiVideo.Api -- migrate
dotnet run --project src/AffiVideo.Api -- seed
dotnet run --project src/AffiVideo.Api        # http://localhost:5080
dotnet run --project src/AffiVideo.Worker
npm --prefix web run dev                      # http://localhost:3000
```

Their settings come from files, so nothing has to be typed:

- `src/AffiVideo.Api/appsettings.Development.json` and the same file in
  `src/AffiVideo.Worker` hold the database and storage settings. The values
  match `.env.example`.
- `Properties/launchSettings.json` in each project sets the Development
  environment, and the API's port.
- `web/.env.development` tells the web app where the API is.

If your `.env` differs from `.env.example` (a changed port or password), set
the same value for the API and worker on this machine only. Both read one
user-secrets store:

```sh
dotnet user-secrets set "ConnectionStrings:Database" "Host=localhost;Port=5433;Database=affivideo;Username=affivideo;Password=local-only-change-me" --project src/AffiVideo.Api
```

For the web app, put overrides in `web/.env.local`.

The Compose services do not read these files: containers run in the
Production environment and take everything from `.env`.

## Sign in

The seed creates one Organization, "AffiVideo Demo", with one Owner:

| Email                        | Password              |
| ---------------------------- | --------------------- |
| `owner@demo.affivideo.local` | `demo-owner-password` |

These are for a system that only listens on this machine. There is no sign-up:
a member comes from the seed or from an Owner adding an Editor on the
Members page. Nothing is emailed, so the Owner chooses the Editor's password
(at least 12 characters) and passes it on.

An Owner can do everything. An Editor can do all creative work but is refused
when adding members, changing the Organization's settings or reading the audit
log. Adding a member is recorded in the audit log, which an Owner reads at
`GET /api/v1/organizations/{id}/audit-log`; it has no page yet.

The session is a cookie that scripts cannot read (HttpOnly, Secure,
SameSite=Lax); nothing is kept in browser storage. Chrome, Edge and Firefox
accept a Secure cookie from `http://localhost`. Safari does not, so use one of
the others until the app is served over HTTPS. Five wrong passwords in a row
lock the member out for five minutes.

A request that changes anything must send an anti-forgery token: fetch
`GET /api/v1/antiforgery-token` and send its `requestToken` in the
`X-CSRF-TOKEN` header. The web app's API client does this for every such
request.

## Keeping Organizations apart

Everything an Organization owns carries its identifier, and
`AffiVideoDbContext` is where it is kept apart: a query only returns records
of the caller's Organization, and saving refuses a record of any other. An
endpoint asked for another Organization's record answers 404, exactly as for
one that does not exist. A new entity that an Organization owns implements
`IOwnedByOrganization` and gets both; add a case for it to
`tests/AffiVideo.Api.Tests/OrganizationIsolationTests.cs`.

## Test

```sh
dotnet test
```

The tests start the API in-process against real PostgreSQL and real object
storage in containers (Testcontainers), so Docker must be running. They do not
use the Compose services or `.env`.

```sh
cd web
npm ci
npm run typecheck
npm run lint
```

To check that the worker image can draw Vietnamese text with no network:

```sh
docker run --rm --network none --entrypoint npm affivideo-worker --prefix /app/remotion run typeface-check
```

It renders one frame in every bundled weight of Be Vietnam Pro and fails if a
weight cannot be loaded.

## Change the API

Building the API writes its OpenAPI description to `openapi/openapi.json`. The
web app's types are generated from that file. After changing an endpoint:

```sh
dotnet build
npm --prefix web run generate:api
```

Commit both files. `npm --prefix web run check:api` fails if the types are out
of date.

## Change the database

```sh
dotnet tool restore
dotnet ef migrations add <Name> --project src/AffiVideo.Infrastructure --output-dir Persistence/Migrations
docker compose up --build --wait
docker compose run --rm migrate
```

## Change the Remotion version

The version is pinned in `remotion/package.json` and `remotion/package-lock.json`,
and it decides which Chrome Headless Shell the worker image downloads. Changing
it is a deliberate act that includes re-reading the licence (ADR 0002).

## Status

Done: the look prototypes (tickets 01, 25, 26, in `prototypes/`), the affiliate
experiment plan (ticket 24, `docs/business/affiliate-experiment.md`), the
walking skeleton (ticket 02), sign in and Organizations (ticket 03),
Products (ticket 04) and Product assets (ticket 05).

Next: Facts (ticket 06).

Notes from the walking skeleton:

- MinIO stopped publishing container images, so Compose and the tests run
  Chainguard's build of MinIO, pinned by digest.
- The first migration is empty: no ticket has needed a table yet. It exists so
  that the migrate command and the migrations history are in place.

Notes from sign in and Organizations:

- A member belongs to exactly one Organization, and an email can be used once
  across all of them.
- The role and the Organization are read from the session cookie, so a change
  to either would not reach a session that is already open. Nothing changes
  them yet; the ticket that does must end the member's sessions.
- The keys that protect cookies are stored in the database, so rebuilding the
  API does not sign anyone out.

Notes from Products:

- Product URLs are text. Nothing requests them; the API only checks that they
  are `http://` or `https://` addresses.
- A price is a decimal with at most two decimal places and always has a
  currency beside it.
- An archived Product is kept and can still be read and edited. Nothing
  brings one back yet.
- A 400 names each field as the request does (`originalUrl`), and the web
  app shows the message next to that field.

Notes from Product assets:

- An upload is a JPEG, PNG or WebP of at most 20 MB and 6000 pixels on each
  side. It is judged by decoding it; its name and declared type are ignored.
- What is stored is a PNG of the decoded pixels, turned upright, in the
  photo's own colour space. Nothing else of the file is kept, so where and
  with what a photo was taken is left behind.
- A file's key in object storage starts with `organizations/{id}/`. The
  bucket is private and the API never hands out a storage address: a file is
  read at `GET /api/v1/products/{productId}/assets/{assetId}/content`, which
  authorises every request.
- A Product has at most 30 photos and one logo. A new logo replaces the old.
- Images are decoded with SkiaSharp (MIT licence), whose native library ships
  in the NuGet package for both Windows and the Linux containers.
