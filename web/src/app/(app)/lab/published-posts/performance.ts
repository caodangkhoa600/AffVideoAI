import type { PerformanceMetric, PerformanceSource } from "@/lib/api/client";

/** What a Performance Snapshot counts, in the order they are shown (PerformanceMetric in the domain). */
export const METRICS: { metric: PerformanceMetric; key: "views" | "likes" | "comments" | "shares" | "clicks" }[] = [
  { metric: "Views", key: "views" },
  { metric: "Likes", key: "likes" },
  { metric: "Comments", key: "comments" },
  { metric: "Shares", key: "shares" },
  { metric: "Clicks", key: "clicks" },
];

const SOURCES: Record<PerformanceSource, string> = { Manual: "Typed in by hand" };

/** Where a figure came from, in words for the member. */
export const sourceName = (source: PerformanceSource) => SOURCES[source] ?? source;

/** A total as it is shown. One that is not known is said to be unknown, never shown as zero. */
export const formatTotal = (value: number | null | undefined) => (value == null ? "Unknown" : value.toLocaleString());

export const formatMoment = (value: string) => new Date(value).toLocaleString();
