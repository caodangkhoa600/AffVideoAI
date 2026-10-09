"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Button } from "@/components/ui/button";
import { api, problemDetail, type Scene, type SceneLayout, type Storyboard, type Technique, type Variant } from "@/lib/api/client";
import { FlaggedForReview } from "../../../../flagged-for-review";
import { creativeTemplateName } from "../../../creative-templates";
import { SceneEditor } from "./scene-editor";
import { StoryboardRender } from "./storyboard-render";

const LOADING = <p className="text-sm text-muted-foreground">Loading…</p>;

// Far more versions than a Variant is expected to have; the count says so if there are more.
const PAGE_SIZE = 50;

/** The layouts of a creative template (SceneLayout in the domain), as a member reads them. */
const LAYOUTS: Record<SceneLayout, string> = {
  Hook: "Hook",
  Reveal: "Reveal",
  Facts: "Facts",
  Closing: "Closing",
  Solution: "Solution",
};

/** How a Scene is produced (Technique in the domain), as a member reads it. */
const TECHNIQUES: Record<Technique, string> = {
  StaticImage: "Static image",
  ImageMotion: "Image motion",
  ImageToVideo: "Image-to-video",
  VideoAsset: "Video asset",
  TextAnimation: "Text animation",
  ThreeDRender: "3D render",
};

const seconds = (milliseconds: number) => `${milliseconds / 1000} s`;

// Which Variant is only known once the page is asked for, so it is drawn inside a
// Suspense boundary: the build cannot prerender what depends on the URL.
export default function VariantPage() {
  return (
    <Suspense fallback={LOADING}>
      <RequestedVariant />
    </Suspense>
  );
}

function RequestedVariant() {
  const { projectId, variantId } = useParams<{ projectId: string; variantId: string }>();
  // `data` is null when the Project has no such Variant in the member's Organization.
  const variant = useQuery({
    queryKey: ["projects", "one", projectId, "variants", variantId],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects/{projectId}/variants/{variantId}", {
        params: { path: { projectId, variantId } },
      });
      // 400 is an identifier that is not one: as absent as one that matches nothing.
      if (response.status === 404 || response.status === 400) return null;
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  if (variant.isError || variant.data === null) {
    return (
      <>
        <h1 className="text-3xl font-semibold tracking-tight">Storyboard</h1>
        <p role="alert" className="text-sm text-destructive">
          {variant.isError ? "The Variant could not be loaded." : "There is no such Variant in your Organization."}
        </p>
        <Link href="/projects" className="font-medium underline underline-offset-4">
          All Projects
        </Link>
      </>
    );
  }
  if (!variant.data) return LOADING;
  return <VariantStoryboards variant={variant.data} />;
}

/** The Variant's Storyboard versions: generating the next one, and reading any of them. */
function VariantStoryboards({ variant }: { variant: Variant }) {
  const queryClient = useQueryClient();
  const path = { projectId: variant.projectId, variantId: variant.id };
  const key = ["projects", "one", variant.projectId, "variants", variant.id, "storyboards"];
  const storyboards = useQuery({
    queryKey: key,
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects/{projectId}/variants/{variantId}/storyboards", {
        params: { path, query: { pageSize: PAGE_SIZE } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
  // The Product the Scenes' photos belong to. The Project page keeps the same Project
  // under the same key, so this answers as it does: null only when there is no such Project.
  const project = useQuery({
    queryKey: ["projects", "one", variant.projectId],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects/{projectId}", {
        params: { path: { projectId: variant.projectId } },
      });
      if (response.status === 404 || response.status === 400) return null;
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
  // The version being read; the newest until another is chosen.
  const [chosen, setChosen] = useState<number>();
  const [generating, setGenerating] = useState(false);
  const [refused, setRefused] = useState<string>();
  // The version in the Scene editor, when one is.
  const [editing, setEditing] = useState<number>();

  // A version was added: the list is asked for again, and the newest is the one to read.
  const added = async () => {
    await queryClient.invalidateQueries({ queryKey: key });
    setChosen(undefined);
    setEditing(undefined);
  };

  const generate = async () => {
    setGenerating(true);
    setRefused(undefined);
    const { error, response } = await api
      .POST("/api/v1/projects/{projectId}/variants/{variantId}/storyboards", { params: { path } })
      .catch(() => ({ error: undefined, response: undefined }));
    if (response?.ok) {
      await added();
    } else {
      // A 409 says in the API's own words what is missing.
      setRefused(problemDetail(error) ?? "The Storyboard could not be generated.");
    }
    setGenerating(false);
  };

  const versions = storyboards.data?.items ?? [];
  const shown = versions.find((storyboard) => storyboard.version === chosen) ?? versions[0];

  return (
    <>
      <div className="flex flex-col gap-1">
        <h1 className="text-3xl font-semibold tracking-tight">Storyboard</h1>
        <p className="break-words font-medium" data-testid="variant-hook">
          {variant.hook}
        </p>
        <p className="text-sm text-muted-foreground">
          {creativeTemplateName(variant.creativeTemplate)} ·{" "}
          <Link href={`/projects/${variant.projectId}`} className="underline underline-offset-4">
            Back to the Project
          </Link>
        </p>
      </div>
      <div className="flex flex-col gap-3">
        <p className="text-sm text-muted-foreground">
          A Storyboard is planned from the Product&apos;s Confirmed Facts and photos by the mock planner, with no AI.
          Generating again, editing and regenerating a Scene each add a version and keep the earlier ones.
        </p>
        <div>
          <Button onClick={generate} disabled={generating || !storyboards.data}>
            {generating ? "Generating…" : versions.length === 0 ? "Generate Storyboard" : "Generate again"}
          </Button>
        </div>
        {refused && (
          <p role="alert" className="text-sm text-destructive" data-testid="storyboard-refused">
            {refused}
          </p>
        )}
      </div>
      {storyboards.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The Storyboards could not be loaded.
        </p>
      ) : !storyboards.data ? (
        LOADING
      ) : !shown ? (
        <p className="text-sm text-muted-foreground">No Storyboard yet.</p>
      ) : (
        <>
          {versions.length > 1 && (
            <div className="flex flex-wrap items-center gap-2" data-testid="storyboard-versions">
              <span className="text-sm text-muted-foreground">
                {storyboards.data.total > versions.length
                  ? `The newest ${versions.length} of ${storyboards.data.total} versions:`
                  : "Versions:"}
              </span>
              {versions.map((storyboard) => (
                <Button
                  key={storyboard.id}
                  size="sm"
                  variant={storyboard.id === shown.id ? "default" : "outline"}
                  aria-pressed={storyboard.id === shown.id}
                  disabled={editing !== undefined}
                  onClick={() => setChosen(storyboard.version)}
                  title={storyboard.flags.length > 0 ? "Flagged for Review" : undefined}
                >
                  {storyboard.version}
                  {storyboard.flags.length > 0 && <span data-testid="storyboard-version-flagged"> · Flagged</span>}
                </Button>
              ))}
            </div>
          )}
          {editing === shown.version ? (
            <SceneEditor
              key={shown.id}
              variant={variant}
              storyboard={shown}
              productId={project.data?.productId}
              onSaved={added}
              onCancel={() => setEditing(undefined)}
            />
          ) : (
            <>
              <StoryboardVersion
                key={shown.id}
                variant={variant}
                storyboard={shown}
                productId={project.data?.productId}
                onEdit={() => setEditing(shown.version)}
                onAdded={added}
                onFlagCleared={() => queryClient.invalidateQueries({ queryKey: key })}
              />
              {/* Keyed by the version, so a job being watched is never shown under another version. */}
              <StoryboardRender key={`render-${shown.id}`} variant={variant} storyboard={shown} />
            </>
          )}
        </>
      )}
    </>
  );
}

function StoryboardVersion({
  variant,
  storyboard,
  productId,
  onEdit,
  onAdded,
  onFlagCleared,
}: {
  variant: Variant;
  storyboard: Storyboard;
  productId?: string;
  onEdit: () => void;
  onAdded: () => Promise<void>;
  onFlagCleared: () => Promise<void>;
}) {
  const total = storyboard.scenes.reduce((sum, scene) => sum + scene.durationMs, 0);
  // The position of the Scene being regenerated, while it is.
  const [regenerating, setRegenerating] = useState<number>();
  const [refused, setRefused] = useState<string>();

  const regenerate = async (position: number) => {
    setRegenerating(position);
    setRefused(undefined);
    const { error, response } = await api
      .POST("/api/v1/projects/{projectId}/variants/{variantId}/storyboards/{version}/scenes/{position}/regenerate", {
        params: { path: { projectId: variant.projectId, variantId: variant.id, version: storyboard.version, position } },
      })
      .catch(() => ({ error: undefined, response: undefined }));
    if (response?.ok) {
      await onAdded();
    } else {
      setRefused(problemDetail(error) ?? "The Scene could not be regenerated.");
      setRegenerating(undefined);
    }
  };

  const clearFlag = async (factIds: string[]) => {
    const { data, error } = await api
      .POST("/api/v1/projects/{projectId}/variants/{variantId}/storyboards/{version}/clear-flag", {
        params: { path: { projectId: variant.projectId, variantId: variant.id, version: storyboard.version } },
        body: { factIds },
      })
      .catch(() => ({ data: undefined, error: undefined }));
    // A 409 is a flag someone else cleared meanwhile: asking again shows it gone.
    await onFlagCleared();
    return data ? undefined : (problemDetail(error) ?? "The flag could not be cleared.");
  };

  return (
    <section className="flex flex-col gap-4" data-testid="storyboard">
      <div className="flex flex-col gap-1">
        <h2 className="text-xl font-semibold tracking-tight">Version {storyboard.version}</h2>
        <p className="text-sm text-muted-foreground">
          {storyboard.scenes.length} Scenes · {seconds(total)} ·{" "}
          {storyboard.renderMode === "ProductLock" ? "Product Lock" : storyboard.renderMode} · made on{" "}
          {new Date(storyboard.createdAt).toLocaleString()}
        </p>
        <p className="w-fit rounded-md border bg-muted px-2 py-1 text-sm" data-testid="storyboard-planner">
          {storyboard.planner === "Mock"
            ? "Produced by the mock planner: fixed sentence patterns filled with Confirmed Facts. No AI wrote this."
            : `Produced by the ${storyboard.planner} planner.`}
        </p>
        <FlaggedForReview flags={storyboard.flags} subject="Storyboard version" onClear={clearFlag} />
        <div>
          <Button variant="outline" onClick={onEdit} disabled={regenerating !== undefined} data-testid="storyboard-edit">
            Edit the Scenes
          </Button>
        </div>
        {refused && (
          <p role="alert" className="text-sm text-destructive" data-testid="scene-regenerate-refused">
            {refused}
          </p>
        )}
      </div>
      <ol className="flex flex-col divide-y rounded-lg border">
        {storyboard.scenes.map((scene) => (
          <SceneRow
            key={scene.position}
            scene={scene}
            productId={productId}
            regenerating={regenerating === scene.position}
            disabled={regenerating !== undefined}
            onRegenerate={() => regenerate(scene.position)}
          />
        ))}
      </ol>
    </section>
  );
}

function SceneRow({
  scene,
  productId,
  regenerating,
  disabled,
  onRegenerate,
}: {
  scene: Scene;
  productId?: string;
  regenerating: boolean;
  disabled: boolean;
  onRegenerate: () => void;
}) {
  return (
    <li className="flex gap-4 p-4" data-testid="scene">
      {productId &&
        scene.assetIds.map((assetId) => (
          // Not next/image: the file comes from the API, which only serves it to a signed-in
          // member of the Organization, and the image optimiser would ask without the session.
          // eslint-disable-next-line @next/next/no-img-element
          <img
            key={assetId}
            src={`/api/v1/products/${productId}/assets/${assetId}/content`}
            alt="The Product photo this Scene shows"
            loading="lazy"
            className="size-20 shrink-0 rounded-lg border bg-muted object-contain"
          />
        ))}
      <div className="flex min-w-0 flex-col gap-2">
        <p className="text-sm text-muted-foreground">
          Scene {scene.position} · {LAYOUTS[scene.layout]} · {seconds(scene.durationMs)} ·{" "}
          <span data-testid="scene-technique">{TECHNIQUES[scene.technique]}</span>
        </p>
        {scene.manuallyEdited && (
          <p className="w-fit rounded-md border bg-muted px-2 py-1 text-sm" data-testid="scene-manually-edited">
            Manually Edited: a person wrote this text, and it is not checked against Facts.
          </p>
        )}
        <div className="flex flex-col" data-testid="scene-on-screen-text">
          {scene.onScreenText.map((line, index) => (
            <p key={index} className="break-words font-medium">
              {line}
            </p>
          ))}
        </div>
        <p className="break-words text-sm">
          <span className="text-muted-foreground">Narration: </span>
          {scene.narrationText}
        </p>
        {scene.facts.length === 0 ? (
          !scene.manuallyEdited && <p className="text-sm text-muted-foreground">Uses no Facts.</p>
        ) : (
          <div className="flex flex-col gap-1 text-sm" data-testid="scene-facts">
            <p className="text-muted-foreground">Facts used:</p>
            <ul className="list-disc pl-5">
              {scene.facts.map((fact) => (
                <li key={fact.factId} className="break-words">
                  {fact.text}
                </li>
              ))}
            </ul>
          </div>
        )}
        <div>
          <Button size="sm" variant="outline" onClick={onRegenerate} disabled={disabled} data-testid="scene-regenerate">
            {regenerating ? "Regenerating…" : "Regenerate this Scene"}
          </Button>
        </div>
      </div>
    </li>
  );
}
