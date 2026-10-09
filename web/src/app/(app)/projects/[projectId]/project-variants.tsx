"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";
import { Field } from "@/components/field";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { api, fieldErrors, type CreativeTemplate, type Variant } from "@/lib/api/client";
import { CREATIVE_TEMPLATES, creativeTemplateName } from "../creative-templates";

// The API's limit (Variant in the domain).
const HOOK_MAX_LENGTH = 200;

// Far more than a Project is expected to have; the count says so if there are more.
const PAGE_SIZE = 200;

/** What the form shows when a Hook is refused: under the field, or once under the form. */
type Refusal = { hook?: string; message?: string };

// A 400 names the Hook; anything else is said once, under the form.
function refusal(error: unknown, fallback: string): Refusal {
  const hook = fieldErrors(error).hook?.join(" ");
  return hook ? { hook } : { message: fallback };
}

/** The Project's Variants: what is there, adding one, and duplicating one with a new Hook. */
export function ProjectVariants({ projectId }: { projectId: string }) {
  const queryClient = useQueryClient();
  const variants = useQuery({
    queryKey: ["projects", "one", projectId, "variants"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects/{projectId}/variants", {
        params: { path: { projectId }, query: { pageSize: PAGE_SIZE } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
  // A new key empties the form after a Variant is added.
  const [added, setAdded] = useState(0);

  // The Project itself is reloaded too: it says how many Variants it has.
  const reload = () => queryClient.invalidateQueries({ queryKey: ["projects"] });

  const add = async (creativeTemplate: CreativeTemplate, hook: string) => {
    const { error, response } = await api
      .POST("/api/v1/projects/{projectId}/variants", { params: { path: { projectId } }, body: { creativeTemplate, hook } })
      .catch(() => ({ error: undefined, response: undefined }));
    if (!response?.ok) return refusal(error, "The Variant could not be added.");
    await reload();
    setAdded((count) => count + 1);
  };

  return (
    <section className="flex flex-col gap-6" data-testid="project-variants">
      <div className="flex flex-col gap-1">
        <h2 className="text-xl font-semibold tracking-tight">
          Variants
          {variants.data && <span className="text-muted-foreground"> · {variants.data.total}</span>}
        </h2>
        <p className="text-sm text-muted-foreground">
          One creative treatment each: a creative template and a Hook, the opening line meant to stop the viewer
          scrolling. A Variant is never changed; a different Hook is a different Variant.
        </p>
      </div>
      <VariantForm key={added} onSubmit={add} />
      {variants.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The Variants could not be loaded.
        </p>
      ) : !variants.data ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : variants.data.items.length === 0 ? (
        <p className="text-sm text-muted-foreground">No Variants yet.</p>
      ) : (
        <>
          <ul className="flex flex-col divide-y rounded-lg border">
            {variants.data.items.map((variant) => (
              <VariantRow key={variant.id} variant={variant} onDuplicated={reload} />
            ))}
          </ul>
          {variants.data.total > variants.data.items.length && (
            <p className="text-sm text-muted-foreground">
              Showing the oldest {variants.data.items.length} of {variants.data.total}.
            </p>
          )}
        </>
      )}
    </section>
  );
}

function VariantRow({ variant, onDuplicated }: { variant: Variant; onDuplicated: () => Promise<void> }) {
  const [duplicating, setDuplicating] = useState(false);

  const duplicate = async (hook: string) => {
    const { error, response } = await api
      .POST("/api/v1/projects/{projectId}/variants/{variantId}/duplicate", {
        params: { path: { projectId: variant.projectId, variantId: variant.id } },
        body: { hook },
      })
      .catch(() => ({ error: undefined, response: undefined }));
    if (!response?.ok) return refusal(error, "The Variant could not be duplicated.");
    await onDuplicated();
    setDuplicating(false);
  };

  return (
    <li className="flex flex-col gap-3 p-4" data-testid="variant">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-1">
          <p className="break-words font-medium" data-testid="variant-hook">
            {variant.hook}
          </p>
          <p className="text-sm text-muted-foreground">{creativeTemplateName(variant.creativeTemplate)}</p>
          <p className="break-all font-mono text-xs text-muted-foreground" data-testid="variant-id">
            {variant.id}
          </p>
        </div>
        {!duplicating && (
          <Button variant="outline" size="sm" onClick={() => setDuplicating(true)}>
            Duplicate
          </Button>
        )}
      </div>
      {duplicating && (
        <>
          <p className="text-sm text-muted-foreground">
            The new Variant keeps the {creativeTemplateName(variant.creativeTemplate)} template. Give it a Hook of its
            own; this Variant stays as it is.
          </p>
          <HookForm variant={variant} onSubmit={duplicate} onCancel={() => setDuplicating(false)} />
        </>
      )}
    </li>
  );
}

/** Adds a Variant: a creative template and a Hook. */
function VariantForm({
  onSubmit,
}: {
  /** Answers why the Variant was refused, or nothing when it was added. */
  onSubmit: (creativeTemplate: CreativeTemplate, hook: string) => Promise<Refusal | undefined>;
}) {
  const id = useId();
  const [creativeTemplate, setCreativeTemplate] = useState<CreativeTemplate>(CREATIVE_TEMPLATES[0].value);
  const [hook, setHook] = useState("");
  const [saving, setSaving] = useState(false);
  const [refused, setRefused] = useState<Refusal>({});

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (hook.trim() === "") {
      setRefused({ hook: "Enter the Hook." });
      return;
    }
    setSaving(true);
    const answer = await onSubmit(creativeTemplate, hook);
    // Added means the form is about to be emptied; only a refusal is shown here.
    if (answer) {
      setRefused(answer);
      setSaving(false);
    }
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
      <div className="grid gap-4 sm:grid-cols-[12rem_1fr]">
        <div className="flex flex-col gap-2">
          <Label htmlFor={`${id}-template`}>Creative template</Label>
          <select
            id={`${id}-template`}
            value={creativeTemplate}
            onChange={(event) => setCreativeTemplate(event.target.value as CreativeTemplate)}
            className="h-8 w-full rounded-lg border border-input bg-transparent px-2.5 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 md:text-sm dark:bg-input/30"
          >
            {CREATIVE_TEMPLATES.map((template) => (
              <option key={template.value} value={template.value}>
                {template.name}
              </option>
            ))}
          </select>
        </div>
        <Field
          id={`${id}-hook`}
          label="Hook"
          value={hook}
          maxLength={HOOK_MAX_LENGTH}
          onChange={(event) => setHook(event.target.value)}
          error={refused.hook}
          hint="The opening line of the video, in Vietnamese."
        />
      </div>
      {refused.message && (
        <p role="alert" className="text-sm text-destructive">
          {refused.message}
        </p>
      )}
      <div>
        <Button type="submit" disabled={saving}>
          {saving ? "Adding…" : "Add Variant"}
        </Button>
      </div>
    </form>
  );
}

/** Asks for the Hook of the Variant that duplicates the one it is given. */
function HookForm({
  variant,
  onSubmit,
  onCancel,
}: {
  variant: Variant;
  /** Answers why the Hook was refused, or nothing when the Variant was duplicated. */
  onSubmit: (hook: string) => Promise<Refusal | undefined>;
  onCancel: () => void;
}) {
  const id = useId();
  const [hook, setHook] = useState("");
  const [saving, setSaving] = useState(false);
  const [refused, setRefused] = useState<Refusal>({});

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (hook.trim() === "") {
      setRefused({ hook: "Enter the new Hook." });
      return;
    }
    if (hook.trim().toLowerCase() === variant.hook.toLowerCase()) {
      setRefused({ hook: "Enter a Hook that differs from the one this Variant has." });
      return;
    }
    setSaving(true);
    const answer = await onSubmit(hook);
    // Duplicated means the form is gone; only a refusal is shown here.
    if (answer) {
      setRefused(answer);
      setSaving(false);
    }
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
      <Field
        id={`${id}-hook`}
        label="New Hook"
        value={hook}
        maxLength={HOOK_MAX_LENGTH}
        autoFocus
        onChange={(event) => setHook(event.target.value)}
        error={refused.hook}
      />
      {refused.message && (
        <p role="alert" className="text-sm text-destructive">
          {refused.message}
        </p>
      )}
      <div className="flex gap-3">
        <Button type="submit" disabled={saving}>
          {saving ? "Duplicating…" : "Duplicate with this Hook"}
        </Button>
        <Button type="button" variant="outline" disabled={saving} onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  );
}
