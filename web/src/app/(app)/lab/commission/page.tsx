"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Field } from "@/components/field";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { api, fieldErrors, problemDetail, type CommissionRecord } from "@/lib/api/client";
import { commissionSourceName, formatAdjustment, formatAmount, formatOrders, formatPeriod } from "./commission";

const LOADING = <p className="text-sm text-muted-foreground">Loading…</p>;

// The most the API serves in one page; the count says so if there are more.
const PAGE_SIZE = 200;

// The API's limit (CommissionRecord in the domain).
const REPORT_MAX_LENGTH = 100;

const SELECT =
  "h-8 w-full rounded-lg border border-input bg-transparent px-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30";

const AMOUNT = /^\d{1,13}(\.\d{1,2})?$/;
// An adjustment takes Commission away when it is below zero.
const SIGNED_AMOUNT = /^-?\d{1,13}(\.\d{1,2})?$/;
const COUNT = /^\d{1,9}$/;

/** What a record is attached to, as the select names it: a link or a Product, with its identifier. */
type Target = `link:${string}` | `product:${string}` | "";

// What the form starts attached to is in the address, which is only known once the
// page is asked for, so it is drawn inside a Suspense boundary.
export default function CommissionPage() {
  return (
    <>
      <div className="flex flex-col gap-1">
        <h1 className="text-3xl font-semibold tracking-tight">Commission</h1>
        <p className="text-sm text-muted-foreground">
          What your affiliate reports say, recorded at the level they give it: for an affiliate link or for a Product.
          It is never divided between the Published Posts that share an affiliate link.{" "}
          <Link href="/lab" className="font-medium underline underline-offset-4">
            Affiliate Lab
          </Link>
        </p>
      </div>
      <Suspense fallback={LOADING}>
        <Commission />
      </Suspense>
    </>
  );
}

function usePaged<T>(queryKey: unknown[], load: () => Promise<{ data?: T; response: Response }>) {
  return useQuery({
    queryKey,
    queryFn: async () => {
      const { data, response } = await load();
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
}

function Commission() {
  const queryClient = useQueryClient();
  // A Published Post shows the Commission of its own link too.
  const reload = () => queryClient.invalidateQueries({ queryKey: ["lab"] });
  const asked = useSearchParams();

  const links = usePaged(["lab", "publishing", "links"], () =>
    api.GET("/api/v1/lab/affiliate-links", { params: { query: { pageSize: PAGE_SIZE } } }),
  );
  const products = usePaged(["lab", "products", { pageSize: PAGE_SIZE }], () =>
    api.GET("/api/v1/lab/products", { params: { query: { pageSize: PAGE_SIZE } } }),
  );
  const records = usePaged(["lab", "commission", "records"], () =>
    api.GET("/api/v1/lab/commission-records", { params: { query: { pageSize: PAGE_SIZE } } }),
  );

  if (links.isError || products.isError || records.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        The Commission records could not be loaded.
      </p>
    );
  }
  if (!links.data || !products.data || !records.data) return LOADING;

  const targets: [Target, string][] = [
    ...links.data.items.map((link): [Target, string] => [
      `link:${link.id}`,
      `Affiliate link · ${link.label || link.url} · ${postCount(link.publishedPostCount)}`,
    ]),
    ...products.data.items.map((product): [Target, string] => [`product:${product.productId}`, `Product · ${product.name}`]),
  ];
  const names = new Map(targets);
  const fromAddress: Target = asked.get("affiliateLinkId")
    ? `link:${asked.get("affiliateLinkId")}`
    : asked.get("productId")
      ? `product:${asked.get("productId")}`
      : "";

  return (
    <>
      <RecordCommission targets={targets} startsAt={names.has(fromAddress) ? fromAddress : ""} onRecorded={reload} />
      <section className="flex flex-col gap-4" data-testid="commission-records">
        <h2 className="text-xl font-semibold tracking-tight">
          Commission records<span className="text-muted-foreground"> · {records.data.total}</span>
        </h2>
        {records.data.items.length === 0 ? (
          <p className="text-sm text-muted-foreground">No Commission is recorded yet.</p>
        ) : (
          <div className="overflow-x-auto rounded-lg border">
            <table className="w-full text-sm whitespace-nowrap">
              <thead className="text-left text-muted-foreground">
                <tr className="border-b">
                  <th className="p-3 font-medium">Period</th>
                  <th className="p-3 font-medium">Attached to</th>
                  <th className="p-3 font-medium">Report</th>
                  <th className="p-3 text-right font-medium">Orders</th>
                  <th className="p-3 text-right font-medium">Confirmed</th>
                  <th className="p-3 text-right font-medium">Commission</th>
                  <th className="p-3 text-right font-medium">Refunds</th>
                  <th className="p-3 text-right font-medium">Adjustments</th>
                  <th className="p-3 text-right font-medium">Net</th>
                  <th className="p-3 font-medium">Source</th>
                  <th className="p-3" />
                </tr>
              </thead>
              <tbody className="divide-y">
                {records.data.items.map((record) => (
                  <RecordRow
                    key={record.id}
                    record={record}
                    attachedTo={
                      names.get(record.affiliateLinkId ? `link:${record.affiliateLinkId}` : `product:${record.productId}`) ??
                      (record.affiliateLinkId ? "An affiliate link" : "A Product")
                    }
                    onDeleted={reload}
                  />
                ))}
              </tbody>
            </table>
          </div>
        )}
        {records.data.items.some((record) => record.overlapsAnother) && (
          <p className="rounded-lg border border-amber-500/50 bg-amber-500/10 p-3 text-sm" data-testid="overlap-warning">
            The records marked &quot;overlaps&quot; cover some of the same days as another record for the same affiliate
            link or Product, in the same currency. If two reports hold the same orders, that Commission is counted twice.
            Delete the one that should not be there; leave them if the reports really are of different orders.
          </p>
        )}
        {records.data.total > records.data.items.length && (
          <p className="text-sm text-muted-foreground">
            Showing the latest {records.data.items.length} of {records.data.total}.
          </p>
        )}
      </section>
    </>
  );
}

const postCount = (count: number) =>
  count === 0 ? "no Published Post" : count === 1 ? "1 Published Post" : `shared by ${count} Published Posts`;

/** Today, as a date input writes it, where the member is. */
function today() {
  const now = new Date();
  return new Date(now.getTime() - now.getTimezoneOffset() * 60_000).toISOString().slice(0, 10);
}

const FIGURES = [
  { key: "commission", label: "Commission", kind: "amount", placeholder: "" },
  { key: "refunds", label: "Refunds", kind: "amount", placeholder: "0" },
  { key: "adjustments", label: "Adjustments (+ or -)", kind: "signed", placeholder: "0" },
  { key: "orders", label: "Orders", kind: "count", placeholder: "Unknown" },
  { key: "confirmedOrders", label: "Confirmed orders", kind: "count", placeholder: "Unknown" },
] as const;

function RecordCommission({
  targets,
  startsAt,
  onRecorded,
}: {
  targets: [Target, string][];
  startsAt: Target;
  onRecorded: () => Promise<void>;
}) {
  const [target, setTarget] = useState<Target>(startsAt);
  const [periodStart, setPeriodStart] = useState(today);
  const [periodEnd, setPeriodEnd] = useState(today);
  const [report, setReport] = useState("");
  const [currency, setCurrency] = useState("VND");
  const [figures, setFigures] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [refused, setRefused] = useState<string>();
  const [recorded, setRecorded] = useState(false);

  const record = async (event: React.FormEvent) => {
    event.preventDefault();
    setRecorded(false);
    setRefused(undefined);
    const wrong: Record<string, string> = {};
    if (!target) wrong.affiliateLinkId = "Choose the affiliate link or the Product the report gives the figures for.";
    if (!periodStart) wrong.periodStart = "Enter the first day the figures cover.";
    if (!periodEnd) wrong.periodEnd = "Enter the last day the figures cover.";
    else if (periodStart && periodEnd < periodStart) wrong.periodEnd = "The period cannot end before it starts.";
    if (report.trim() === "") wrong.report = "Enter the report the figures are from.";
    if (!/^[A-Z]{3}$/.test(currency)) wrong.currency = "Use a three-letter currency code in capitals, such as VND.";
    const typed = Object.fromEntries(FIGURES.map(({ key }) => [key, (figures[key] ?? "").trim()]));
    for (const { key, kind } of FIGURES) {
      if (typed[key] === "") continue;
      if (kind === "amount" && !AMOUNT.test(typed[key])) wrong[key] = "Enter an amount of zero or more, with at most two decimal places.";
      if (kind === "signed" && !SIGNED_AMOUNT.test(typed[key])) {
        wrong[key] = "Enter an amount with at most two decimal places. Start it with - when Commission was taken away.";
      }
      if (kind === "count" && !COUNT.test(typed[key])) wrong[key] = "Enter a whole number of zero or more, or leave it empty.";
    }
    if (typed.commission === "") wrong.commission = "Enter the Commission the report gives. Zero is a figure.";
    setErrors(wrong);
    if (Object.keys(wrong).length > 0) return;

    const [kind, id] = target.split(":");
    setSaving(true);
    const { error, response } = await api
      .POST("/api/v1/lab/commission-records", {
        body: {
          affiliateLinkId: kind === "link" ? id : null,
          productId: kind === "product" ? id : null,
          periodStart,
          periodEnd,
          report,
          currency,
          commission: Number(typed.commission),
          // Nothing was taken back or added unless the report says so. Orders left empty are unknown, which is not zero.
          refunds: Number(typed.refunds || "0"),
          adjustments: Number(typed.adjustments || "0"),
          orders: typed.orders === "" ? null : Number(typed.orders),
          confirmedOrders: typed.confirmedOrders === "" ? null : Number(typed.confirmedOrders),
        },
      })
      .catch(() => ({ error: undefined, response: undefined }));
    if (response?.ok) {
      // What it is attached to, the report and the currency stay: the next record is often the next period of the same report.
      setFigures({});
      setRecorded(true);
      await onRecorded();
    } else if (response?.status === 400) {
      const byField = Object.fromEntries(Object.entries(fieldErrors(error)).map(([field, said]) => [field, said.join(" ")]));
      // The select stands for both identifiers.
      if (byField.productId) byField.affiliateLinkId = byField.productId;
      setErrors(byField);
      if (Object.keys(byField).length === 0) setRefused("The Commission record could not be recorded.");
    } else {
      setRefused(problemDetail(error) ?? "The Commission record could not be recorded.");
    }
    setSaving(false);
  };

  return (
    <section className="flex flex-col gap-4" data-testid="record-commission">
      <h2 className="text-xl font-semibold tracking-tight">Record Commission</h2>
      <p className="text-sm text-muted-foreground">
        Type what the report says for one period. Refunds are Commission that was taken back because orders were
        refunded: they reduce the net figure. An adjustment is anything else the programme changed: start it with -
        when Commission was taken away, and leave it plain when some was added, such as a bonus. Leave the orders empty
        when the report does not give them: empty is kept as unknown, not as zero. A record is not changed afterwards;
        delete a wrong one and record it again.
      </p>
      <form onSubmit={record} noValidate className="flex flex-col gap-4">
        <div className="flex max-w-xl flex-col gap-2">
          <Label htmlFor="commission-target">Attached to</Label>
          <select
            id="commission-target"
            className={SELECT}
            value={target}
            aria-invalid={errors.affiliateLinkId ? true : undefined}
            aria-describedby={errors.affiliateLinkId ? "commission-target-error" : undefined}
            onChange={(event) => setTarget(event.target.value as Target)}
          >
            <option value="">
              {targets.length === 0 ? "Add an affiliate link or a Product first" : "Choose an affiliate link or a Product"}
            </option>
            {targets.map(([value, name]) => (
              <option key={value} value={value}>
                {name}
              </option>
            ))}
          </select>
          {errors.affiliateLinkId && (
            <p id="commission-target-error" role="alert" className="text-sm text-destructive">
              {errors.affiliateLinkId}
            </p>
          )}
        </div>
        <div className="grid max-w-xl gap-4 sm:grid-cols-2">
          <Field
            id="commission-period-start"
            label="From"
            type="date"
            value={periodStart}
            onChange={(event) => setPeriodStart(event.target.value)}
            error={errors.periodStart}
          />
          <Field
            id="commission-period-end"
            label="To"
            type="date"
            value={periodEnd}
            onChange={(event) => setPeriodEnd(event.target.value)}
            error={errors.periodEnd}
          />
          <Field
            id="commission-report"
            label="Report"
            placeholder="Shopee Affiliate"
            value={report}
            maxLength={REPORT_MAX_LENGTH}
            onChange={(event) => setReport(event.target.value)}
            error={errors.report}
          />
          <Field
            id="commission-currency"
            label="Currency"
            value={currency}
            maxLength={3}
            onChange={(event) => setCurrency(event.target.value.toUpperCase())}
            error={errors.currency}
          />
        </div>
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-5">
          {FIGURES.map(({ key, label, kind, placeholder }) => (
            <Field
              key={key}
              id={`commission-${key}`}
              label={label}
              // Text, not a number input: that one hands over nothing for what it cannot read, and nothing means unknown.
              inputMode={kind === "count" ? "numeric" : kind === "signed" ? "text" : "decimal"}
              maxLength={16}
              placeholder={placeholder}
              value={figures[key] ?? ""}
              onChange={(event) => setFigures({ ...figures, [key]: event.target.value })}
              error={errors[key]}
            />
          ))}
        </div>
        {refused && (
          <p role="alert" className="text-sm text-destructive">
            {refused}
          </p>
        )}
        <div className="flex items-center gap-3">
          <Button type="submit" disabled={saving}>
            {saving ? "Recording…" : "Record Commission"}
          </Button>
          {recorded && (
            <span role="status" className="text-sm text-muted-foreground">
              Recorded.
            </span>
          )}
        </div>
      </form>
    </section>
  );
}

function RecordRow({
  record,
  attachedTo,
  onDeleted,
}: {
  record: CommissionRecord;
  attachedTo: string;
  onDeleted: () => Promise<void>;
}) {
  const [confirming, setConfirming] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [refused, setRefused] = useState(false);

  const remove = async () => {
    setDeleting(true);
    const { response } = await api
      .DELETE("/api/v1/lab/commission-records/{recordId}", { params: { path: { recordId: record.id } } })
      .catch(() => ({ response: undefined }));
    // One that is already gone is as deleted as was asked for.
    if (response?.ok || response?.status === 404) await onDeleted();
    else setRefused(true);
    setDeleting(false);
    setConfirming(false);
  };

  return (
    <tr data-testid="commission-record">
      <td className="p-3">
        {formatPeriod(record.periodStart, record.periodEnd)}
        {record.overlapsAnother && (
          <span className="text-amber-600 dark:text-amber-400" data-testid="overlap-flag" title="Covers some of the same days as another record for the same affiliate link or Product">
            {" "}
            · overlaps
          </span>
        )}
      </td>
      <td className="max-w-xs truncate p-3" title={attachedTo}>
        {attachedTo}
      </td>
      <td className="p-3">{record.report}</td>
      <td className="p-3 text-right tabular-nums">
        <span className={record.orders == null ? "text-muted-foreground" : undefined}>{formatOrders(record.orders)}</span>
      </td>
      <td className="p-3 text-right tabular-nums">
        <span className={record.confirmedOrders == null ? "text-muted-foreground" : undefined}>
          {formatOrders(record.confirmedOrders)}
        </span>
      </td>
      <td className="p-3 text-right tabular-nums">{formatAmount(record.commission, record.currency)}</td>
      <td className="p-3 text-right tabular-nums">{formatAmount(record.refunds, record.currency)}</td>
      <td className="p-3 text-right tabular-nums">{formatAdjustment(record.adjustments, record.currency)}</td>
      <td className="p-3 text-right font-semibold tabular-nums">{formatAmount(record.net, record.currency)}</td>
      <td className="p-3">
        {commissionSourceName(record.source)}, {new Date(record.recordedAt).toLocaleString()}
      </td>
      <td className="p-3 text-right">
        {confirming ? (
          <span className="flex items-center justify-end gap-2">
            <Button type="button" variant="destructive" size="sm" disabled={deleting} onClick={remove}>
              {deleting ? "Deleting…" : "Delete record"}
            </Button>
            <Button type="button" variant="ghost" size="sm" disabled={deleting} onClick={() => setConfirming(false)}>
              Keep
            </Button>
          </span>
        ) : (
          <Button type="button" variant="ghost" size="sm" onClick={() => setConfirming(true)}>
            Delete
          </Button>
        )}
        {refused && (
          <p role="alert" className="text-sm text-destructive">
            The record could not be deleted.
          </p>
        )}
      </td>
    </tr>
  );
}
