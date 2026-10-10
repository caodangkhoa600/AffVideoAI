"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect } from "react";
import { Button } from "@/components/ui/button";
import { useSession } from "@/lib/session";
import { useProduct } from "../products/use-product";
import { useProject, useVariant } from "../projects/use-project";
import { forgetPosition, hrefOf, positionOf, reachable, savedPosition, savePosition, STEPS, type Position } from "./position";
import { DirectionStep, FactsStep, PhotosStep, ProductStep, StoryboardStep, VideoStep } from "./steps";

const LOADING = <p className="text-sm text-muted-foreground">Loading…</p>;

// Where the member is comes from the URL, so the wizard is drawn inside a Suspense
// boundary: the build cannot prerender what depends on the URL.
export default function CreateVideoPage() {
  return (
    <>
      <h1 className="text-3xl font-semibold tracking-tight">Create video</h1>
      <Suspense fallback={LOADING}>
        <Wizard />
      </Suspense>
    </>
  );
}

/**
 * The guided way from a Product to an approved, downloaded video, one step at a time.
 * Every step works on the same Product, Project, Variant and Storyboard as their own
 * pages do, so nothing made here is lost by leaving, and the wizard can be left at any step.
 */
function Wizard() {
  const query = useSearchParams().toString();
  const router = useRouter();
  const memberId = useSession().data?.member.id;
  const position = positionOf(query);

  // The address of the wizard itself, with no step: back to the video under way, or to the first step.
  // An address with a step is where the member now is, and is remembered as that.
  useEffect(() => {
    if (!memberId) return;
    const named = positionOf(query);
    if (named) savePosition(memberId, named);
    else router.replace(hrefOf(savedPosition(memberId) ?? { step: "product" }));
  }, [memberId, query, router]);

  if (!position || !memberId) return LOADING;

  const startAnother = () => {
    forgetPosition(memberId);
    router.push(hrefOf({ step: "product" }));
  };
  return <WizardAt position={position} onStartAnother={startAnother} />;
}

function WizardAt({ position: asked, onStartAnother }: { position: Position; onStartAnother: () => void }) {
  const router = useRouter();
  const project = useProject(asked.projectId);
  const variant = useVariant(asked.projectId, asked.variantId);
  // Once the video has a Project, its Product is the Project's, whatever the address says.
  const position = { ...asked, productId: asked.projectId ? project.data?.productId : asked.productId };
  const product = useProduct(position.productId);

  const go = (change: Partial<Position>, how?: "replace") =>
    router[how ?? "push"](hrefOf({ ...position, ...change }));

  // What the address names was deleted, or was never this Organization's.
  const gone =
    product.data === null ? "Product" : project.data === null ? "Project" : variant.data === null ? "Variant" : undefined;
  if (gone || product.isError || project.isError || variant.isError) {
    return (
      <>
        <p role="alert" className="text-sm text-destructive">
          {gone
            ? `The ${gone} this video was being made from is no longer in your Organization.`
            : "The video being made could not be loaded."}
        </p>
        <div>
          <Button onClick={onStartAnother}>Start another video</Button>
        </div>
      </>
    );
  }

  if (asked.projectId && !project.data) return LOADING;

  // A step asked for before what it needs exists: the step that makes what is missing.
  const step = reachable(position.step, position) ? position.step : position.productId ? "direction" : "product";
  const started = !!position.productId;
  const at = STEPS.findIndex(({ id }) => id === step);

  return (
    <>
      <nav aria-label="Steps">
        <ol className="flex flex-wrap gap-2" data-testid="wizard-steps">
          {STEPS.map(({ id, title }, index) => (
            <li key={id}>
              {id === step ? (
                <span
                  aria-current="step"
                  className="inline-flex h-7 items-center rounded-lg bg-primary px-2.5 text-[0.8rem] font-medium text-primary-foreground"
                >
                  {index + 1}. {title}
                </span>
              ) : (
                <Button
                  size="sm"
                  variant="outline"
                  // Back to any step. Ahead only once the creative direction is chosen: until then Next leads
                  // on, and waits for what the following step needs.
                  disabled={!reachable(id, position) || (index > at && !position.variantId)}
                  onClick={() => go({ step: id })}
                >
                  {index + 1}. {title}
                </Button>
              )}
            </li>
          ))}
        </ol>
      </nav>

      {step === "product" ? (
        <ProductStep position={position} product={product.data ?? undefined} go={go} />
      ) : (step === "storyboard" || step === "video") && (!variant.data || !project.data) ? (
        LOADING
      ) : step === "storyboard" ? (
        <StoryboardStep variant={variant.data!} productId={project.data!.productId} go={go} />
      ) : step === "video" ? (
        <VideoStep variant={variant.data!} go={go} />
      ) : !product.data || (position.projectId && !project.data) || (position.variantId && !variant.data) ? (
        LOADING
      ) : step === "photos" ? (
        <PhotosStep productId={product.data.id} go={go} />
      ) : step === "facts" ? (
        <FactsStep productId={product.data.id} go={go} />
      ) : (
        <DirectionStep
          product={product.data}
          project={project.data ?? undefined}
          variant={variant.data ?? undefined}
          go={go}
        />
      )}

      {started && (
        <div className="flex flex-wrap items-center gap-3 border-t pt-4 text-sm text-muted-foreground">
          <span>
            What has been made so far is kept, and this page comes back to this step. To make a different video:
          </span>
          <Button variant="outline" size="sm" onClick={onStartAnother} data-testid="wizard-start-another">
            Start another video
          </Button>
        </div>
      )}
    </>
  );
}
