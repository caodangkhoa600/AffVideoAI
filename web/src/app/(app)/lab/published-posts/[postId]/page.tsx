"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Field } from "@/components/field";
import { Button } from "@/components/ui/button";
import {
  api,
  fieldErrors,
  type PerformanceMetric,
  type PerformanceSnapshot,
  type PublishedPost,
} from "@/lib/api/client";
import { creativeTemplateName } from "../../../projects/creative-templates";
import { CommissionTotals } from "../../commission/commission";
import { METRICS, formatMoment, formatTotal, sourceName } from "../performance";

const LOADING = <p className="text-sm text-muted-foreground">Loading…</p>;

// The most the API serves in one page; the count says so if there are more.
const PAGE_SIZE = 200;

/** Now, as a date and time input writes it, where the member is. */
function now() {
  const date = new Date();
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

const metricList = (metrics: PerformanceMetric[]) => metrics.map((metric) => metric.toLowerCase()).join(", ");

// Which Published Post is only known once the page is asked for, so it is drawn inside a
// Suspense boundary: the build cannot prerender what depends on the URL.
export default function PublishedPostPage() {
  return (
    <Suspense fallback={LOADING}>
      <RequestedPost />
    </Suspense>
  );
}

function RequestedPost() {
  const { postId } = useParams<{ postId: string }>();
  const post = useQuery({
    queryKey: ["lab", "publishing", "post", postId],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/lab/published-posts/{postId}", {
        params: { path: { postId } },
      });
      // 400 is an identifier that is not one: as absent as one that matches nothing.
      if (response.status === 404 || response.status === 400) return null;
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  if (post.isError || post.data === null) {
    return (
      <>
        <h1 className="text-3xl font-semibold tracking-tight">Published Post</h1>
        <p role="alert" className="text-sm text-destructive">
          {post.isError ? "The Published Post could not be loaded." : "There is no such Published Post in your Organization."}
        </p>
        <Link href="/lab/published-posts" className="font-medium underline underline-offset-4">
          Published Posts
        </Link>
      </>
    );
  }
  if (!post.data) return LOADING;
  return <PostDetails post={post.data} />;
}

function PostDetails({ post }: { post: PublishedPost }) {
  const queryClient = useQueryClient();
  // The list of Published Posts shows the current figure too.
  const reload = () => queryClient.invalidateQueries({ queryKey: ["lab", "publishing"] });

  const snapshots = useQuery({
    queryKey: ["lab", "publishing", "post", post.id, "snapshots"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/lab/published-posts/{postId}/performance-snapshots", {
        params: { path: { postId: post.id }, query: { pageSize: PAGE_SIZE } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  return (
    <>
      <div className="flex flex-col gap-1">
        <h1 className="text-3xl font-semibold tracking-tight break-words" data-testid="published-post-hook">
          {post.hook}
        </h1>
        <p className="text-sm text-muted-foreground">
          Published Post · {post.productName} · {creativeTemplateName(post.creativeTemplate)} · {post.socialAccount.platform}{" "}
          · {post.socialAccount.handle} · {post.publishedOn}
        </p>
        <a href={post.url} target="_blank" rel="noopener noreferrer" className="break-all text-sm underline underline-offset-4">
          {post.url}
        </a>
        <Link href="/lab/published-posts" className="self-start font-medium underline underline-offset-4">
          Published Posts
        </Link>
      </div>
      <CurrentFigures post={post} />
      <Commission post={post} />
      <EnterSnapshot postId={post.id} onRecorded={reload} />
      {snapshots.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The Performance Snapshots could not be loaded.
        </p>
      ) : !snapshots.data ? (
        LOADING
      ) : (
        <>
          <ViewsOverTime snapshots={snapshots.data.items} total={snapshots.data.total} />
          <History snapshots={snapshots.data.items} total={snapshots.data.total} />
        </>
      )}
    </>
  );
}

function CurrentFigures({ post }: { post: PublishedPost }) {
  const current = post.currentPerformance;
  return (
    <section className="flex flex-col gap-4" data-testid="current-figures">
      <h2 className="text-xl font-semibold tracking-tight">Current figures</h2>
      {!current ? (
        <p className="text-sm text-muted-foreground">No Performance Snapshot yet. Enter the first one below.</p>
      ) : (
        <>
          <dl className="grid grid-cols-2 gap-4 sm:grid-cols-5">
            {METRICS.map(({ metric, key }) => (
              <div key={metric} className="flex flex-col gap-1 rounded-lg border p-4" data-testid={`current-${key}`}>
                <dt className="text-sm text-muted-foreground">{metric}</dt>
                <dd className={current[key] == null ? "text-sm text-muted-foreground" : "text-2xl font-semibold tabular-nums"}>
                  {formatTotal(current[key])}
                </dd>
              </div>
            ))}
          </dl>
          <p className="text-sm text-muted-foreground" data-testid="current-source">
            {sourceName(current.source)} · totals as of {formatMoment(current.takenAt)} · entered {formatMoment(current.recordedAt)}
          </p>
        </>
      )}
    </section>
  );
}

/**
 * What the affiliate reports say this Published Post earned. That is only known while no other
 * Published Post carries its affiliate link; otherwise it is shown for the link and for the Product.
 */
function Commission({ post }: { post: PublishedPost }) {
  const link = post.affiliateLink;
  return (
    <section className="flex flex-col gap-4" data-testid="post-commission">
      <h2 className="text-xl font-semibold tracking-tight">Commission</h2>
      {!link ? (
        <p className="text-sm text-muted-foreground" data-testid="commission-no-link">
          This Published Post carries no affiliate link, so no Commission can be shown for it.
        </p>
      ) : post.commission ? (
        <>
          <p className="text-sm text-muted-foreground">
            No other Published Post carries its affiliate link (<span className="break-all">{link.label || link.url}</span>
            ), so what is recorded for the link is what this Published Post earned.
          </p>
          {post.commission.length === 0 ? (
            <p className="text-sm text-muted-foreground">No Commission is recorded for the link yet.</p>
          ) : (
            <CommissionTotals totals={post.commission} testId="commission-of-post" />
          )}
        </>
      ) : (
        <SharedCommission post={post} linkId={link.id} />
      )}
      {link && (
        <Link href={`/lab/commission?affiliateLinkId=${link.id}`} className="self-start font-medium underline underline-offset-4">
          Record Commission for this link
        </Link>
      )}
    </section>
  );
}

function SharedCommission({ post, linkId }: { post: PublishedPost; linkId: string }) {
  const ofLink = useQuery({
    queryKey: ["lab", "commission", "link", linkId],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/lab/affiliate-links/{linkId}/commission", {
        params: { path: { linkId } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
  const ofProduct = useQuery({
    queryKey: ["lab", "commission", "product", post.productId],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/lab/products/{productId}/commission", {
        params: { path: { productId: post.productId } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  if (ofLink.isError || ofProduct.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        The Commission could not be loaded.
      </p>
    );
  }
  if (!ofLink.data || !ofProduct.data) return LOADING;
  return (
    <>
      <p className="rounded-lg border border-amber-500/50 bg-amber-500/10 p-3 text-sm" data-testid="commission-shared-note">
        Its affiliate link (<span className="break-all">{ofLink.data.label || ofLink.data.url}</span>) is carried by{" "}
        {ofLink.data.publishedPostCount} Published Posts. The report gives one figure for the link, and nothing says
        which Published Post earned how much of it, so Commission cannot be split by post. It is shown for the link and
        for the Product.
      </p>
      <h3 className="font-medium">For the affiliate link, all {ofLink.data.publishedPostCount} Published Posts together</h3>
      {ofLink.data.totals.length === 0 ? (
        <p className="text-sm text-muted-foreground">No Commission is recorded for the link yet.</p>
      ) : (
        <CommissionTotals totals={ofLink.data.totals} testId="commission-of-link" />
      )}
      <h3 className="font-medium">For the Product, {ofProduct.data.productName}</h3>
      {ofProduct.data.totals.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          No Commission is recorded for the Product, or for an affiliate link that only its Published Posts carry.
        </p>
      ) : (
        <CommissionTotals totals={ofProduct.data.totals} testId="commission-of-product" />
      )}
    </>
  );
}

function EnterSnapshot({ postId, onRecorded }: { postId: string; onRecorded: () => Promise<void> }) {
  const [takenAt, setTakenAt] = useState(now);
  const [figures, setFigures] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [refused, setRefused] = useState<string>();
  const [recorded, setRecorded] = useState<PerformanceSnapshot>();

  const record = async (event: React.FormEvent) => {
    event.preventDefault();
    setRecorded(undefined);
    setRefused(undefined);
    const wrong: Record<string, string> = {};
    const totals: Record<string, number | null> = {};
    for (const { key } of METRICS) {
      const typed = (figures[key] ?? "").trim();
      if (typed !== "" && !/^\d+$/.test(typed)) wrong[key] = "Enter a whole number of zero or more, or leave it empty.";
      // Empty is unknown, which is not zero.
      totals[key] = typed === "" ? null : Number(typed);
    }
    const read = new Date(takenAt);
    if (!takenAt || Number.isNaN(read.getTime())) wrong.takenAt = "Enter when you read the figures.";
    if (Object.keys(wrong).length === 0 && Object.values(totals).every((value) => value === null)) {
      wrong.views = "Enter at least one figure.";
    }
    setErrors(wrong);
    if (Object.keys(wrong).length > 0) return;

    setSaving(true);
    const { data, error, response } = await api
      .POST("/api/v1/lab/published-posts/{postId}/performance-snapshots", {
        params: { path: { postId } },
        body: { takenAt: read.toISOString(), ...totals },
      })
      .catch(() => ({ data: undefined, error: undefined, response: undefined }));
    if (response?.ok && data) {
      setFigures({});
      setTakenAt(now());
      setRecorded(data);
      await onRecorded();
    } else {
      const byField = Object.fromEntries(Object.entries(fieldErrors(error)).map(([field, said]) => [field, said.join(" ")]));
      setErrors(byField);
      // Said under the form unless a field of it says why.
      if (!["takenAt", ...METRICS.map(({ key }) => key)].some((field) => field in byField)) {
        setRefused("The Performance Snapshot could not be recorded.");
      }
    }
    setSaving(false);
  };

  return (
    <section className="flex flex-col gap-4" data-testid="enter-snapshot">
      <h2 className="text-xl font-semibold tracking-tight">Enter a Performance Snapshot</h2>
      <p className="text-sm text-muted-foreground">
        Type the totals the platform shows now, not what was gained since last time. Leave a figure empty when you do
        not know it: empty is kept as unknown, not as zero. A snapshot is never changed; if a figure is wrong, enter a
        newer snapshot.
      </p>
      <form onSubmit={record} noValidate className="flex flex-col gap-4">
        <div className="max-w-xs">
          <Field
            id="snapshot-taken-at"
            label="Figures read at"
            type="datetime-local"
            value={takenAt}
            onChange={(event) => setTakenAt(event.target.value)}
            error={errors.takenAt}
          />
        </div>
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-5">
          {METRICS.map(({ metric, key }) => (
            <Field
              key={key}
              id={`snapshot-${key}`}
              label={metric}
              // Text, not a number input: that one hands over nothing for what it cannot read, and nothing means unknown.
              inputMode="numeric"
              maxLength={15}
              placeholder="Unknown"
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
            {saving ? "Recording…" : "Record snapshot"}
          </Button>
          {recorded && recorded.lowerThanPrevious.length === 0 && (
            <span role="status" className="text-sm text-muted-foreground">
              Recorded.
            </span>
          )}
        </div>
        {recorded && recorded.lowerThanPrevious.length > 0 && (
          <p role="status" className="rounded-lg border border-amber-500/50 bg-amber-500/10 p-3 text-sm" data-testid="lower-warning">
            Recorded, but these totals are lower than in the snapshot before it: {metricList(recorded.lowerThanPrevious)}.
            A running total does not usually go down. If one of them was typed wrong, enter a newer snapshot with the
            right figure.
          </p>
        )}
      </form>
    </section>
  );
}

/** How the views grew: every snapshot that knows the views, oldest first. */
function ViewsOverTime({ snapshots, total }: { snapshots: PerformanceSnapshot[]; total: number }) {
  // Latest first as served, so of two snapshots about the same moment the first met is the correction.
  const seen = new Set<string>();
  const points = snapshots
    .filter((snapshot) => !seen.has(snapshot.takenAt) && seen.add(snapshot.takenAt))
    .filter((snapshot) => snapshot.views != null)
    .map((snapshot) => ({ at: new Date(snapshot.takenAt).getTime(), views: snapshot.views as number, takenAt: snapshot.takenAt }))
    .reverse();

  return (
    <section className="flex flex-col gap-4" data-testid="views-over-time">
      <h2 className="text-xl font-semibold tracking-tight">Views over time</h2>
      {points.length === 0 ? (
        <p className="text-sm text-muted-foreground">No snapshot with views yet.</p>
      ) : (
        <>
          {points.length > 1 && <ViewsLine points={points} />}
          <ol className="flex flex-col divide-y rounded-lg border text-sm">
            {points.map((point, index) => (
              <li key={point.takenAt} className="flex flex-wrap justify-between gap-x-4 p-3" data-testid="views-point">
                <span className="text-muted-foreground">{formatMoment(point.takenAt)}</span>
                <span className="tabular-nums">
                  {point.views.toLocaleString()}
                  {index > 0 && <span className="text-muted-foreground"> · {change(point.views - points[index - 1].views)}</span>}
                </span>
              </li>
            ))}
          </ol>
          {total > snapshots.length && (
            <p className="text-sm text-muted-foreground">From the latest {snapshots.length} of {total} snapshots.</p>
          )}
        </>
      )}
    </section>
  );
}

const change = (by: number) => (by > 0 ? `+${by.toLocaleString()}` : by.toLocaleString());

function ViewsLine({ points }: { points: { at: number; views: number; takenAt: string }[] }) {
  const width = 600;
  const height = 160;
  const pad = 8;
  const first = points[0].at;
  const span = points[points.length - 1].at - first || 1;
  const most = Math.max(...points.map((point) => point.views)) || 1;
  const x = (at: number) => pad + ((at - first) / span) * (width - 2 * pad);
  // From zero, so a small rise does not look like a large one.
  const y = (views: number) => height - pad - (views / most) * (height - 2 * pad);

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      role="img"
      aria-label={`Views from ${points[0].views.toLocaleString()} to ${points[points.length - 1].views.toLocaleString()}`}
      className="w-full rounded-lg border text-foreground"
    >
      <polyline
        fill="none"
        stroke="currentColor"
        strokeWidth={2}
        points={points.map((point) => `${x(point.at)},${y(point.views)}`).join(" ")}
      />
      {points.map((point) => (
        <circle key={point.takenAt} cx={x(point.at)} cy={y(point.views)} r={3} fill="currentColor">
          <title>
            {formatMoment(point.takenAt)}: {point.views.toLocaleString()} views
          </title>
        </circle>
      ))}
    </svg>
  );
}

function History({ snapshots, total }: { snapshots: PerformanceSnapshot[]; total: number }) {
  if (snapshots.length === 0) return null;
  return (
    <section className="flex flex-col gap-4" data-testid="snapshot-history">
      <h2 className="text-xl font-semibold tracking-tight">
        Every snapshot<span className="text-muted-foreground"> · {total}</span>
      </h2>
      <div className="overflow-x-auto rounded-lg border">
        <table className="w-full text-sm whitespace-nowrap">
          <thead className="text-left text-muted-foreground">
            <tr className="border-b">
              <th className="p-3 font-medium">Totals as of</th>
              {METRICS.map(({ metric }) => (
                <th key={metric} className="p-3 text-right font-medium">
                  {metric}
                </th>
              ))}
              <th className="p-3 font-medium">Source</th>
              <th className="p-3 font-medium">Entered</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {snapshots.map((snapshot, index) => (
              <tr key={snapshot.id} data-testid="snapshot">
                <td className="p-3">
                  {formatMoment(snapshot.takenAt)}
                  {index === 0 && <span className="text-muted-foreground"> · current</span>}
                </td>
                {METRICS.map(({ metric, key }) => (
                  <td key={metric} className="p-3 text-right tabular-nums">
                    <span className={snapshot[key] == null ? "text-muted-foreground" : undefined}>{formatTotal(snapshot[key])}</span>
                    {snapshot.lowerThanPrevious.includes(metric) && (
                      <span className="text-amber-600 dark:text-amber-400" title="Lower than in the snapshot before it">
                        {" "}
                        ↓ lower
                      </span>
                    )}
                  </td>
                ))}
                <td className="p-3">{sourceName(snapshot.source)}</td>
                <td className="p-3">{formatMoment(snapshot.recordedAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {total > snapshots.length && (
        <p className="text-sm text-muted-foreground">
          Showing the latest {snapshots.length} of {total}.
        </p>
      )}
    </section>
  );
}
