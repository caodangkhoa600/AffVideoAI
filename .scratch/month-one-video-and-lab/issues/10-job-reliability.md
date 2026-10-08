# 10: Job reliability

**What to build:** Render jobs survive the things that go wrong: a double click starts one job, a member can cancel, a crashed worker's job is picked up again, failures are retried a limited number of times, and a failed job says what went wrong.

**Blocked by:** 09

**Status:** ready-for-agent

- [ ] Submitting the same render twice with the same idempotency key returns the existing job
- [ ] A worker claims a job with a time-limited lease and renews it while working; two workers never run the same job
- [ ] A job whose lease expires returns to the queue and is completed by another worker
- [ ] Retries are limited in number and spaced with exponential backoff; after the limit the job is failed
- [ ] A member can cancel a queued or running job; a running FFmpeg process is stopped and temporary files are removed
- [ ] A failed job stores its stage, a category, a readable message and the technical detail, and the web app shows the readable message
- [ ] Worker concurrency is bounded by configuration
- [ ] Each of these behaviours has a test at the HTTP seam, observing job state as the API reports it
