"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { api, type LabDashboardGroup } from "@/lib/api/client";

function number(value: number | null | undefined) {
  return value == null ? "Unknown" : value.toLocaleString("en-US");
}

function Metric({ value, sources }: { value?: number | null; sources: string[] }) {
  return <span title={sources.join("\n") || "No source recorded"}>{number(value)}</span>;
}

function Rate({ value, sources }: { value?: number | null; sources: string[] }) {
  return (
    <span title={sources.join("\n") || "No matching source recorded"}>
      {value == null ? "Not available" : `${(value * 100).toLocaleString("en-US", { maximumFractionDigits: 2 })}%`}
    </span>
  );
}

function GroupTable({ title, groups }: { title: string; groups: LabDashboardGroup[] }) {
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-xl font-semibold tracking-tight">{title}</h2>
      <div className="overflow-x-auto rounded-lg border">
        <table className="w-full min-w-[900px] text-left text-sm">
          <thead className="border-b bg-muted/50 text-muted-foreground">
            <tr>
              <th scope="col" className="p-3">{title.slice(0, -1)}</th>
              <th scope="col" className="p-3 text-right">Posts</th>
              <th scope="col" className="p-3 text-right">Views</th>
              <th scope="col" className="p-3 text-right">Likes</th>
              <th scope="col" className="p-3 text-right">Comments</th>
              <th scope="col" className="p-3 text-right">Shares</th>
              <th scope="col" className="p-3 text-right">Clicks</th>
              <th scope="col" className="p-3 text-right">Click-through</th>
              <th scope="col" className="p-3 text-right">Conversion</th>
              <th scope="col" className="p-3 text-right">Commission</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {groups.map((group) => (
              <tr key={group.key}>
                <th scope="row" className="p-3 font-medium">
                  {group.name}
                  {group.tooSmallToCompare && <span className="block text-xs text-muted-foreground">Too small to compare</span>}
                </th>
                <td className="p-3 text-right tabular-nums" title={group.postSources.join("\n")}>{number(group.posts)}</td>
                <td className="p-3 text-right tabular-nums"><Metric {...group.views} /></td>
                <td className="p-3 text-right tabular-nums"><Metric {...group.likes} /></td>
                <td className="p-3 text-right tabular-nums"><Metric {...group.comments} /></td>
                <td className="p-3 text-right tabular-nums"><Metric {...group.shares} /></td>
                <td className="p-3 text-right tabular-nums"><Metric {...group.clicks} /></td>
                <td className="p-3 text-right tabular-nums"><Rate value={group.clickThroughRate} sources={group.clickThroughRateSources} /></td>
                <td className="p-3 text-right tabular-nums"><Rate value={group.conversionRate} sources={group.conversionRateSources} /></td>
                <td className="p-3 text-right tabular-nums">
                  {group.commission.length === 0 ? "Not recorded" : group.commission.map((total) => (
                    <span key={total.currency} className="block" title={total.sources.join("\n")}>
                      {number(total.net)} {total.currency}
                    </span>
                  ))}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {groups.length === 0 && <p className="p-3 text-sm text-muted-foreground">No records yet.</p>}
      </div>
    </section>
  );
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
          Latest Performance Snapshot for each Published Post. Hover over a figure to see its sources.
          Missing counts stay unknown, and rates appear only when their inputs and attribution are recorded.
        </p>
        <Link href="/lab" className="self-start font-medium underline underline-offset-4">Affiliate Lab</Link>
      </div>
      {dashboard.isError ? <p role="alert" className="text-sm text-destructive">The dashboard could not be loaded.</p>
        : !dashboard.data ? <p className="text-sm text-muted-foreground">Loading…</p>
          : <>
            <p className="text-sm text-muted-foreground">
              A group is too small to compare below {dashboard.data.minimumPosts} Published Posts or {number(dashboard.data.minimumViews)} views.
            </p>
            <GroupTable title="Products" groups={dashboard.data.products} />
            <GroupTable title="Creative templates" groups={dashboard.data.creativeTemplates} />
            <GroupTable title="Hooks" groups={dashboard.data.hooks} />
            <GroupTable title="Campaigns" groups={dashboard.data.campaigns} />
          </>}
    </>
  );
}
