"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { z } from "zod";
import { Field, TextAreaField } from "@/components/field";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { api, fieldErrors, type Product, type Project, type Variant } from "@/lib/api/client";
import { ProductAssets, useProductAssets } from "../products/[productId]/product-assets";
import { ProductFacts, useFacts } from "../products/[productId]/product-facts";
import { ProductForm } from "../products/product-form";
import { StoryboardRender } from "../projects/[projectId]/variants/[variantId]/storyboard-render";
import { StoryboardVersions, useStoryboards } from "../projects/[projectId]/variants/[variantId]/storyboard-versions";
import { CREATIVE_TEMPLATES, creativeTemplateName } from "../projects/creative-templates";
import type { Position } from "./position";

/** Moves the wizard. "replace" where going back in the browser should not undo the move. */
type Go = (change: Partial<Position>, how?: "replace") => void;

const LOADING = <p className="text-sm text-muted-foreground">Loading…</p>;

const SELECT =
  "h-8 w-full rounded-lg border border-input bg-transparent px-2.5 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 md:text-sm dark:bg-input/30";

// The API's limits (Project in the domain).
const MIN_SECONDS = 15;
const MAX_SECONDS = 30;
const DURATION = `Enter a whole number of seconds from ${MIN_SECONDS} to ${MAX_SECONDS}.`;
// What every creative template's Hook layout holds (CreativeTemplates in the domain). A Variant may have a
// longer Hook, and it is only when the Storyboard is generated that the API says it does not fit.
const HOOK_MAX_LENGTH = 60;

// Far more than an Organization is expected to have; the step says so if there are more.
const PRODUCTS_SHOWN = 200;

/** What a step is for, in a sentence or two, above what it works with. */
function About({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1">
      <h2 className="text-xl font-semibold tracking-tight" data-testid="wizard-step">
        {title}
      </h2>
      <p className="text-sm text-muted-foreground">{children}</p>
    </div>
  );
}

/** Back and Next, under a step. `waiting` says what Next is waiting for, and holds it back. */
function Moves({ onBack, onNext, waiting }: { onBack?: () => void; onNext?: () => void; waiting?: string }) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      {onBack && (
        <Button variant="outline" onClick={onBack} data-testid="wizard-back">
          Back
        </Button>
      )}
      {onNext && (
        <Button onClick={onNext} disabled={!!waiting} data-testid="wizard-next">
          Next
        </Button>
      )}
      {waiting && <span className="text-sm text-muted-foreground">{waiting}</span>}
    </div>
  );
}

/** Step 1: the Product the video is of, picked from the Organization's or added here. */
export function ProductStep({ position, product, go }: { position: Position; product?: Product; go: Go }) {
  const products = useQuery({
    queryKey: ["products", "list", { status: "Active", page: 1, pageSize: PRODUCTS_SHOWN }],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/products", {
        params: { query: { status: "Active", pageSize: PRODUCTS_SHOWN } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
  const [chosen, setChosen] = useState(position.productId ?? "");
  const [adding, setAdding] = useState(false);

  const about = <About title="Which Product is the video of?">Pick one you have already added, or add a new one.</About>;

  // The creative direction has been chosen: the video is of this Product, and stays so.
  if (position.projectId) {
    if (!product) return LOADING;
    return (
      <>
        <About title="Which Product is the video of?">
          This video is of <span className="font-medium text-foreground">{product.name}</span>. Its creative direction
          has been chosen, so the Product stays. To make a video of another Product, start another video.
        </About>
        <Moves onNext={() => go({ step: "photos" })} />
      </>
    );
  }
  if (products.isError) {
    return (
      <>
        {about}
        <p role="alert" className="text-sm text-destructive">
          The Products could not be loaded.
        </p>
      </>
    );
  }
  if (!products.data) return LOADING;

  const none = products.data.items.length === 0;
  if (adding || none) {
    return (
      <>
        <About title="Add the Product">
          What it is and who it is for. Its photos come next, and its Facts after that.
        </About>
        <ProductForm
          onCancel={none ? undefined : () => setAdding(false)}
          onSaved={(saved) => go({ productId: saved.id, step: "photos" })}
        />
      </>
    );
  }
  const listed = products.data.items.some((option) => option.id === chosen) ? chosen : "";
  return (
    <>
      {about}
      <div className="flex max-w-xl flex-col gap-2">
        <Label htmlFor="wizard-product">Product</Label>
        <select id="wizard-product" className={SELECT} value={listed} onChange={(event) => setChosen(event.target.value)}>
          <option value="">Choose a Product</option>
          {products.data.items.map((option) => (
            <option key={option.id} value={option.id}>
              {option.name}
            </option>
          ))}
        </select>
        {products.data.total > products.data.items.length && (
          <p className="text-sm text-muted-foreground">
            Showing the first {products.data.items.length} Products by name.
          </p>
        )}
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <Button
          onClick={() => go({ productId: listed, step: "photos" })}
          disabled={listed === ""}
          data-testid="wizard-next"
        >
          Next
        </Button>
        <Button variant="outline" onClick={() => setAdding(true)} data-testid="wizard-add-product">
          Add a new Product
        </Button>
      </div>
    </>
  );
}

/** Step 2: the Product's photos. A video needs at least one. */
export function PhotosStep({ productId, go }: { productId: string; go: Go }) {
  const assets = useProductAssets(productId);
  const photos = assets.data?.filter((asset) => asset.kind === "Photo").length ?? 0;
  return (
    <>
      <About title="Add photos of the Product">
        The video is made from these photos and never changes how the Product looks. A photo of the Product alone on a
        plain background, at least 400 pixels on each side, works best. A logo is optional.
      </About>
      <ProductAssets productId={productId} />
      <Moves
        onBack={() => go({ step: "product" })}
        onNext={() => go({ step: "facts" })}
        waiting={photos === 0 ? "Add at least one photo to go on." : undefined}
      />
    </>
  );
}

/** Step 3: the Product's Facts. Only a Confirmed Fact can appear in a video. */
export function FactsStep({ productId, go }: { productId: string; go: Go }) {
  const confirmed = useFacts(productId, "Confirmed");
  return (
    <>
      <About title="Confirm what the video may say">
        Add what is true of the Product, one statement at a time, in Vietnamese, then confirm each one. The video only
        ever says what you have Confirmed here.
      </About>
      <ProductFacts productId={productId} />
      <Moves
        onBack={() => go({ step: "photos" })}
        onNext={() => go({ step: "direction" })}
        waiting={(confirmed.data?.total ?? 0) === 0 ? "Confirm at least one Fact to go on." : undefined}
      />
    </>
  );
}

const text = (max: number, missing: string) =>
  z.string().trim().min(1, missing).max(max, `Use at most ${max} characters.`);

// The same rules the API applies, so most mistakes are caught before a request
// is made. The API's answer is still shown next to the field when it disagrees.
const direction = z.object({
  creativeTemplate: z.enum(CREATIVE_TEMPLATES.map((template) => template.value)),
  hook: text(HOOK_MAX_LENGTH, "Enter the Hook."),
  // Text, so that "20.5" is refused here rather than rounded on its way to the API.
  targetDurationSeconds: z
    .string()
    .trim()
    .regex(/^\d+$/, DURATION)
    .refine((seconds) => Number(seconds) >= MIN_SECONDS && Number(seconds) <= MAX_SECONDS, DURATION),
  audience: text(500, "Say who the video is for."),
  objective: text(500, "Say what the video is meant to get the viewer to do."),
});

type Direction = z.infer<typeof direction>;

const DIRECTION_FIELDS = Object.keys(direction.shape) as (keyof Direction)[];

/**
 * Step 4: the creative direction, which is the brief of a Project and the creative template and
 * Hook of a Variant. Choosing it makes both; from then on it is fixed, and shown as it was chosen.
 */
export function DirectionStep({
  product,
  project,
  variant,
  go,
}: {
  product: Product;
  /** There once the brief has been saved. */
  project?: Project;
  /** There once the whole direction has been saved. */
  variant?: Variant;
  go: Go;
}) {
  const queryClient = useQueryClient();
  const form = useForm<Direction>({
    resolver: zodResolver(direction),
    defaultValues: {
      creativeTemplate: CREATIVE_TEMPLATES[0].value,
      hook: "",
      targetDurationSeconds: String(project?.targetDurationSeconds ?? 20),
      // The Product's own target audience is where the brief starts.
      audience: project?.audience ?? product.targetAudience,
      objective: project?.objective ?? "",
    },
  });

  const template = useWatch({ control: form.control, name: "creativeTemplate" });

  if (project && variant) {
    return (
      <>
        <About title="The creative direction">
          Chosen, and fixed for this video. A different creative template or Hook makes a different video: start
          another one for it.
        </About>
        <dl className="grid gap-x-6 gap-y-3 sm:grid-cols-[10rem_1fr]" data-testid="wizard-direction">
          <Chosen label="Creative template">{creativeTemplateName(variant.creativeTemplate)}</Chosen>
          <Chosen label="Hook">{variant.hook}</Chosen>
          <Brief project={project} />
        </dl>
        <Moves onBack={() => go({ step: "facts" })} onNext={() => go({ step: "storyboard" })} />
      </>
    );
  }

  const save = form.handleSubmit(async (values) => {
    const refuse = (error: unknown, fallback: string) => {
      const refused = fieldErrors(error);
      const named = DIRECTION_FIELDS.filter((field) => refused[field]);
      for (const field of named) form.setError(field, { message: refused[field].join(" ") });
      if (named.length === 0) form.setError("root", { message: fallback });
    };

    let projectId = project?.id;
    if (!projectId) {
      const { data, error } = await api
        .POST("/api/v1/projects", {
          body: {
            productId: product.id,
            audience: values.audience,
            language: "vi",
            targetDurationSeconds: Number(values.targetDurationSeconds),
            objective: values.objective,
          },
        })
        .catch(() => ({ data: undefined, error: undefined }));
      if (!data) return refuse(error, "The creative direction could not be saved.");
      projectId = data.id;
      // Known here before the address names it, so the form stays as it is while the Hook is tried.
      queryClient.setQueryData(["projects", "one", data.id], data);
    }

    const { data: added, error } = await api
      .POST("/api/v1/projects/{projectId}/variants", {
        params: { path: { projectId } },
        body: { creativeTemplate: values.creativeTemplate, hook: values.hook },
      })
      .catch(() => ({ data: undefined, error: undefined }));
    await queryClient.invalidateQueries({ queryKey: ["projects"] });
    if (!added) {
      refuse(error, "The creative direction could not be saved.");
      // The brief was saved and is now fixed: the address keeps it, so that trying again does not save another.
      if (!project) go({ projectId }, "replace");
      return;
    }
    go({ projectId, variantId: added.id, step: "storyboard" });
  });

  const { errors, isSubmitting } = form.formState;
  return (
    <>
      <About title="Choose the creative direction">
        How the video looks, how it opens and how long it runs. These are fixed once you go on.
      </About>
      <form onSubmit={save} noValidate className="flex max-w-xl flex-col gap-4">
        <div className="flex flex-col gap-2">
          <Label htmlFor="creativeTemplate">Creative template</Label>
          <select id="creativeTemplate" className={SELECT} {...form.register("creativeTemplate")}>
            {CREATIVE_TEMPLATES.map((option) => (
              <option key={option.value} value={option.value}>
                {option.name}
              </option>
            ))}
          </select>
          <p className="text-sm text-muted-foreground" data-testid="wizard-template-about">
            {CREATIVE_TEMPLATES.find((option) => option.value === template)?.about}
          </p>
        </div>
        <Field
          id="hook"
          label="Hook"
          maxLength={HOOK_MAX_LENGTH}
          hint={`The opening line of the video, in Vietnamese, meant to stop the viewer scrolling. At most ${HOOK_MAX_LENGTH} characters.`}
          error={errors.hook?.message}
          {...form.register("hook")}
        />
        {project ? (
          <dl className="grid gap-x-6 gap-y-3 sm:grid-cols-[10rem_1fr]">
            <Brief project={project} />
          </dl>
        ) : (
          <>
            <Field
              id="targetDurationSeconds"
              label="Duration (seconds)"
              inputMode="numeric"
              hint={`From ${MIN_SECONDS} to ${MAX_SECONDS} seconds.`}
              error={errors.targetDurationSeconds?.message}
              {...form.register("targetDurationSeconds")}
            />
            <Field
              id="audience"
              label="Audience"
              hint="Who the video is for."
              error={errors.audience?.message}
              {...form.register("audience")}
            />
            <TextAreaField
              id="objective"
              label="Objective"
              rows={3}
              hint="What the video is meant to get the viewer to do."
              error={errors.objective?.message}
              {...form.register("objective")}
            />
          </>
        )}
        {errors.root && (
          <p role="alert" className="text-sm text-destructive">
            {errors.root.message}
          </p>
        )}
        <div className="flex gap-3">
          <Button type="button" variant="outline" disabled={isSubmitting} onClick={() => go({ step: "facts" })}>
            Back
          </Button>
          <Button type="submit" disabled={isSubmitting} data-testid="wizard-next">
            {isSubmitting ? "Saving…" : "Next"}
          </Button>
        </div>
      </form>
    </>
  );
}

function Brief({ project }: { project: Project }) {
  return (
    <>
      <Chosen label="Duration">{project.targetDurationSeconds} seconds</Chosen>
      <Chosen label="Audience">{project.audience}</Chosen>
      <Chosen label="Objective">
        <span className="whitespace-pre-wrap">{project.objective}</span>
      </Chosen>
    </>
  );
}

function Chosen({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <>
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="min-w-0 break-words">{children}</dd>
    </>
  );
}

/** Step 5: the Storyboard, generated here, read Scene by Scene and edited. */
export function StoryboardStep({ variant, productId, go }: { variant: Variant; productId: string; go: Go }) {
  const storyboards = useStoryboards(variant);
  return (
    <>
      <About title="Review the Storyboard">
        The plan of the video, Scene by Scene: what is on screen, for how long, and which Facts it uses. Generate it,
        read it, and change what you want before rendering.
      </About>
      <StoryboardVersions variant={variant} productId={productId} render={false} />
      {storyboards.data?.total === 0 && <AnotherHook variant={variant} go={go} />}
      <Moves
        onBack={() => go({ step: "direction" })}
        onNext={() => go({ step: "video" })}
        waiting={(storyboards.data?.total ?? 0) === 0 ? "Generate the Storyboard to go on." : undefined}
      />
    </>
  );
}

/**
 * The way on when the Storyboard cannot be generated because the Hook does not fit its layout: a Variant is
 * never changed, so the video is given a new one, duplicated from this one with another Hook.
 */
function AnotherHook({ variant, go }: { variant: Variant; go: Go }) {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [hook, setHook] = useState("");
  const [saving, setSaving] = useState(false);
  const [refused, setRefused] = useState<string>();

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (hook.trim() === "") return setRefused("Enter the new Hook.");
    setSaving(true);
    setRefused(undefined);
    const { data, error } = await api
      .POST("/api/v1/projects/{projectId}/variants/{variantId}/duplicate", {
        params: { path: { projectId: variant.projectId, variantId: variant.id } },
        body: { hook },
      })
      .catch(() => ({ data: undefined, error: undefined }));
    if (!data) {
      setRefused(fieldErrors(error).hook?.join(" ") ?? "The Hook could not be changed.");
      setSaving(false);
      return;
    }
    await queryClient.invalidateQueries({ queryKey: ["projects"] });
    go({ variantId: data.id }, "replace");
  };

  if (!open) {
    return (
      <div className="flex flex-wrap items-center gap-3 text-sm text-muted-foreground">
        <span>
          The Hook is “{variant.hook}”. If the Storyboard cannot be generated because of it, give the video another.
        </span>
        <Button variant="outline" size="sm" onClick={() => setOpen(true)} data-testid="wizard-another-hook">
          Use another Hook
        </Button>
      </div>
    );
  }
  return (
    <form onSubmit={submit} noValidate className="flex max-w-xl flex-col gap-4">
      <Field
        id="another-hook"
        label="New Hook"
        value={hook}
        maxLength={HOOK_MAX_LENGTH}
        autoFocus
        onChange={(event) => setHook(event.target.value)}
        error={refused}
        hint="The video keeps its creative template. The Variant with the old Hook stays on the Project's page."
      />
      <div className="flex gap-3">
        <Button type="submit" disabled={saving}>
          {saving ? "Saving…" : "Use this Hook"}
        </Button>
        <Button type="button" variant="outline" disabled={saving} onClick={() => setOpen(false)}>
          Cancel
        </Button>
      </div>
    </form>
  );
}

/** Step 6: the video. Rendering the newest Storyboard version, watching the result, approving and downloading it. */
export function VideoStep({ variant, go }: { variant: Variant; go: Go }) {
  const storyboards = useStoryboards(variant);
  const newest = storyboards.data?.items[0];
  const about = (
    <About title="Render, watch, approve and download">
      Rendering takes a minute or two and runs in the background: you can leave this page and come back. Watch the
      video, approve it if it is fit to publish, then download the MP4.
    </About>
  );

  if (storyboards.isError) {
    return (
      <>
        {about}
        <p role="alert" className="text-sm text-destructive">
          The Storyboard could not be loaded.
        </p>
      </>
    );
  }
  if (!storyboards.data) return LOADING;
  if (!newest) {
    return (
      <>
        {about}
        <p className="text-sm text-muted-foreground">There is no Storyboard to render yet.</p>
        <Moves onBack={() => go({ step: "storyboard" })} />
      </>
    );
  }
  return (
    <>
      {about}
      <p className="text-sm text-muted-foreground" data-testid="wizard-video-version">
        This renders Storyboard version {newest.version}, the newest. Narration and music are added on{" "}
        <Link href={`/projects/${variant.projectId}/variants/${variant.id}`} className="underline underline-offset-4">
          the Variant&apos;s page
        </Link>
        , before rendering.
      </p>
      {/* Keyed by the version, so a job being watched is never shown under another version. */}
      <StoryboardRender key={newest.id} variant={variant} storyboard={newest} />
      <div className="flex flex-wrap items-center gap-3">
        <Button variant="outline" onClick={() => go({ step: "storyboard" })} data-testid="wizard-back">
          Back
        </Button>
        <Button variant="outline" asChild>
          <Link href="/videos">Video library</Link>
        </Button>
      </div>
    </>
  );
}
