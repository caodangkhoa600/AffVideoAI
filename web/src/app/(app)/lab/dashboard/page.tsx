"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { api, type CommissionTotal, type LabDashboardGroup, type LabDashboardMetric } from "@/lib/api/client";
import { creativeTemplateName } from "../../projects/creative-templates";
import { formatAmount, formatPeriod, reportsAndSources } from "../commission/commission";
import { METRICS, formatMoment, formatTotal, sourceName } from "../published-posts/performance";

const LOADING = <p className="text-sm text-muted-foreground">Loading…</p>;

const NOT_AVAILABLE = <span className="text-muted-foreground">Not available</span>;

const formatRate = (rate: number) => `${(rate * 100).toLocaleString("en-US", { maximumFractionDigits: 2 })}%`;

const postCount = (count: number) => (count === 1 ? "1 Published Post" : `${count} Published Posts`);

/** Where the performance figures of a row came from: how its snapshots got in and the moments they apply to. */
function performanceSource(group: LabDashboardGroup) {
  const used = METRICS.map(({ key }) => group[key]).filter((metric) => metric.earliestTakenAt && metric.latestTakenAt);
  if (used.length === 0) return "No Performance Snapshot";
  const sources = [...new Set(used.flatMap((metric) => metric.sources))].map(sourceName).join(", ");
  const earliest = used.map((metric) => metric.earliestTakenAt as string).sort()[0];
  const latest = used.map((metric) => metric.latestTakenAt as string).sort().at(-1) as string;
  return `${sources}, totals as of ${earliest === latest ? formatMoment(latest) : `${formatMoment(earliest)} to ${formatMoment(latest)}`}`;
}

export default function LabDashboardPage() {
  const dashboard = useQuery({
    queryKey: ["lab", "dashboard"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/lab/dashboard");
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  return (
    <>
      <div className="flex flex-col gap-1">
        <h1 className="text-3xl font-semibold tracking-tight">Affiliate Lab dashboard</h1>
        <p className="text-sm text-muted-foreground">
          What your Published Posts add up to, grouped four ways, from the latest Performance Snapshot of each. A
          figure that is not known is shown as unknown, never as zero, and a rate is shown only when everything it is
          worked out from is recorded. No group is ranked against another.{" "}
          <Link href="/lab" className="font-medium underline underline-offset-4">
            Affiliate Lab
          </Link>
        </p>
      </div>
      {dashboard.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The dashboard could not be loaded.
        </p>
      ) : !dashboard.data ? (
        LOADING
      ) : (
        <>
          <ul className="list-disc pl-5 text-sm text-muted-foreground" data-testid="dashboard-rules">
            <li>
              A group with fewer than {postCount(dashboard.data.minimumPublishedPosts)}, or fewer than{" "}
              {dashboard.data.minimumViews.toLocaleString()} views, or views that are not all known, is marked too small
              to compare. The mark is on the whole row, its Commission included.
            </li>
            <li>Click-through is clicks for each view, when every Published Post in the group knows both.</li>
            <li>
              Conversion is orders for each click, from affiliate links whose every Published Post is in the group. It
              is not available when a Published Post has no such link, when a record leaves its orders out, or when a
              record covers days before the link was first published. Orders recorded for a Product make no rate.
            </li>
            <li>
              Commission is what is recorded for the affiliate links whose every Published Post is in the group. It is
              never divided between Published Posts, and a link that reaches outside the group is left out.
            </li>
          </ul>
          <GroupTable title="Products" heading="Product" groups={dashboard.data.products} withProductCommission />
          <GroupTable
            title="Creative templates"
            heading="Creative template"
            groups={dashboard.data.creativeTemplates}
            name={(group) => creativeTemplateName(group.key as Parameters<typeof creativeTemplateName>[0])}
          />
          <GroupTable title="Hooks" heading="Hook" groups={dashboard.data.hooks} />
          <GroupTable
            title="Campaigns"
            heading="Campaign"
            groups={dashboard.data.campaigns}
            note="A Variant can be in more than one Campaign, and then counts in each. Do not add the figures of two Campaigns together."
          />
        </>
      )}
    </>
  );
}

function GroupTable({
  title,
  heading,
  groups,
  name = (group) => group.name,
  note,
  withProductCommission = false,
}: {
  title: string;
  heading: string;
  groups: LabDashboardGroup[];
  name?: (group: LabDashboardGroup) => string;
  note?: string;
  withProductCommission?: boolean;
}) {
  const columns = 9 + (withProductCommission ? 1 : 0);
  return (
    <section className="flex flex-col gap-3" data-testid={`dashboard-${heading.toLowerCase().replace(" ", "-")}`}>
      <h2 className="text-xl font-semibold tracking-tight">
        {title}
        <span className="text-muted-foreground"> · {groups.length}</span>
      </h2>
      {note && <p className="text-sm text-muted-foreground">{note}</p>}
      {groups.length === 0 ? (
        <p className="text-sm text-muted-foreground">Nothing to show yet.</p>
      ) : (
        <div className="overflow-x-auto rounded-lg border">
          <table className="w-full text-left text-sm">
            <thead className="text-muted-foreground">
              <tr className="border-b">
                <th scope="col" className="p-3 font-medium">{heading}</th>
                {METRICS.map(({ metric }) => (
                  <th key={metric} scope="col" className="p-3 text-right font-medium">
                    {metric}
                  </th>
                ))}
                <th scope="col" className="p-3 text-right font-medium">Click-through</th>
                <th scope="col" className="p-3 text-right font-medium">Conversion</th>
                <th scope="col" className="p-3 font-medium">{withProductCommission ? "Commission for its links" : "Commission"}</th>
                {withProductCommission && <th scope="col" className="p-3 font-medium">Commission for the Product</th>}
              </tr>
            </thead>
            {groups.map((group) => (
              <tbody key={group.key} className="border-b last:border-b-0" data-testid="dashboard-group">
                <tr>
                  <th scope="row" className="max-w-xs p-3 align-top font-medium break-words">
                    {name(group)}
                    <span className="block text-xs font-normal text-muted-foreground">{postCount(group.publishedPosts)}</span>
                    {group.tooSmallToCompare && (
                      <span className="block text-xs font-normal text-amber-600 dark:text-amber-400" data-testid="too-small">
                        Too small to compare
                      </span>
                    )}
                  </th>
                  {METRICS.map(({ metric, key }) => (
                    <td key={metric} className="p-3 text-right align-top whitespace-nowrap tabular-nums">
                      <Figure metric={group[key]} publishedPosts={group.publishedPosts} />
                    </td>
                  ))}
                  <td className="p-3 text-right align-top whitespace-nowrap tabular-nums">
                    {group.clickThroughRate == null ? NOT_AVAILABLE : formatRate(group.clickThroughRate)}
                  </td>
                  <td className="p-3 text-right align-top whitespace-nowrap tabular-nums">
                    {group.conversionRate == null ? NOT_AVAILABLE : formatRate(group.conversionRate)}
                  </td>
                  <td className="p-3 align-top">
                    <Commission totals={group.recordedForLinks} beforePublication={group.recordsBeforePublication} />
                  </td>
                  {withProductCommission && (
                    <td className="p-3 align-top">
                      <Commission totals={group.recordedForProduct} beforePublication={0} />
                    </td>
                  )}
                </tr>
                <tr>
                  <td colSpan={columns} className="px-3 pb-3 text-xs text-muted-foreground" data-testid="performance-source">
                    Performance: {performanceSource(group)}
                  </td>
                </tr>
              </tbody>
            ))}
          </table>
        </div>
      )}
      {withProductCommission && groups.length > 0 && (
        <p className="text-sm text-muted-foreground">
          The two Commission columns are never added to each other: a report for a Product may already hold what a
          report for one of its affiliate links holds.
        </p>
      )}
    </section>
  );
}

/** One figure of a group. Unknown unless every Published Post knows it, with how many do not. */
function Figure({ metric, publishedPosts }: { metric: LabDashboardMetric; publishedPosts: number }) {
  if (metric.value != null) return formatTotal(metric.value);
  return (
    <span className="text-muted-foreground">
      Unknown
      {metric.unknown > 0 && (
        <span className="block text-xs">
          {metric.unknown} of {publishedPosts} not known
        </span>
      )}
    </span>
  );
}

/** What is recorded, one line for each currency, each with its report, its source and the days it covers. */
function Commission({ totals, beforePublication }: { totals: CommissionTotal[]; beforePublication: number }) {
  if (totals.length === 0) return <span className="text-muted-foreground">Not recorded</span>;
  return (
    <>
      {totals.map((total) => (
        <span key={total.currency} className="block" data-testid="dashboard-commission">
          <span className="font-medium whitespace-nowrap tabular-nums">{formatAmount(total.net, total.currency)} net</span>
          <span className="block text-xs text-muted-foreground">
            {reportsAndSources(total)} · {formatPeriod(total.periodStart, total.periodEnd)}
          </span>
          {total.overlappingRecords > 0 && (
            <span className="block text-xs text-amber-600 dark:text-amber-400" data-testid="overlap-flag">
              {total.overlappingRecords} records overlap: part of this may be counted twice
            </span>
          )}
        </span>
      ))}
      {beforePublication > 0 && (
        <span className="block text-xs text-amber-600 dark:text-amber-400" data-testid="before-publication">
          {beforePublication === 1 ? "1 record covers" : `${beforePublication} records cover`} days before the link was
          first published
        </span>
      )}
    </>
  );
}
