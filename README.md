# AffiVideo

AffiVideo turns a Product photo and a few Confirmed Facts into a short vertical
marketing video. The vocabulary is in [CONTEXT.md](CONTEXT.md), the decisions in
[docs/adr](docs/adr), and the current spec and tickets in
[.scratch/month-one-video-and-lab](.scratch/month-one-video-and-lab).

## What runs

| Service    | What it is                                                                                             | Address on this machine                |
| ---------- | ------------------------------------------------------------------------------------------------------ | -------------------------------------- |
| `web`      | Next.js web app (`web/`)                                                                               | http://localhost:3000                  |
| `api`      | ASP.NET Core API (`src/AffiVideo.Api`)                                                                 | http://localhost:8080                  |
| `worker`   | .NET worker that renders: FFmpeg, Remotion and the cut-out model (`src/AffiVideo.Worker`, `remotion/`) | none                                   |
| `postgres` | PostgreSQL 17                                                                                          | localhost:5432                         |
| `minio`    | S3-compatible object storage                                                                           | http://localhost:9000, console on 9001 |

The only things needed on the host are Docker, and the .NET 10 SDK and Node 22
for running tests and tools. FFmpeg, Remotion, Chrome Headless Shell and the
model that cuts a Product out of its photo exist only inside the worker image.

## Start

```sh
cp .env.example .env
docker compose up --build --wait
docker compose run --rm migrate
docker compose run --rm seed
```

`up --wait` returns once all five services pass their health checks. The first
build downloads Chrome Headless Shell and the cut-out model (224 MB) and takes
a few minutes.

`migrate` applies the database migrations and creates the storage bucket. It is
the only thing that changes the schema; nothing does so at startup. Run it again
after pulling changes that add a migration.

`seed` creates the demonstration Organization, its Owner and a fictional sample
Product, the AirBeat X1, with three Confirmed Facts in Vietnamese. It adds only
what is missing, so run it again after pulling changes that add to the seed;
otherwise running it again changes nothing.

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

A worker on the host has nothing to render with, and every job it took would
fail. To render while working on the API or the web app, leave the worker to
Docker: `docker compose up -d --build --wait worker`.

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
when adding members, changing the Organiza tion's settings or reading the audit
log. Adding a member, confirming or withdrawing a Fact, deleting a Project,
approving or deleting a Rendered Video, and clearing a Flagged for Review mark
are recorded in the audit log, which an
Owner reads at `GET /api/v1/organizations/{id}/audit-log`; it has no page yet.

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

The tests that render build the worker image from the code as it is
(`docker build`, tagged `affivideo-worker:test`) and run it in a container
beside the other two. The worker renders the jobs the tests queue over HTTP,
and the MP4 the API serves is inspected with ffprobe inside that container.
The tests about the queue (`RenderReliabilityTests`) each make a database of
their own in the same PostgreSQL, run an API on it and start their own
workers, which they kill, starve of Remotion or give short leases.
The first build takes a few minutes; a render takes about a minute. To watch
what they rendered, name a file for it:
`AFFIVIDEO_TEST_VIDEO=render.mp4 dotnet test`.

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
Products (ticket 04), Product assets (ticket 05), Facts (ticket 06),
Projects and Variants (ticket 07), Storyboard generation (ticket 08),
render and preview (ticket 09), job reliability (ticket 10), approve,
download and the library (ticket 11), Storyboard editing (ticket 12) and
flagging work built on Withdrawn Facts (ticket 13).

Next: the Luxury and Problem/Solution templates (ticket 14) and the Affiliate
Lab and Campaigns (ticket 18).

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

Notes from Facts:

- A Fact is Proposed, then Confirmed, then Withdrawn; it can also go straight
  from Proposed to Withdrawn. Any other change is answered 409 with the reason.
- A Fact's text, language and source never change, and no endpoint changes
  them. Editing is `POST /api/v1/products/{productId}/facts/{factId}/replace`,
  which withdraws the Fact and adds a new Proposed one in a single save.
- A Fact is written in Vietnamese (`vi`) or English (`en`). The list is
  `ContentLanguages` in the domain.
- Two changes to one Fact at the same moment cannot both be saved: the second
  is answered 409 and nothing of it is recorded.
- The seeded Facts are Confirmed in the demonstration Owner's name, and the
  audit log says so.

Notes from Projects and Variants:

- A Project is one Product plus a brief: audience, language, target duration
  and objective. The brief is fixed when the Project is created; no endpoint
  changes it.
- The target duration is a whole number of seconds from 15 to 30, and the
  language is Vietnamese (`vi`): `ContentLanguages.VideoCodes` in the domain.
  Facts can still be kept in English.
- A Variant is a creative template (`LuxuryCinematic`, `ProductShowcase` or
  `ProblemSolution`) and a Hook. Nothing changes or removes a Variant, so its
  identifier is its identifier for life.
- Duplicating is `POST /api/v1/projects/{projectId}/variants/{variantId}/duplicate`
  with the new Hook. It adds a separate Variant with the same creative
  template, and refuses the Hook the duplicated Variant already has.
- Deleting a Project deletes its Variants with it and is recorded in the audit
  log as `project.deleted`. The Product is kept.

Notes from Storyboard generation:

- `POST /api/v1/projects/{projectId}/variants/{variantId}/storyboards` plans
  the Variant's next Storyboard version. It takes no body. Generating again
  adds a version; nothing changes or removes one.
- The text is written by the mock planner (`MockLanguageModel`): fixed
  Vietnamese sentence patterns with the Hook, the Product's name and Confirmed
  Facts placed in them whole. No AI and no credentials are involved, the same
  inputs always give the same Scenes, and every Storyboard says `Mock` as its
  planner. It is the only implementation of `ILanguageModel`; the video
  generation, text-to-speech and image-processing interfaces beside it in
  `AffiVideo.Application/Providers` have none.
- Only Product Showcase can be planned. Its four Scenes (Hook, Reveal, Facts,
  Closing) share the target duration as 3 : 5 : 7 : 5, in tenths of a second,
  and always sum to it exactly. The definition is `CreativeTemplates` in the
  domain, and its version is recorded on each Storyboard.
- A Storyboard uses the oldest Confirmed Facts in the Project's language: up
  to three, and fewer when three could not each be read in the time a shorter
  video gives them (three Facts of twelve words fit 20 seconds, two fit 15).
  Each Scene keeps the identifier and a copy of the text of the Facts it
  used. A usable photo is a photo (not the logo) of at least 400
  pixels on each side; the Facts Scene shows the newest, the others the oldest.
- Every Storyboard is planned in Product Lock, where the planning engine
  assigns only static image, image motion and text animation. Hybrid exists as
  a value and chooses nothing else until there is a provider to generate with.
- When a Storyboard cannot be planned the answer is 409 with the reason: no
  Confirmed Fact in the language, no usable photo, another creative template,
  a Fact with too many words to be read even alone, or a Hook, Fact or Product
  name too long for its layout. A Hook holds at most
  13 words (more would not all be on screen within two seconds) and 60
  characters; a Fact 120 characters.
- Deleting a Project deletes its Variants' Storyboards with them.

Notes from render and preview:

- `POST /api/v1/projects/{projectId}/variants/{variantId}/storyboards/{version}/renders`
  queues a job and answers 202 at once. The job is a row in PostgreSQL
  (`RenderJobs`); the worker takes the one queued longest. Its state is all the
  progress there is: `GET /api/v1/render-jobs/{id}`, which the web app asks
  every two seconds. There is no percentage.
- A job goes queued, validating, planning, generating assets, rendering,
  quality review, completed, and can fail from any of them with a reason for
  the member. `RenderJobStates` in the domain is the whole list of allowed
  changes. Generating video exists and nothing enters it yet.
- The Product is cut out of each photo by BiRefNet (general, lite; MIT
  licence) run on the CPU by ONNX Runtime in the worker, about 15 seconds a
  photo, once: the cut-out, on its soft shadow, is kept in object storage
  beside the photo and removed with it. A photo that came already cut out is
  used as it is when it passes the same checks.
- A cut-out is the photo's own pixels with a new transparency, and it is
  checked before use: something was found, the background did not stay, no
  large part is half see-through, no rim of background is left, and no pixel
  that shows was changed. A photo that fails is shown whole, on a card with
  rounded corners, and the Rendered Video names it (`uncutAssetIds`).
- Each Scene is drawn on its own by Remotion from a file of data (its text,
  its photo, and what the Scene before left on screen, so the Product carries
  on from there) and FFmpeg joins the clips without encoding the video again,
  under a silent AAC track. Both are started with a list of arguments and no
  shell. No text a member typed reaches FFmpeg at all.
- The look is the Product Showcase creative template in
  `remotion/src/ProductShowcase.tsx`. The image build type-checks and bundles
  it; a job renders from its own copy of the bundle in a folder under
  `/tmp/affivideo-render`, which is deleted when the job ends.
- The finished file is checked with ffprobe before it is kept: 1080 by 1920,
  H.264 tagged BT.709, AAC, MP4, and as long as its Scenes.
- A Rendered Video is stored at
  `organizations/{id}/rendered-videos/{id}.mp4` and previewed at
  `GET /api/v1/rendered-videos/{id}/content`, which authorises every request.
- A Project that has a Rendered Video cannot be deleted (409) until its
  Rendered Videos have been deleted.

Notes from job reliability:

- A render is submitted with an `Idempotency-Key` header of at most 100
  characters, and is refused (400) without one. A Storyboard version has one
  job for each key: the first request is answered 202, and a repeat 200 with
  the same job, whatever state it has reached. The web app makes a new key for
  each wish to render and keeps it until the API has answered.
- A worker takes a job under a lease (`LeaseId`, `LeaseExpiresAt` on the job)
  and renews it every few seconds while it works. Every write a worker makes is
  conditional on the lease still being its own, so a worker that has lost a job
  saves nothing, whatever it goes on to do.
- A job whose lease has run out goes back to the queue. Workers do this: each
  looks for such jobs whenever it looks for work, so with no worker running a
  job stays as its worker left it. A worker stopped politely leaves its job the
  same way, and that also counts as an attempt.
- An attempt that fails for a reason that might pass (anything but a Storyboard
  that cannot be rendered) puts the job back in the queue, to be taken again
  after a wait that doubles each time: 10 seconds, then 20. After three
  attempts the job is failed. While it waits it is queued, and the job says
  when it may next be taken (`retryAt`) and how often it has been (`attempt`).
- A failed job says the stage it failed in, a category (`InvalidInput`,
  `Internal`, `Timeout` or `WorkerLost`), a message for the member and the
  technical detail (`failure` on the job). The web app shows the message. The
  detail is what the program reported, stack trace included, and any member of
  the Organization can read it from the API.
- `POST /api/v1/render-jobs/{id}/cancel` cancels a job that is queued or
  running; one that has completed or failed answers 409. A running job's worker
  finds out when it next renews its lease or moves to the next stage, stops
  Remotion and FFmpeg, and deletes the job's folder. What the member sees is
  cancelled at once; the worker follows within the renewal interval.
- The settings, with their defaults. For the worker's queue, section
  `RenderQueue`: `LeaseDuration` (30 s), `LeaseRenewalInterval` (5 s),
  `MaxAttempts` (3), `RetryBaseDelay` (10 s). For the worker itself, section
  `Rendering`: `JobsAtOnce` (1), the number of jobs one worker renders at the
  same time. In Compose they are environment variables on the worker, such as
  `Rendering__JobsAtOnce`. Several workers can serve one queue.
- A job's temporary files are in `/tmp/affivideo-render/{job id}-{attempt}`.

Notes from approve, download and the library:

- A Rendered Video is ready for review, then approved:
  `POST /api/v1/rendered-videos/{id}/approve` records who and when, and writes
  `rendered-video.approved` to the audit log. Any member can approve, the one
  who rendered it included. Nothing takes an approval back; approving twice is
  answered 409.
- `GET /api/v1/rendered-videos/{id}/download` is the same file as the preview,
  sent to be saved under the Product's name and the time it was rendered. It
  answers 409 until the Rendered Video is approved. The preview needs no
  approval.
- The library is `GET /api/v1/rendered-videos` and the Videos page. `search`
  looks in the Product's name and in the Project's objective, which is what a
  Project is called by since it has no name of its own; `state` and
  `creativeTemplate` narrow the list; `sort` is `NewestFirst` (the default) or
  `OldestFirst`. Each Rendered Video says the Product, Project, Variant and
  Storyboard version it was made from.
- `DELETE /api/v1/rendered-videos/{id}` deletes a Rendered Video in either
  state, with its file, and writes `rendered-video.deleted` to the audit log.
  The web app asks once more first. The job that made it is kept, completed and
  with no Rendered Video, and the Storyboard version can be rendered again.
- The cut-out model is loaded with ONNX Runtime's memory arena off. With it on,
  the worker held over 13 GB while a photo was cut, and Docker's virtual
  machine killed it; it now peaks near 5 GB. Give Docker at least 8 GB.

Notes from flagging work built on Withdrawn Facts:

- A Storyboard version is Flagged for Review while a Scene of it lists a Fact
  that is Withdrawn, and a Rendered Video while the version it was rendered
  from does. Both say why in `flags`: one entry for each such Fact, with the
  text as the work used it and when the Fact was withdrawn. Nothing is written
  when a Fact is withdrawn: the flag is read from the Scenes' own record of the
  Facts they used, so work made before this existed, or rendered after the
  withdrawal, is flagged like any other.
- Flagged work is kept as it is. It can still be read, played, approved,
  downloaded and rendered. An edit of a flagged version is still refused while
  a Scene rests on the Withdrawn Fact (see Storyboard editing).
- `POST .../storyboards/{version}/clear-flag` and
  `POST /api/v1/rendered-videos/{id}/clear-flag` clear the flags on that one
  version or video, in the member's name, and write `storyboard.flag-cleared`
  or `rendered-video.flag-cleared` to the audit log. The body names the
  Withdrawn Facts whose flags the member saw (`factIds`), and only those are
  cleared: a Fact withdrawn since the page was loaded keeps its flag. They
  answer 409 for work that is not flagged, or not by any of those Facts. What
  is kept is which Fact's flag was cleared (`ClearedFlags`), so a Fact
  withdrawn afterwards flags the work again.
- A version and each video rendered from it are reviewed separately: clearing
  one leaves the others flagged. A video rendered from a version after its
  flag was cleared is flagged, since it shows the Withdrawn Fact.
- A Manually Edited Scene lists no Facts, so withdrawing a Fact its text was
  once written from flags nothing.
- `GET /api/v1/rendered-videos?flagged=true` is the library narrowed to
  Flagged for Review; `flagged=false` is the rest.

Notes from Storyboard editing:

- `POST /api/v1/projects/{projectId}/variants/{variantId}/storyboards/{version}/edits`
  makes the Variant's next version from that one, edited, and leaves that one
  as it is. The body names every Scene of the version once, in the order the
  next version plays them, each with what to change about it: `onScreenText`,
  `narrationText`, `assetId` (the photo it shows) and `durationMs`. Whatever is
  left out stays. Any version can be edited, not only the newest.
- A Scene whose on-screen or narration text changes is marked Manually Edited
  (`manuallyEdited`), and stays marked through later versions. Its text is the
  Organization's own: the Scene no longer lists Facts, and nothing checks what
  it says against them. Text sent back as it was is no edit.
- `POST .../storyboards/{version}/scenes/{position}/regenerate` makes the next
  version with that one Scene planned again from the Product's Confirmed Facts
  and photos as they are now. The Scene keeps its place and its duration and
  loses its mark; every other Scene is as it was.
- An edit or a regeneration that would make an unacceptable version is
  answered 409 with every reason, and makes nothing: Scenes that do not sum to
  the target duration; a photo the Product does not have, or an image that is
  the logo or under 400 pixels a side; a Scene that still rests on a Fact which
  is no longer Confirmed; the Hook's Scene anywhere but first; a Scene shorter
  than its layout needs (2 seconds, 2.5 for the closing); more or fewer lines
  than the layout sets (one, two for the closing, one to three Facts); a line
  too long for its layout; Facts given too little time to be read; narration
  over 500 characters; or an edit that changes nothing. The limits are
  `CreativeTemplates` and `StoryboardRules` in the domain.
- The worker keeps the Scene clips it draws, once the video they were drawn
  for has passed its checks, in object storage at
  `organizations/{id}/scene-clips/{key}.mp4`. The key is a hash of everything
  that decides the clip's frames: the bundled templates, the file of data the
  Scene's template is handed, and the images that names. A Scene with the same
  key is not drawn again, in the next version or in the same one rendered
  again. A Rendered Video says which Scenes were drawn for it
  (`drawnScenePositions`). A Scene is handed the layout and photo of the Scene
  before it, so moving a Scene or changing its photo also redraws the Scene
  after it; changing its text or duration does not.
- Nothing removes a kept clip. They are the Organization's until its storage is
  cleared by hand.
- On the Variant's page, "Edit the Scenes" opens the Scene editor on the
  version being read, and each Scene has "Regenerate this Scene".
