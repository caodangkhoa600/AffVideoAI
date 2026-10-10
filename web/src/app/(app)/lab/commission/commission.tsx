import type { CommissionTotal } from "@/lib/api/client";

/** An amount with its currency. Amounts in different currencies are never added to each other. */
export const formatAmount = (amount: number, currency: string) =>
  `${amount.toLocaleString("en-US", { maximumFractionDigits: 2 })} ${currency}`;

/** A count of orders as it is shown. One the report did not give is said to be unknown, never shown as zero. */
export const formatOrders = (orders: number | null | undefined) => (orders == null ? "Unknown" : orders.toLocaleString());

export const formatPeriod = (start: string, end: string) => (start === end ? start : `${start} to ${end}`);

/** What Commission records add up to, one row for each currency, each with where its figures came from. */
export function CommissionTotals({ totals, testId }: { totals: CommissionTotal[]; testId?: string }) {
  return (
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
            <th className="p-3 font-medium">Source</th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {totals.map((total) => (
            <tr key={total.currency} data-testid="commission-total">
              <td className="p-3 text-right font-semibold tabular-nums">{formatAmount(total.net, total.currency)}</td>
              <td className="p-3 text-right tabular-nums">{formatAmount(total.commission, total.currency)}</td>
              <td className="p-3 text-right tabular-nums">{formatAmount(total.refunds, total.currency)}</td>
              <td className="p-3 text-right tabular-nums">{formatAmount(total.adjustments, total.currency)}</td>
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
                {total.sources.join(", ")} · {total.records === 1 ? "1 record" : `${total.records} records`}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
