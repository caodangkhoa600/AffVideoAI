import type { EstimatedAmount, RenderedVideo } from "@/lib/api/client";
import { TECHNIQUES } from "../projects/techniques";

/** Estimated totals as a member reads them: one amount for each currency, never added to each other. */
export function formatEstimates(totals: EstimatedAmount[]): string {
  return totals
    .map((total) => `${total.amount.toLocaleString("en-US", { maximumFractionDigits: 6 })} ${total.currency}`)
    .join(" + ");
}

/**
 * What rendering a Rendered Video is estimated to have cost: the total, and each attempt of the job that made it,
 * the failed ones included. Every amount is an estimate from the configured rates, and is always called one.
 */
export function RenderedVideoCost({ video }: { video: RenderedVideo }) {
  const { estimatedTotals, attempts } = video.productionCost;
  if (attempts.length === 0) {
    return (
      <p className="text-sm text-muted-foreground" data-testid="rendered-video-cost">
        Estimated production cost: not recorded. This video was rendered before costs were recorded.
      </p>
    );
  }

  const failed = attempts.filter((attempt) => attempt.outcome === "Failed").length;
  return (
    <details className="text-sm text-muted-foreground" data-testid="rendered-video-cost">
      <summary className="cursor-pointer">
        Estimated production cost: <span className="font-medium text-foreground">{formatEstimates(estimatedTotals)}</span> ·{" "}
        {attempts.length === 1 ? "1 attempt" : `${attempts.length} attempts`}
        {failed > 0 && `, ${failed} failed`}
      </summary>
      <ul className="mt-1 flex flex-col gap-1 pl-4">
        {attempts.map((attempt) => (
          <li key={attempt.attempt} data-testid="rendered-video-cost-attempt">
            Attempt {attempt.attempt} {attempt.outcome === "Failed" ? "failed" : "made the video"} after{" "}
            {(attempt.durationMs / 1000).toFixed(1)} s · estimated {formatEstimates([{ amount: attempt.estimatedAmount, currency: attempt.currency }])}{" "}
            at rates “{attempt.ratesVersion}” · {attempt.provider === "Local" ? "rendered locally" : attempt.provider} ·{" "}
            {attempt.techniqueCounts.map((count) => `${count.scenes} × ${TECHNIQUES[count.technique]}`).join(", ")}
          </li>
        ))}
      </ul>
      <p className="mt-1 pl-4">An estimate from the rates configured for the worker. Nobody was billed this.</p>
    </details>
  );
}
