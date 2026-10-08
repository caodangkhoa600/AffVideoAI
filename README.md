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
```

`up --wait` returns once all five services pass their health checks. The first
build downloads Chrome Headless Shell and takes a few minutes.

`migrate` applies the database migrations and creates the storage bucket. It is
the only thing that changes the schema; nothing does so at startup. Run it again
after pulling changes that add a migration.

Then open http://localhost:3000/status. It shows the API, the database and
object storage as reachable.

If a port is taken, change it in `.env`. `docker compose down` stops everything
and keeps the data; `docker compose down -v` also deletes it.

## Run the apps on the host

For working on the code, run only the database and storage in Docker and the
three apps on the host:

```sh
docker compose up -d --wait postgres minio
dotnet run --project src/AffiVideo.Api -- migrate
dotnet run --project src/AffiVideo.Api        # http://localhost:5080
dotnet run --project src/AffiVideo.Worker
npm --prefix web run dev                      # http://localhost:3000/status
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
experiment plan (ticket 24, `docs/business/affiliate-experiment.md`) and the
walking skeleton (ticket 02).

Next: sign in and Organizations (ticket 03).

Notes from the walking skeleton:

- MinIO stopped publishing container images, so Compose and the tests run
  Chainguard's build of MinIO, pinned by digest.
- The first migration is empty: no ticket has needed a table yet. It exists so
  that the migrate command and the migrations history are in place.
- There is no `AffiVideo.Domain` project yet. Ticket 03 adds it with its first
  type, the Organization.
- shadcn/ui, React Hook Form and Zod are not installed yet. The status page
  needs none of them; the first ticket with a form adds them.
