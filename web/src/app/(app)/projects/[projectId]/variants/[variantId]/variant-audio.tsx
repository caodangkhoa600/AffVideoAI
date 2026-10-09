"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { api, fieldErrors, type Variant, type VariantAudio, type VariantAudioKind } from "@/lib/api/client";

// The API's limits (VariantAudio in the domain). The size is checked here too, so
// that a file far too large is refused before it is sent rather than cut off on the way.
const MAX_UPLOAD_BYTES = 20 * 1024 * 1024;
const LIMITS = "MP3 or WAV, up to 20 MB and 5 minutes.";

/** The two kinds of audio (VariantAudioKind in the domain), as a member reads them. */
const KINDS: Record<VariantAudioKind, { title: string; named: string; about: string }> = {
  Narration: {
    title: "Narration",
    named: "narration",
    about: "A voice speaking over the video, always at full volume.",
  },
  Music: {
    title: "Music",
    named: "music",
    about: "Music under the video, at the volume chosen here: 100 is as loud as narration, 0 is not heard.",
  },
};

const length = (milliseconds: number) => `${(milliseconds / 1000).toFixed(1)} s`;

/** The Variant's narration and music: what is there, uploading in its place, the music's volume, and removing. */
export function VariantAudioSection({ variant }: { variant: Variant }) {
  const path = { projectId: variant.projectId, variantId: variant.id };
  const queryKey = ["projects", "one", variant.projectId, "variants", variant.id, "audio"];
  const audio = useQuery({
    queryKey,
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects/{projectId}/variants/{variantId}/audio", {
        params: { path },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  return (
    <section className="flex flex-col gap-4" data-testid="variant-audio">
      <div className="flex flex-col gap-1">
        <h2 className="text-xl font-semibold tracking-tight">Narration and music</h2>
        <p className="text-sm text-muted-foreground">
          Your own files: {LIMITS} Each is brought to one loudness and mixed into every video rendered for this Variant
          from then on. Audio longer than the video is cut where the video ends, fading out. Videos already rendered
          keep the sound they have.
        </p>
      </div>
      {audio.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The narration and music could not be loaded.
        </p>
      ) : !audio.data ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        (Object.keys(KINDS) as VariantAudioKind[]).map((kind) => (
          <Track
            key={kind}
            variant={variant}
            kind={kind}
            track={audio.data.find((one) => one.kind === kind)}
            queryKey={queryKey}
          />
        ))
      )}
    </section>
  );
}

function Track({
  variant,
  kind,
  track,
  queryKey,
}: {
  variant: Variant;
  kind: VariantAudioKind;
  track?: VariantAudio;
  queryKey: string[];
}) {
  const queryClient = useQueryClient();
  const path = { projectId: variant.projectId, variantId: variant.id };
  const { title, named, about } = KINDS[kind];
  const rightsId = useId();
  const input = useRef<HTMLInputElement>(null);
  // Ticked afresh for every file: the confirmation is of that file, and is recorded with it.
  const [rightsConfirmed, setRightsConfirmed] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [asking, setAsking] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [refused, setRefused] = useState<string>();

  const upload = async (file: File) => {
    if (file.size > MAX_UPLOAD_BYTES) {
      setRefused("The file is larger than 20 MB.");
      return;
    }
    setUploading(true);
    setRefused(undefined);
    const { error, response } = await api
      .POST("/api/v1/projects/{projectId}/variants/{variantId}/audio", {
        params: { path },
        // The description calls a file a string; what is sent is the form below.
        body: { kind, file: file.name, rightsConfirmed: "true" },
        bodySerializer: () => {
          const form = new FormData();
          form.set("kind", kind);
          form.set("file", file);
          form.set("rightsConfirmed", "true");
          return form;
        },
      })
      .catch(() => ({ error: undefined, response: undefined }));
    if (response?.ok) {
      setRightsConfirmed(false);
      await queryClient.invalidateQueries({ queryKey });
    } else {
      const reasons = fieldErrors(error);
      setRefused(Object.values(reasons).flat().join(" ") || `The ${named} could not be uploaded.`);
    }
    setUploading(false);
  };

  const remove = async (audioId: string) => {
    setRemoving(true);
    setRefused(undefined);
    const { response } = await api
      .DELETE("/api/v1/projects/{projectId}/variants/{variantId}/audio/{audioId}", {
        params: { path: { ...path, audioId } },
      })
      .catch(() => ({ response: undefined }));
    // 404 is audio someone has already removed or replaced: gone, which is what was asked for.
    if (response?.ok || response?.status === 404) {
      await queryClient.invalidateQueries({ queryKey });
    } else {
      setRefused(`The ${named} could not be removed.`);
    }
    setRemoving(false);
    setAsking(false);
  };

  return (
    <div className="flex flex-col gap-3 rounded-lg border p-4" data-testid={`variant-audio-${named}`}>
      <div className="flex flex-col gap-1">
        <h3 className="font-medium">{title}</h3>
        <p className="text-sm text-muted-foreground">{about}</p>
      </div>
      {track ? (
        <>
          {/* The file comes from the API, which only serves it to a signed-in member of the Organization. */}
          <audio
            controls
            preload="none"
            src={`/api/v1/projects/${variant.projectId}/variants/${variant.id}/audio/${track.id}/content`}
            className="w-full max-w-md"
          />
          <p className="text-sm text-muted-foreground" data-testid="variant-audio-details">
            {length(track.durationMs)} · {track.channels === 1 ? "mono" : "stereo"} · rights confirmed by{" "}
            {track.rightsConfirmedByEmail ?? "a member"} on {new Date(track.rightsConfirmedAt).toLocaleString()}
          </p>
          {/* Keyed by the file, so that a new one starts from its own volume. */}
          {kind === "Music" && <Volume key={track.id} variant={variant} track={track} queryKey={queryKey} />}
        </>
      ) : (
        <p className="text-sm text-muted-foreground">No {named} yet.</p>
      )}
      <div className="flex flex-col gap-2">
        <label htmlFor={rightsId} className="flex items-start gap-2 text-sm">
          <input
            id={rightsId}
            type="checkbox"
            className="mt-0.5"
            checked={rightsConfirmed}
            disabled={uploading}
            onChange={(event) => setRightsConfirmed(event.target.checked)}
          />
          <span>
            I hold the rights to use the {named} I am about to upload in videos for this Organization. This is recorded
            in my name.
          </span>
        </label>
        <div className="flex flex-wrap gap-2">
          {/* The input is emptied after each choice, so that choosing the same file again is still a change. */}
          <input
            ref={input}
            type="file"
            accept="audio/mpeg,audio/wav,.mp3,.wav"
            className="sr-only"
            aria-label={`${track ? "Replace" : "Upload"} ${named}`}
            tabIndex={-1}
            onChange={(event) => {
              const file = event.target.files?.[0];
              event.target.value = "";
              if (file) void upload(file);
            }}
          />
          <Button
            variant="outline"
            disabled={!rightsConfirmed || uploading || removing}
            onClick={() => input.current?.click()}
          >
            {uploading ? "Uploading…" : track ? `Replace ${named}` : `Upload ${named}`}
          </Button>
          {track &&
            (asking ? (
              <>
                <Button variant="destructive" onClick={() => remove(track.id)} disabled={removing}>
                  {removing ? "Removing…" : "Yes, remove"}
                </Button>
                <Button variant="outline" onClick={() => setAsking(false)} disabled={removing}>
                  Cancel
                </Button>
              </>
            ) : (
              <Button variant="outline" onClick={() => setAsking(true)} disabled={uploading}>
                Remove
              </Button>
            ))}
        </div>
      </div>
      {refused && (
        <p role="alert" className="text-sm text-destructive" data-testid="variant-audio-refused">
          {refused}
        </p>
      )}
    </div>
  );
}

/** How loud the music is beside narration. Nothing is saved until the member says so. */
function Volume({ variant, track, queryKey }: { variant: Variant; track: VariantAudio; queryKey: string[] }) {
  const queryClient = useQueryClient();
  const id = useId();
  const [volume, setVolume] = useState(track.volumePercent);
  const [saving, setSaving] = useState(false);
  const [failed, setFailed] = useState(false);

  const save = async () => {
    setSaving(true);
    setFailed(false);
    const { response } = await api
      .PUT("/api/v1/projects/{projectId}/variants/{variantId}/audio/{audioId}/volume", {
        params: { path: { projectId: variant.projectId, variantId: variant.id, audioId: track.id } },
        body: { volumePercent: volume },
      })
      .catch(() => ({ response: undefined }));
    if (response?.ok) {
      await queryClient.invalidateQueries({ queryKey });
    } else {
      setFailed(true);
    }
    setSaving(false);
  };

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-3">
        <label htmlFor={id} className="text-sm font-medium">
          Volume
        </label>
        <input
          id={id}
          type="range"
          min={0}
          max={100}
          step={5}
          value={volume}
          disabled={saving}
          onChange={(event) => setVolume(Number(event.target.value))}
          className="w-48"
        />
        <span className="w-12 text-sm tabular-nums" data-testid="variant-audio-volume">
          {volume}%
        </span>
        <Button variant="outline" size="sm" onClick={save} disabled={saving || volume === track.volumePercent}>
          {saving ? "Saving…" : "Save volume"}
        </Button>
      </div>
      {failed && (
        <p role="alert" className="text-sm text-destructive">
          The volume could not be saved.
        </p>
      )}
    </div>
  );
}
