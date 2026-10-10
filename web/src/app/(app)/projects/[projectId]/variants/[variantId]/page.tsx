"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { Suspense } from "react";
import type { Variant } from "@/lib/api/client";
import { creativeTemplateName } from "../../../creative-templates";
import { useProject, useVariant } from "../../../use-project";
import { StoryboardVersions } from "./storyboard-versions";
import { VariantAudioSection } from "./variant-audio";

const LOADING = <p className="text-sm text-muted-foreground">Loading…</p>;

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
  const variant = useVariant(projectId, variantId);

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

/** The Variant's Storyboard versions, the Rendered Video of each, and the Variant's narration and music. */
function VariantStoryboards({ variant }: { variant: Variant }) {
  // The Product the Scenes' photos belong to.
  const project = useProject(variant.projectId);

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
      <StoryboardVersions variant={variant} productId={project.data?.productId} render />
      {/* The audio is the Variant's, not a version's: it is there whichever version is read, and before there is one. */}
      <VariantAudioSection variant={variant} />
    </>
  );
}

