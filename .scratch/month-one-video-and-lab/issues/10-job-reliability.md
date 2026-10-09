# 10: Job reliability

**What to build:** Render jobs survive the things that go wrong: a double click starts one job, a member can cancel, a crashed worker's job is picked up again, failures are retried a limited number of times, and a failed job says what went wrong.

**Blocked by:** 09

**Status:** done

- [x] Submitting the same render twice with the same idempotency key returns the existing job
- [x] A worker claims a job with a time-limited lease and renews it while working; two workers never run the same job
- [x] A job whose lease expires returns to the queue and is completed by another worker
- [x] Retries are limited in number and spaced with exponential backoff; after the limit the job is failed
- [x] A member can cancel a queued or running job; a running FFmpeg process is stopped and temporary files are removed
- [x] A failed job stores its stage, a category, a readable message and the technical detail, and the web app shows the readable message
- [x] Worker concurrency is bounded by configuration
- [x] Each of these behaviours has a test at the HTTP seam, observing job state as the API reports it

## Comments

- 2026-10-09, implemented. `dotnet test` passed 425 tests before the last review fixes; after them (a cancel case added to the isolation tests, the retry time moved onto the job, a rename, two glossary entries) the solution builds and the isolation and state tests pass (138), but the full suite was not run again. The web app passed its typecheck and lint. Nothing was checked in a browser, and the Compose stack was not rebuilt or migrated.

  How each box was checked: by `RenderReliabilityTests`, each test on a database and API of its own with the workers it starts (killed with `docker kill`, given short leases, or given no Remotion). The web page (key, Cancel button, failure message, retry notice) has no automated test; that is ticket 17's browser test.

  Things a reader might not expect:

  - A render without an `Idempotency-Key` header is refused (400). A repeat with the same key answers 200 with the first job.
  - The job says `attempt` and `retryAt`, and a failure has a fourth category, `Timeout`. A Storyboard that cannot be rendered (`InvalidInput`) is not tried again.
  - The technical detail, stack trace included, is returned by the API to any member of the Organization. The web app does not show it.
  - Cancelling a job that completed or failed answers 409.

  Known gaps, from the code review, not fixed:

  - A job whose lease has run out only goes back to the queue when a worker next looks for work. With no worker, or every place busy, the API goes on saying it is rendering.
  - A worker that cannot reach the database for longer than a lease keeps rendering while another takes the job. Nothing it writes is kept, but for that time two workers run it. The worker should stop when its own lease deadline passes.
  - Lease expiry is judged by each worker's clock, not the database's.
  - A worker stopped politely (a deploy) leaves its job to the lease, which uses up an attempt.
  - Cancelling at a stage boundary is not tested on its own; only the kill of a running Remotion is. The cut-out model does not stop mid-photo.
  - `A_worker_renders_no_more_jobs_at_once_than_it_is_configured_to` failed once (the waiting job had been taken once already) and then passed 16 times. The cause is not known; the test now prints the worker's log if it happens again.
