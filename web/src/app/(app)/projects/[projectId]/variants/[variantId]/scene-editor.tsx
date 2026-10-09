"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { api, problemDetail, type Scene, type SceneEdit, type SceneLayout, type Storyboard, type Variant } from "@/lib/api/client";

/** What each line of a layout's on-screen text is (SceneLayout in the domain), and how many lines it sets. */
const LINES: Record<SceneLayout, { labels: string[]; most: number }> = {
  Hook: { labels: ["Hook"], most: 1 },
  Reveal: { labels: ["Name"], most: 1 },
  Facts: { labels: [], most: 3 },
  Closing: { labels: ["Name", "Call to action"], most: 2 },
  Solution: { labels: ["Label", "Name"], most: 2 },
};

/** A Scene as it is being edited. `position` stays the Scene's place in the version being edited. */
type Draft = {
  position: number;
  layout: SceneLayout;
  onScreenText: string[];
  narrationText: string;
  assetId: string;
  /** In seconds, as typed. */
  duration: string;
};

const draftOf = (scene: Scene): Draft => ({
  position: scene.position,
  layout: scene.layout,
  onScreenText: [...scene.onScreenText],
  narrationText: scene.narrationText,
  assetId: scene.assetIds[0] ?? "",
  duration: String(scene.durationMs / 1000),
});

const milliseconds = (duration: string) => Math.round(Number(duration) * 1000);

/**
 * The basic Scene editor: text, the photo, the order and the durations of one Storyboard version.
 * Saving changes nothing about that version: it makes the Variant's next one.
 */
export function SceneEditor({
  variant,
  storyboard,
  productId,
  onSaved,
  onCancel,
}: {
  variant: Variant;
  storyboard: Storyboard;
  productId?: string;
  onSaved: () => Promise<void>;
  onCancel: () => void;
}) {
  const [drafts, setDrafts] = useState(() => storyboard.scenes.map(draftOf));
  const [saving, setSaving] = useState(false);
  const [refused, setRefused] = useState<string>();
  // The Product's photos, to choose a Scene's from. The API says why when one cannot be shown in a video.
  const photos = useQuery({
    queryKey: ["products", "one", productId, "assets"],
    enabled: !!productId,
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/products/{productId}/assets", {
        params: { path: { productId: productId! } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
    select: (assets) => assets.filter((asset) => asset.kind === "Photo"),
  });

  const target = storyboard.scenes.reduce((sum, scene) => sum + scene.durationMs, 0);
  const total = drafts.reduce((sum, draft) => sum + (milliseconds(draft.duration) || 0), 0);

  // What the API refused is no longer what is on the page once anything is changed.
  const change = (index: number, changed: Partial<Draft>) => {
    setRefused(undefined);
    setDrafts((all) => all.map((draft, at) => (at === index ? { ...draft, ...changed } : draft)));
  };
  const swap = (index: number, other: number) => {
    setRefused(undefined);
    setDrafts((all) => all.map((draft, at) => (at === index ? all[other] : at === other ? all[index] : draft)));
  };
  // The Hook opens the video, so nothing moves above it and it moves nowhere.
  const movable = (index: number) => index >= 0 && index < drafts.length && drafts[index].layout !== "Hook";

  const save = async () => {
    setSaving(true);
    setRefused(undefined);
    // Only what differs is sent: text sent back as it was is no edit, and the API marks a Scene by what changed.
    const scenes: SceneEdit[] = drafts.map((draft) => {
      const scene = storyboard.scenes.find((original) => original.position === draft.position)!;
      const was = draftOf(scene);
      const lines = draft.onScreenText.map((line) => line.trim());
      return {
        position: draft.position,
        onScreenText: lines.join("\n") === was.onScreenText.join("\n") ? undefined : lines,
        narrationText: draft.narrationText.trim() === was.narrationText ? undefined : draft.narrationText,
        assetId: draft.assetId === was.assetId ? undefined : draft.assetId,
        durationMs: milliseconds(draft.duration) === scene.durationMs ? undefined : milliseconds(draft.duration),
      };
    });
    const { error, response } = await api
      .POST("/api/v1/projects/{projectId}/variants/{variantId}/storyboards/{version}/edits", {
        params: { path: { projectId: variant.projectId, variantId: variant.id, version: storyboard.version } },
        body: { scenes },
      })
      .catch(() => ({ error: undefined, response: undefined }));
    if (response?.ok) {
      await onSaved();
    } else {
      // A 409 says in the API's own words what the edit breaks.
      setRefused(problemDetail(error) ?? "The edit could not be saved.");
      setSaving(false);
    }
  };

  return (
    <section className="flex flex-col gap-4" data-testid="scene-editor">
      <div className="flex flex-col gap-1">
        <h2 className="text-xl font-semibold tracking-tight">Editing version {storyboard.version}</h2>
        <p className="text-sm text-muted-foreground">
          Saving makes a new version and keeps this one. A Scene whose text you change is marked Manually Edited: its
          text is yours, and is no longer checked against Facts.
        </p>
      </div>
      <ol className="flex flex-col divide-y rounded-lg border">
        {drafts.map((draft, index) => {
          const id = `scene-${draft.position}`;
          const { labels, most } = LINES[draft.layout];
          return (
            <li key={draft.position} className="flex flex-col gap-3 p-4" data-testid="scene-draft">
              <div className="flex flex-wrap items-center gap-2">
                <p className="mr-auto text-sm font-medium">
                  Scene {index + 1} · {draft.layout}
                </p>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={!movable(index) || !movable(index - 1)}
                  onClick={() => swap(index, index - 1)}
                  aria-label={`Move Scene ${index + 1} earlier`}
                >
                  Earlier
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={!movable(index) || !movable(index + 1)}
                  onClick={() => swap(index, index + 1)}
                  aria-label={`Move Scene ${index + 1} later`}
                >
                  Later
                </Button>
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor={`${id}-line-0`}>On-screen text</Label>
                {draft.onScreenText.map((line, at) => (
                  <div key={at} className="flex gap-2">
                    <Input
                      id={`${id}-line-${at}`}
                      aria-label={labels[at] ?? `Line ${at + 1}`}
                      placeholder={labels[at]}
                      value={line}
                      onChange={(event) =>
                        change(index, { onScreenText: draft.onScreenText.map((old, i) => (i === at ? event.target.value : old)) })
                      }
                    />
                    {draft.layout === "Facts" && draft.onScreenText.length > 1 && (
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() => change(index, { onScreenText: draft.onScreenText.filter((_, i) => i !== at) })}
                      >
                        Remove
                      </Button>
                    )}
                  </div>
                ))}
                {draft.layout === "Facts" && draft.onScreenText.length < most && (
                  <div>
                    <Button size="sm" variant="outline" onClick={() => change(index, { onScreenText: [...draft.onScreenText, ""] })}>
                      Add a line
                    </Button>
                  </div>
                )}
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor={`${id}-narration`}>Narration</Label>
                <Textarea
                  id={`${id}-narration`}
                  value={draft.narrationText}
                  onChange={(event) => change(index, { narrationText: event.target.value })}
                />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor={`${id}-duration`}>Duration in seconds</Label>
                <Input
                  id={`${id}-duration`}
                  type="number"
                  min={0.1}
                  step={0.1}
                  className="w-28"
                  value={draft.duration}
                  onChange={(event) => change(index, { duration: event.target.value })}
                />
              </div>
              {productId && (photos.data?.length ?? 0) > 0 && (
                <fieldset className="flex flex-col gap-2">
                  <legend className="mb-2 text-sm font-medium">Photo</legend>
                  <div className="flex flex-wrap gap-2">
                    {photos.data!.map((photo) => (
                      <label key={photo.id} className="cursor-pointer">
                        <input
                          type="radio"
                          name={`${id}-photo`}
                          className="peer sr-only"
                          checked={draft.assetId === photo.id}
                          onChange={() => change(index, { assetId: photo.id })}
                        />
                        {/* Not next/image: the file comes from the API, which only serves it to a signed-in member. */}
                        {/* eslint-disable-next-line @next/next/no-img-element */}
                        <img
                          src={`/api/v1/products/${productId}/assets/${photo.id}/content`}
                          alt={`A photo of the Product, ${photo.width} by ${photo.height} pixels`}
                          loading="lazy"
                          className="size-16 rounded-lg border bg-muted object-contain peer-checked:ring-3 peer-checked:ring-ring peer-focus-visible:ring-3"
                        />
                      </label>
                    ))}
                  </div>
                </fieldset>
              )}
            </li>
          );
        })}
      </ol>
      <p className={`text-sm ${total === target ? "text-muted-foreground" : "text-destructive"}`} data-testid="scene-editor-total">
        The Scenes last {total / 1000} s in all. The target duration is {target / 1000} s
        {total === target ? "." : ": move time from one Scene to another until they match."}
      </p>
      {refused && (
        <p role="alert" className="text-sm text-destructive" data-testid="scene-editor-refused">
          {refused}
        </p>
      )}
      <div className="flex gap-2">
        <Button onClick={save} disabled={saving}>
          {saving ? "Saving…" : "Save as a new version"}
        </Button>
        <Button variant="outline" onClick={onCancel} disabled={saving}>
          Cancel
        </Button>
      </div>
    </section>
  );
}
