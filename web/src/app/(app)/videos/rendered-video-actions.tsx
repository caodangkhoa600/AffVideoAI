"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { api, problemDetail, type RenderedVideo, type RenderedVideoState } from "@/lib/api/client";

/** Where a Rendered Video is (RenderedVideoState in the domain), as a member reads it. */
export const VIDEO_STATES: Record<RenderedVideoState, string> = {
  ReadyForReview: "Ready for review",
  Approved: "Approved",
};

/** What a member does with a Rendered Video they have watched: approve it, download it once approved, or delete it. */
export function RenderedVideoActions({ video }: { video: RenderedVideo }) {
  const queryClient = useQueryClient();
  const [approving, setApproving] = useState(false);
  const [asking, setAsking] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [failed, setFailed] = useState<string>();
  const path = { videoId: video.id };

  // The library, and the page of the Variant the video was rendered for, which shows it too.
  const changed = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ["rendered-videos"] }),
      queryClient.invalidateQueries({ queryKey: ["projects", "one", video.projectId] }),
    ]);

  const approve = async () => {
    setApproving(true);
    setFailed(undefined);
    const { data, error } = await api
      .POST("/api/v1/rendered-videos/{videoId}/approve", { params: { path } })
      .catch(() => ({ data: undefined, error: undefined }));
    // A 409 is a video someone else approved meanwhile: asking again shows it approved.
    if (!data) setFailed(problemDetail(error) ?? "The Rendered Video could not be approved.");
    await changed();
    setApproving(false);
  };

  const remove = async () => {
    setDeleting(true);
    setFailed(undefined);
    const { response } = await api
      .DELETE("/api/v1/rendered-videos/{videoId}", { params: { path } })
      .catch(() => ({ response: undefined }));
    // 404 is a Rendered Video someone has already deleted: gone, which is what was asked for.
    if (response?.ok || response?.status === 404) {
      await changed();
      return;
    }
    setFailed("The Rendered Video could not be deleted.");
    setDeleting(false);
    setAsking(false);
  };

  if (asking) {
    // Asks once more before deleting: the file goes with it and nothing brings it back.
    return (
      <div className="flex flex-wrap items-center gap-3" data-testid="rendered-video-delete-confirm">
        <span className="text-sm">Delete this Rendered Video and its file? This cannot be undone.</span>
        <Button variant="destructive" size="sm" onClick={remove} disabled={deleting}>
          {deleting ? "Deleting…" : "Yes, delete"}
        </Button>
        <Button variant="outline" size="sm" onClick={() => setAsking(false)} disabled={deleting}>
          Cancel
        </Button>
      </div>
    );
  }
  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-2">
        {video.state === "Approved" ? (
          <Button asChild size="sm">
            {/* The API serves the file to a signed-in member of the Organization, and only once it is approved. */}
            <a href={`/api/v1/rendered-videos/${video.id}/download`} download data-testid="rendered-video-download">
              Download MP4
            </a>
          </Button>
        ) : (
          <Button size="sm" onClick={approve} disabled={approving} data-testid="rendered-video-approve">
            {approving ? "Approving…" : "Approve"}
          </Button>
        )}
        <Button variant="outline" size="sm" onClick={() => setAsking(true)} data-testid="rendered-video-delete">
          Delete
        </Button>
      </div>
      {video.state !== "Approved" && (
        <p className="text-sm text-muted-foreground">
          Watch it, then approve it: a Rendered Video can be downloaded once it is approved.
        </p>
      )}
      {failed && (
        <p role="alert" className="text-sm text-destructive">
          {failed}
        </p>
      )}
    </div>
  );
}

/** The state of a Rendered Video and, once approved, who approved it and when. */
export function RenderedVideoStatus({ video }: { video: RenderedVideo }) {
  return (
    <span data-testid="rendered-video-state">
      {VIDEO_STATES[video.state]}
      {video.approvedAt &&
        ` by ${video.approvedByEmail ?? "a member"} on ${new Date(video.approvedAt).toLocaleString()}`}
    </span>
  );
}
