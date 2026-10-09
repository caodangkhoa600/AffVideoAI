# 16: Production cost records

**What to build:** Every render writes a cost record, and a member sees the production cost of each Rendered Video and the total for a Product.

**Blocked by:** 09

**Status:** done

- [x] Each render attempt, successful or failed, writes a record with provider, Technique counts, duration, attempt number, estimated amount and currency
- [x] Rates come from configuration and the record stores the version of the rates used; local rendering defaults to zero
- [x] Amounts are labelled as estimated, never as billed
- [x] The Rendered Video page shows its production cost, including failed attempts that led to it
- [x] The Product page shows total production cost across its Rendered Videos
- [x] Pure-function tests cover the cost calculation

## Comments

- 2026-10-10, implemented. `dotnet test` passed 608 tests, up from 576, with the worker rendering in its own container. The web app passed its typecheck, its lint and the check that its API types are current. Nobody has looked at the new lines on the Variant's page, the library or the Product's page in a browser; they were type-checked and linted only.

  The tests of the calculation were written before the domain code. The tests at the HTTP seam were written after the queue and the endpoints, and passed the first time they ran, so they were never seen to fail.

  What was built:

  - `ProductionCostRecord`, one for each attempt at a render job that ends, saved in the same write as what ended the attempt: the Rendered Video, the failure, the return to the queue. It holds the provider (`Local`), the Scenes of the Storyboard version counted by Technique, how long the attempt took, the attempt number, the estimated amount, the currency and the version of the rates.
  - The rates are settings of the worker, section `ProductionCost`: `RatesVersion` (`1`), `Currency` (`USD`), `LocalPerAttempt` (0), `LocalPerMinute` (0). The estimate is the rate for an attempt plus the rate for a minute for as long as it took (`ProductionCosts.Estimate`).
  - A Rendered Video carries `productionCost`: the attempts of the job that made it and their total. `GET /api/v1/products/{id}/production-cost` adds up every record of a Product.
  - The Variant's page and the library show "Estimated production cost" on each Rendered Video, opening to the attempts. The Product's page has an "Estimated production cost" section.
  - A render job now records when its latest attempt started (`AttemptStartedAt`).
  - CONTEXT.md gained Production cost.

  How each box was checked:

  - A record for each attempt, successful or failed: tests at the HTTP seam with workers of their own. A render that completes; a job that fails three times with nothing to draw with (three records); a Storyboard that cannot be rendered (one); a job whose worker is killed and which another worker completes (a failed attempt and a completed one on the video); a job whose worker is killed on its last attempt (one). The fields are read from the Rendered Video's response.
  - Rates from configuration, the version stored, local defaults to zero: a worker given 250 VND an attempt and 1200 a minute records exactly that for the duration it records, under the version it was given. The shared worker, given nothing, records 0 USD under version `1`.
  - Labelled as estimated, never as billed: by the names (`estimatedAmount`, `estimatedTotals`), the endpoint's summary and the pages' wording. No test reads the wording.
  - The Rendered Video shows its cost with the failed attempts that led to it: the killed-worker test reads both attempts and their sum from the video. The page was not opened.
  - The Product's total: the same tests read it, and one deletes both videos and then the Project and finds the total unchanged.
  - Pure-function tests: the estimate (each rate, both, rounding, a negative duration), which rates are usable, what a record keeps, totals by currency.

  From the code review, which ran on both halves:

  - A worker whose lease had just run out could save its failure at the moment the queue took the job back, and be stopped by the index that keeps an attempt to one record, with an error instead of quietly losing the job. That case is now treated as a lost job.
  - A rate could be set high enough that an estimate would not fit its column, which would have stopped every worker from taking jobs. A rate is now at most a million.
  - Two comments called the cost "spent", a word the glossary avoids. Some names in the queue were made plainer.

  Left as it is:

  - What "duration" and "rate" mean is my reading. Duration is how long the attempt took, not the length of the video. There are two rates, for an attempt and for a minute, where the spec says "a configurable rate". The Technique counts are recorded and have no rate of their own.
  - An attempt a member cancels leaves no record, nor does one whose Project is deleted while it runs. The ticket says "successful or failed".
  - The Product's total is wider than "across its Rendered Videos": it includes jobs that never made a video and videos since deleted, so it can be more than the sum of what its videos show. The page says so.
  - A Rendered Video shows the attempts of the job that made it. A job that failed for good before the member rendered again is in the Product's total only.
  - The duration of an attempt whose worker stopped runs to the end of its lease, so it is up to one lease too long.
  - The Technique counts are of the whole Storyboard version, including Scenes whose clips were reused and not drawn.
  - Nothing ties `RatesVersion` to the rates: whoever changes a rate has to change it.
  - A Technique's name is kept inside the record. Renaming a Technique would need the old records rewritten.
  - Videos rendered before this have no records and show "not recorded".
  - The glossary has no entry for "rates" or "provider", which the API and the pages use.
  - The settings are not in `compose.yaml` or `.env.example`; like the queue's, they are environment variables set on the worker by hand.
