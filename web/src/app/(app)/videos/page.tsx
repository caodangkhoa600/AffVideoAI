"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { api, type CreativeTemplate, type RenderedVideoOrder, type RenderedVideoState } from "@/lib/api/client";
import { CREATIVE_TEMPLATES, creativeTemplateName } from "../projects/creative-templates";
import { RenderedVideoActions, RenderedVideoStatus, VIDEO_STATES } from "./rendered-video-actions";

const PAGE_SIZE = 10;

const SELECT =
  "h-8 rounded-lg border border-input bg-transparent px-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30";

/** The library: every Rendered Video of the Organization. */
export default function VideosPage() {
  const [search, setSearch] = useState("");
  const [state, setState] = useState<RenderedVideoState | "">("");
  const [creativeTemplate, setCreativeTemplate] = useState<CreativeTemplate | "">("");
  const [sort, setSort] = useState<RenderedVideoOrder>("NewestFirst");
  const [page, setPage] = useState(1);

  const videos = useQuery({
    queryKey: ["rendered-videos", "list", { search, state, creativeTemplate, sort, page }],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/rendered-videos", {
        params: {
          query: {
            search: search || undefined,
            state: state || undefined,
            creativeTemplate: creativeTemplate || undefined,
            sort,
            page,
            pageSize: PAGE_SIZE,
          },
        },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
    // The list stays on screen while the next search or page loads.
    placeholderData: keepPreviousData,
  });

  // Any change to what is being looked for starts again from the first page.
  const fromFirstPage = <T,>(set: (value: T) => void) => (value: T) => {
    set(value);
    setPage(1);
  };

  const pages = videos.data ? Math.max(1, Math.ceil(videos.data.total / videos.data.pageSize)) : 1;
  // The list got shorter (a Rendered Video was deleted) while a later page was open.
  if (videos.data && !videos.isPlaceholderData && page > pages) setPage(pages);
  return (
    <>
      <h1 className="text-3xl font-semibold tracking-tight">Video library</h1>

      <div className="flex flex-wrap items-end gap-4">
        <div className="flex min-w-48 flex-1 flex-col gap-2">
          <Label htmlFor="search">Search by Product name or Project objective</Label>
          <Input id="search" type="search" value={search} onChange={(e) => fromFirstPage(setSearch)(e.target.value)} />
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="state">State</Label>
          <select
            id="state"
            className={SELECT}
            value={state}
            onChange={(e) => fromFirstPage(setState)(e.target.value as RenderedVideoState | "")}
          >
            <option value="">All</option>
            {(Object.keys(VIDEO_STATES) as RenderedVideoState[]).map((value) => (
              <option key={value} value={value}>
                {VIDEO_STATES[value]}
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="creative-template">Creative template</Label>
          <select
            id="creative-template"
            className={SELECT}
            value={creativeTemplate}
            onChange={(e) => fromFirstPage(setCreativeTemplate)(e.target.value as CreativeTemplate | "")}
          >
            <option value="">All</option>
            {CREATIVE_TEMPLATES.map(({ value, name }) => (
              <option key={value} value={value}>
                {name}
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="sort">Sort by date</Label>
          <select
            id="sort"
            className={SELECT}
            value={sort}
            onChange={(e) => fromFirstPage(setSort)(e.target.value as RenderedVideoOrder)}
          >
            <option value="NewestFirst">Newest first</option>
            <option value="OldestFirst">Oldest first</option>
          </select>
        </div>
      </div>

      {videos.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The Rendered Videos could not be loaded.
        </p>
      ) : (
        <ul className="divide-y rounded-lg border" data-testid="videos">
          {!videos.data ? (
            <li className="px-4 py-3 text-sm text-muted-foreground">Loading…</li>
          ) : videos.data.items.length === 0 ? (
            <li className="px-4 py-3 text-sm text-muted-foreground">
              {search || state || creativeTemplate
                ? "No Rendered Videos match."
                : "No Rendered Videos yet. Render a Storyboard on a Variant's page to make one."}
            </li>
          ) : (
            videos.data.items.map((video) => (
              <li key={video.id} className="flex flex-wrap gap-4 px-4 py-4" data-testid="video">
                {/* Nothing of the file is fetched until the member plays it. The API serves it only to a signed-in member of the Organization. */}
                <video
                  controls
                  playsInline
                  preload="none"
                  src={`/api/v1/rendered-videos/${video.id}/content`}
                  className="aspect-[9/16] w-32 shrink-0 rounded-lg border bg-black"
                />
                <div className="flex min-w-0 flex-1 flex-col gap-2">
                  <div className="flex flex-col">
                    <Link
                      href={`/projects/${video.projectId}/variants/${video.variantId}`}
                      className="truncate font-medium underline-offset-4 hover:underline"
                    >
                      {video.productName}
                    </Link>
                    <span className="truncate text-sm text-muted-foreground">{video.projectObjective}</span>
                  </div>
                  <p className="text-sm">“{video.hook}”</p>
                  <p className="text-sm text-muted-foreground">
                    {creativeTemplateName(video.creativeTemplate)} · Storyboard version {video.storyboardVersion} ·{" "}
                    {video.durationMs / 1000} s · {(video.sizeInBytes / (1024 * 1024)).toFixed(1)} MB · rendered{" "}
                    {new Date(video.createdAt).toLocaleString()}
                  </p>
                  <p className="text-sm font-medium">
                    <RenderedVideoStatus video={video} />
                  </p>
                  <RenderedVideoActions video={video} />
                </div>
              </li>
            ))
          )}
        </ul>
      )}

      {videos.data && (
        <div className="flex items-center justify-between gap-4 text-sm text-muted-foreground">
          <span data-testid="videos-total">
            {videos.data.total} {videos.data.total === 1 ? "Rendered Video" : "Rendered Videos"}
          </span>
          <div className="flex items-center gap-3">
            <Button variant="outline" size="sm" onClick={() => setPage(page - 1)} disabled={page <= 1}>
              Previous
            </Button>
            <span>
              Page {page} of {pages}
            </span>
            <Button variant="outline" size="sm" onClick={() => setPage(page + 1)} disabled={page >= pages}>
              Next
            </Button>
          </div>
        </div>
      )}
    </>
  );
}
