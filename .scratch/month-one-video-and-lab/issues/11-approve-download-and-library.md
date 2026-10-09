# 11: Approve, download and video library

**What to build:** A member approves a Rendered Video and downloads the MP4. A library lists all Rendered Videos with search, filter, sort, preview, download and delete.

**Blocked by:** 09

**Status:** done

- [x] A Rendered Video that is ready for review can be approved; approval records who and when and is written to the audit log
- [x] Download is refused until the Rendered Video is approved; preview works before approval
- [x] The downloaded file is the same MP4 that was previewed
- [x] The library can be searched by Product or Project name, filtered by state and creative template, and sorted by date
- [x] Deleting a Rendered Video asks for confirmation and removes the stored file
- [x] A member of another Organization is refused approval, download and delete by identifier

## Comments

- 2026-10-09, implemented. `dotnet test` passed 456 tests, 31 of them new: the API in-process against PostgreSQL and MinIO in containers, with the worker rendering every Rendered Video these tests approve, download and delete. The web app passed its typecheck and lint. The stack was rebuilt with `docker compose up --build --wait` and migrated, and in headless Chrome as the seeded Owner: a 15-second video of the AirBeat X1 was rendered, found on the Videos page, narrowed by search, state and creative template and sorted both ways, approved by clicking Approve, downloaded (the same 1,938,436 bytes as the preview, as `AirBeat X1 20261009-121437.mp4`), and deleted from its Variant's page after the page asked once more. That check ran before the code review's changes below, which the tests cover and the browser did not see again.

  How each box was checked:

  - By a test at the HTTP seam: approval with who and when and its audit log entry; two members approving at once; download refused before approval and the preview not; the download byte for byte the preview; search, both filters, both orders and paging; delete removing the file from storage; another Organization refused approval, download and delete; someone not signed in refused.
  - In the browser, by hand, once: that the page asks before deleting. No automated test covers the question itself, and ticket 17's browser flow as the spec lists it stops at download.
  - The creative template filter is only shown letting every video through or none: Product Showcase is the only template that can be rendered until ticket 14, so no test has two videos for it to tell apart.

  Things that differ from what the ticket or spec might lead a reader to expect:

  - A Project has no name. Search looks in the Product's name and in the Project's objective, which is what the Projects page calls a Project by.
  - Any member can approve, including the one who rendered it, and nothing takes an approval back. Approving twice is 409.
  - A Rendered Video can be deleted whether or not it is approved. Ticket 19 will have to decide what a Published Post does to that.
  - Deleting writes `rendered-video.deleted` to the audit log (user story 12), which the ticket's box does not mention.
  - The job that made a deleted Rendered Video stays completed and loses its `renderedVideoId`, and the Variant's page says the video was deleted. A completed job without a Rendered Video is new.
  - With its Rendered Videos deleted a Project can be deleted again, which ticket 09 left impossible.
  - The row goes first and the file after it, as with a Product's photos: if storage refuses, the request still succeeds and the file is left behind with a warning in the log.
  - The stage a member sees for a completed job is now "Rendered"; "Ready for review" and "Approved" are the Rendered Video's own state, shown under the player.
  - A Rendered Video now says what it was made from (Product, Project, Variant, Hook, creative template, Storyboard version), for the library to show and link.
  - The download is named after the Product and the time it was rendered, in UTC.

  - The download test and the tests that read the library share two Rendered Videos; only the tests that approve or delete render their own.

  The worker's memory, which this ticket ran into and did not set out to change: the full test run failed twice with renders that never ended, because the kernel of Docker's virtual machine had killed the test worker for memory. The worker held 13.5 GiB while the cut-out model ran, measured with `docker stats`, on a machine that gives Docker 15.7 GiB. `OnnxCutOut` now loads the model with ONNX Runtime's memory arena and memory pattern off, and the same tests peak at 5.0 GiB. The cut-out tests pass unchanged. How long a cut-out takes was not measured before and after. This is very likely the render that "ended Failed, with the reason not captured" in ticket 09's notes.

  Besides the ticket: Rendered Videos moved out of `IRenders` into `IRenderedVideos`, and the escaping of a search for ILIKE into `LikePattern`, shared with Products.

  Left for later tickets:

  - 13: Flagged for Review is not a state here; the library's state filter has two values.
  - 17: the dashboard's recent Rendered Videos can come from `GET /api/v1/rendered-videos?pageSize=`.
  - 19: `RenderedVideo.CanBeDownloaded` is the approval rule; recording a Published Post needs the same one.
  - From the code review, not worth the change now: the preview still reads the whole MP4 into memory; the Videos page repeats the Products page's filter and paging code; `ScopedRenderedVideos` repeats the audit entry helper of `ScopedFacts`; a delete that loses five races in a row is a 500.
