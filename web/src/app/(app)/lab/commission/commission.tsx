import type { CommissionSource, CommissionTotal } from "@/lib/api/client";

/** An amount with its currency. Amounts in different currencies are never added to each other. */
export const formatAmount = (amount: number, currency: string) =>
  `${amount.toLocaleString("en-US", { maximumFractionDigits: 2 })} ${currency}`;

/** An adjustment with its sign said: one below zero took Commission away, one above added some. */
export const formatAdjustment = (amount: number, currency: string) =>
  `${amount > 0 ? "+" : ""}${formatAmount(amount, currency)}`;

/** A count of orders as it is shown. One the report did not give is said to be unknown, never shown as zero. */
export const formatOrders = (orders: number | null | undefined) => (orders == null ? "Unknown" : orders.toLocaleString());

export const formatPeriod = (start: string, end: string) => (start === end ? start : `${start} to ${end}`);

const SOURCES: Record<CommissionSource, string> = { Manual: "typed in by hand" };

/** How a Commission figure got in, in words for the member. */
export const commissionSourceName = (source: CommissionSource) => SOURCES[source] ?? source;

/** What a figure was read from and how it got in: every Commission figure is shown with both. */
export const reportsAndSources = (total: Pick<CommissionTotal, "reports" | "sources">) =>
  `${total.reports.join(", ")} · ${total.sources.map(commissionSourceName).join(", ")}`;

/** What Commission records add up to, one row for each currency, each with where its figures came from. */
export function CommissionTotals({ totals, testId }: { totals: CommissionTotal[]; testId?: string }) {
  const overlapping = totals.some((total) => total.overlappingRecords > 0);
  return (
    <>
      <div className="overflow-x-auto rounded-lg border" data-testid={testId}>
        <table className="w-full text-sm whitespace-nowrap">
          <thead className="text-left text-muted-foreground">
            <tr className="border-b">
              <th className="p-3 text-right font-medium">Net</th>
              <th className="p-3 text-right font-medium">Commission</th>
              <th className="p-3 text-right font-medium">Refunds</th>
              <th className="p-3 text-right font-medium">Adjustments</th>
              <th className="p-3 text-right font-medium">Orders</th>
              <th className="p-3 text-right font-medium">Confirmed</th>
              <th className="p-3 font-medium">Period</th>
              <th className="p-3 font-medium">Report and source</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {totals.map((total) => (
              <tr key={total.currency} data-testid="commission-total">
                <td className="p-3 text-right font-semibold tabular-nums">{formatAmount(total.net, total.currency)}</td>
                <td className="p-3 text-right tabular-nums">{formatAmount(total.commission, total.currency)}</td>
                <td className="p-3 text-right tabular-nums">{formatAmount(total.refunds, total.currency)}</td>
                <td className="p-3 text-right tabular-nums">{formatAdjustment(total.adjustments, total.currency)}</td>
                <td className="p-3 text-right tabular-nums">
                  <span className={total.orders == null ? "text-muted-foreground" : undefined}>{formatOrders(total.orders)}</span>
                </td>
                <td className="p-3 text-right tabular-nums">
                  <span className={total.confirmedOrders == null ? "text-muted-foreground" : undefined}>
                    {formatOrders(total.confirmedOrders)}
                  </span>
                </td>
                <td className="p-3">{formatPeriod(total.periodStart, total.periodEnd)}</td>
                <td className="p-3">
                  {reportsAndSources(total)} · {total.records === 1 ? "1 record" : `${total.records} records`}
                  {total.overlappingRecords > 0 && (
                    <span className="text-amber-600 dark:text-amber-400" data-testid="overlap-flag">
                      {" "}
                      · {total.overlappingRecords} overlap
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {overlapping && (
        <p className="rounded-lg border border-amber-500/50 bg-amber-500/10 p-3 text-sm" data-testid="overlap-warning">
          Some of these records cover the same days as another record for the same affiliate link or Product. If two
          reports hold the same orders, that Commission is counted twice here. Check them on the Commission page.
        </p>
      )}
    </>
  );
}
