"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Button } from "@/components/ui/button";
import { api, problemDetail, type Scene, type SceneLayout, type Storyboard, type Technique, type Variant } from "@/lib/api/client";
import { creativeTemplateName } from "../../../creative-templates";
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

  const generate = async () => {
    setGenerating(true);
    setRefused(undefined);
    const { error, response } = await api
      .POST("/api/v1/projects/{projectId}/variants/{variantId}/storyboards", { params: { path } })
      .catch(() => ({ error: undefined, response: undefined }));
    if (response?.ok) {
      await queryClient.invalidateQueries({ queryKey: key });
      setChosen(undefined);
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
          Generating again adds a version and keeps the earlier ones.
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
                  onClick={() => setChosen(storyboard.version)}
                >
                  {storyboard.version}
                </Button>
              ))}
            </div>
          )}
          <StoryboardVersion storyboard={shown} productId={project.data?.productId} />
          {/* Keyed by the version, so a job being watched is never shown under another version. */}
          <StoryboardRender key={shown.id} variant={variant} storyboard={shown} />
        </>
      )}
    </>
  );
}

function StoryboardVersion({ storyboard, productId }: { storyboard: Storyboard; productId?: string }) {
  const total = storyboard.scenes.reduce((sum, scene) => sum + scene.durationMs, 0);
  return (
    <section className="flex flex-col gap-4" data-testid="storyboard">
      <div className="flex flex-col gap-1">
        <h2 className="text-xl font-semibold tracking-tight">Version {storyboard.version}</h2>
        <p className="text-sm text-muted-foreground">
          {storyboard.scenes.length} Scenes · {seconds(total)} ·{" "}
          {storyboard.renderMode === "ProductLock" ? "Product Lock" : storyboard.renderMode} · generated on{" "}
          {new Date(storyboard.createdAt).toLocaleString()}
        </p>
        <p className="w-fit rounded-md border bg-muted px-2 py-1 text-sm" data-testid="storyboard-planner">
          {storyboard.planner === "Mock"
            ? "Produced by the mock planner: fixed sentence patterns filled with Confirmed Facts. No AI wrote this."
            : `Produced by the ${storyboard.planner} planner.`}
        </p>
      </div>
      <ol className="flex flex-col divide-y rounded-lg border">
        {storyboard.scenes.map((scene) => (
          <SceneRow key={scene.position} scene={scene} productId={productId} />
        ))}
      </ol>
    </section>
  );
}

function SceneRow({ scene, productId }: { scene: Scene; productId?: string }) {
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
          <p className="text-sm text-muted-foreground">Uses no Facts.</p>
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
      </div>
    </li>
  );
}
