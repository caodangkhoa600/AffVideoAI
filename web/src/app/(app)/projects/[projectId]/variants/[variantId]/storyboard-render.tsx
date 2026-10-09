"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { api, problemDetail, type RenderJob, type RenderJobState, type Storyboard, type Variant } from "@/lib/api/client";
import { RenderedVideoActions, RenderedVideoStatus } from "../../../../videos/rendered-video-actions";

/** Where a render job is (RenderJobState in the domain), as a member reads it. There is no percentage to show. */
const STAGES: Record<RenderJobState, string> = {
  Created: "Queued",
  Queued: "Queued",
  Validating: "Validating",
  Planning: "Planning",
  GeneratingAssets: "Cutting the Product out of its photos",
  GeneratingVideo: "Generating video",
  Rendering: "Rendering",
  QualityReview: "Checking the video",
  Completed: "Rendered",
  Failed: "Failed",
  Cancelled: "Cancelled",
};

const ENDED: RenderJobState[] = ["Completed", "Failed", "Cancelled"];

// How often the job is asked for while it has not ended. The worker renders; this page only asks.
const POLL_MS = 2000;

/** Rendering one Storyboard version: starting a job, its current stage, and the Rendered Video once there is one. */
export function StoryboardRender({ variant, storyboard }: { variant: Variant; storyboard: Storyboard }) {
  const queryClient = useQueryClient();
  const path = { projectId: variant.projectId, variantId: variant.id, version: storyboard.version };
  const key = ["projects", "one", variant.projectId, "variants", variant.id, "storyboards", storyboard.version, "renders"];
  // The newest job of this version. Asking the API again is what brings a member
  // who left the page back to where the job is now.
  const newest = useQuery({
    queryKey: key,
    queryFn: async (): Promise<RenderJob | null> => {
      const { data, response } = await api.GET(
        "/api/v1/projects/{projectId}/variants/{variantId}/storyboards/{version}/renders",
        { params: { path, query: { pageSize: 1 } } },
      );
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data.items[0] ?? null;
    },
    refetchInterval: (query) => (query.state.data && !ENDED.includes(query.state.data.state) ? POLL_MS : false),
  });
  const [submitting, setSubmitting] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [refused, setRefused] = useState<string>();
  // One key for one wish to render: a click sent twice, or sent again after the
  // answer was lost, queues one job. A new key is made once the API has answered.
  const idempotencyKey = useRef<string>(undefined);

  const render = async () => {
    setSubmitting(true);
    setRefused(undefined);
    idempotencyKey.current ??= crypto.randomUUID();
    const { data, error } = await api
      .POST("/api/v1/projects/{projectId}/variants/{variantId}/storyboards/{version}/renders", {
        params: { path, header: { "Idempotency-Key": idempotencyKey.current } },
      })
      .catch(() => ({ data: undefined, error: undefined }));
    if (data) {
      idempotencyKey.current = undefined;
      queryClient.setQueryData(key, data);
    } else {
      setRefused(problemDetail(error) ?? "The render could not be started.");
    }
    setSubmitting(false);
  };

  const cancel = async (jobId: string) => {
    setCancelling(true);
    setRefused(undefined);
    const { data, error } = await api
      .POST("/api/v1/render-jobs/{jobId}/cancel", { params: { path: { jobId } } })
      .catch(() => ({ data: undefined, error: undefined }));
    if (data) {
      queryClient.setQueryData(key, data);
    } else {
      // It ended while the member was reaching for the button: show how.
      setRefused(problemDetail(error) ?? "The render could not be cancelled.");
      await queryClient.invalidateQueries({ queryKey: key });
    }
    setCancelling(false);
  };

  const job = newest.data;
  const running = !!job && !ENDED.includes(job.state);

  return (
    <section className="flex flex-col gap-3" data-testid="render">
      <h3 className="text-lg font-semibold tracking-tight">Rendered Video</h3>
      <p className="text-sm text-muted-foreground">
        Rendering makes a 1080 by 1920 MP4 of this version in Product Lock: the Product is cut out of its photo and only
        ever scaled, moved and rotated. It runs in the background, so this page can be left and come back to.
      </p>
      <div className="flex gap-2">
        <Button onClick={render} disabled={submitting || running || newest.isPending}>
          {submitting ? "Starting…" : job ? "Render again" : "Render video"}
        </Button>
        {running && (
          <Button variant="outline" onClick={() => cancel(job.id)} disabled={cancelling} data-testid="render-cancel">
            {cancelling ? "Cancelling…" : "Cancel"}
          </Button>
        )}
      </div>
      {refused && (
        <p role="alert" className="text-sm text-destructive">
          {refused}
        </p>
      )}
      {newest.isError && (
        <p role="alert" className="text-sm text-destructive">
          The render jobs could not be loaded.
        </p>
      )}
      {job && (
        <p className="text-sm" aria-live="polite">
          <span className="text-muted-foreground">Stage: </span>
          <span className="font-medium" data-testid="render-stage">
            {STAGES[job.state]}
          </span>
          <span className="text-muted-foreground"> · started {new Date(job.createdAt).toLocaleString()}</span>
        </p>
      )}
      {job?.state === "Queued" && job.retryAt && (
        <p className="text-sm text-muted-foreground" data-testid="render-retry">
          Attempt {job.attempt} did not finish. It will be tried again after {new Date(job.retryAt).toLocaleTimeString()}.
        </p>
      )}
      {job?.state === "Failed" && (
        <p role="alert" className="text-sm text-destructive" data-testid="render-failure">
          {job.failure?.message ?? "The video could not be rendered."}
        </p>
      )}
      {job?.renderedVideoId && <RenderedVideoPreview videoId={job.renderedVideoId} />}
      {job?.state === "Completed" && !job.renderedVideoId && (
        <p className="text-sm text-muted-foreground" data-testid="rendered-video-deleted">
          The Rendered Video this render made has been deleted. Render again to make another.
        </p>
      )}
    </section>
  );
}

function RenderedVideoPreview({ videoId }: { videoId: string }) {
  const video = useQuery({
    queryKey: ["rendered-videos", "one", videoId],
    // `data` is null when the Rendered Video was deleted after the job was last asked for.
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/rendered-videos/{videoId}", { params: { path: { videoId } } });
      if (response.status === 404) return null;
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
  if (video.data === null) return null;
  const uncut = video.data?.uncutAssetIds.length ?? 0;
  const drawn = video.data?.drawnScenePositions ?? [];

  return (
    <div className="flex flex-col gap-2">
      {/* The file comes from the API, which only serves it to a signed-in member of the Organization. */}
      <video
        controls
        playsInline
        preload="metadata"
        src={`/api/v1/rendered-videos/${videoId}/content`}
        className="aspect-[9/16] w-full max-w-xs rounded-lg border bg-black"
        data-testid="rendered-video"
      />
      {video.data && (
        <p className="text-sm font-medium">
          <RenderedVideoStatus video={video.data} />
        </p>
      )}
      {video.data && (
        <p className="text-sm text-muted-foreground">
          Rendered from Storyboard version <span data-testid="rendered-video-version">{video.data.storyboardVersion}</span> ·{" "}
          {video.data.durationMs / 1000} s · {(video.data.sizeInBytes / (1024 * 1024)).toFixed(1)} MB · no narration or
          music, so the audio track is silent
        </p>
      )}
      {video.data && (
        <p className="text-sm text-muted-foreground" data-testid="rendered-video-drawn">
          {drawn.length === 0
            ? "No Scene had to be drawn again: every one was the same as in an earlier render."
            : `${drawn.length === 1 ? "Scene" : "Scenes"} ${drawn.join(", ")} ${drawn.length === 1 ? "was" : "were"} drawn for this video. Any other was reused from an earlier render, where it was the same.`}
        </p>
      )}
      {uncut > 0 && (
        <p className="text-sm text-muted-foreground" data-testid="rendered-video-uncut">
          {uncut === 1 ? "One photo is" : `${uncut} photos are`} shown whole, on a card: the Product could not be cut
          out cleanly. A photo of the Product alone on a plain background cuts out best.
        </p>
      )}
      {video.data && <RenderedVideoActions video={video.data} />}
    </div>
  );
}
