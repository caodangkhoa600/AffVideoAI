"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { Field, TextAreaField } from "@/components/field";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { api, fieldErrors, type Product } from "@/lib/api/client";

// The API's limits (Project in the domain).
const MIN_SECONDS = 15;
const MAX_SECONDS = 30;
const DURATION = `Enter a whole number of seconds from ${MIN_SECONDS} to ${MAX_SECONDS}.`;

// Far more than an Organization is expected to have; the form says so if there are more.
const PRODUCTS_SHOWN = 200;

const text = (max: number, missing: string) =>
  z.string().trim().min(1, missing).max(max, `Use at most ${max} characters.`);

// The same rules the API applies, so most mistakes are caught before a request
// is made. The API's answer is still shown next to the field when it disagrees.
const schema = z.object({
  productId: z.string().min(1, "Choose a Product."),
  audience: text(500, "Say who the videos are for."),
  // Text, so that "20.5" is refused here rather than rounded on its way to the API.
  targetDurationSeconds: z
    .string()
    .trim()
    .regex(/^\d+$/, DURATION)
    .refine((seconds) => Number(seconds) >= MIN_SECONDS && Number(seconds) <= MAX_SECONDS, DURATION),
  objective: text(500, "Say what the videos are meant to get the viewer to do."),
});

type Values = z.infer<typeof schema>;

const FIELDS = Object.keys(schema.shape) as (keyof Values)[];

const LOADING = <p className="text-sm text-muted-foreground">Loading…</p>;

// Which Product the Project starts from can come in the URL, so the form is drawn
// inside a Suspense boundary: the build cannot prerender what depends on the URL.
export default function NewProjectPage() {
  return (
    <>
      <h1 className="text-3xl font-semibold tracking-tight">New Project</h1>
      <Suspense fallback={LOADING}>
        <ProductsThenForm />
      </Suspense>
    </>
  );
}

function ProductsThenForm() {
  const asked = useSearchParams().get("productId") ?? "";
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

  if (products.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        The Products could not be loaded.
      </p>
    );
  }
  if (!products.data) return LOADING;
  if (products.data.items.length === 0) {
    return (
      <>
        <p className="text-sm text-muted-foreground">A Project is made from a Product, and there is none yet.</p>
        <Link href="/products/new" className="font-medium underline underline-offset-4">
          New Product
        </Link>
      </>
    );
  }
  return (
    <ProjectForm
      products={products.data.items}
      more={products.data.total > products.data.items.length}
      asked={products.data.items.some((product) => product.id === asked) ? asked : ""}
    />
  );
}

function ProjectForm({ products, more, asked }: { products: Product[]; more: boolean; asked: string }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const audienceOf = (productId: string) => products.find((product) => product.id === productId)?.targetAudience ?? "";
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { productId: asked, audience: audienceOf(asked), targetDurationSeconds: "20", objective: "" },
  });

  const product = form.register("productId", {
    // The Product's own target audience is where the brief starts, until the member has written another.
    onChange: (event: React.ChangeEvent<HTMLSelectElement>) => {
      if (!form.getFieldState("audience").isDirty) {
        form.resetField("audience", { defaultValue: audienceOf(event.target.value) });
      }
    },
  });

  const save = form.handleSubmit(async (values) => {
    const { data, error } = await api
      .POST("/api/v1/projects", {
        body: {
          productId: values.productId,
          audience: values.audience,
          language: "vi",
          targetDurationSeconds: Number(values.targetDurationSeconds),
          objective: values.objective,
        },
      })
      .catch(() => ({ data: undefined, error: undefined }));
    if (data) {
      await queryClient.invalidateQueries({ queryKey: ["projects"] });
      router.push(`/projects/${data.id}`);
      return;
    }
    const refused = fieldErrors(error);
    const named = FIELDS.filter((field) => refused[field]);
    for (const field of named) form.setError(field, { message: refused[field].join(" ") });
    if (named.length === 0) form.setError("root", { message: "The Project could not be created." });
  });

  const { errors, isSubmitting } = form.formState;
  return (
    <form onSubmit={save} noValidate className="flex max-w-xl flex-col gap-4">
      <div className="flex flex-col gap-2">
        <Label htmlFor="productId">Product</Label>
        <select
          id="productId"
          aria-invalid={errors.productId ? true : undefined}
          className="h-8 w-full rounded-lg border border-input bg-transparent px-2.5 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 md:text-sm dark:bg-input/30"
          {...product}
        >
          <option value="">Choose a Product</option>
          {products.map((option) => (
            <option key={option.id} value={option.id}>
              {option.name}
            </option>
          ))}
        </select>
        {errors.productId ? (
          <p role="alert" className="text-sm text-destructive">
            {errors.productId.message}
          </p>
        ) : (
          more && <p className="text-sm text-muted-foreground">Showing the first {products.length} Products by name.</p>
        )}
      </div>
      <Field
        id="audience"
        label="Audience"
        hint="Who the videos are for."
        error={errors.audience?.message}
        {...form.register("audience")}
      />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field
          id="targetDurationSeconds"
          label="Target duration (seconds)"
          inputMode="numeric"
          hint={`From ${MIN_SECONDS} to ${MAX_SECONDS} seconds.`}
          error={errors.targetDurationSeconds?.message}
          {...form.register("targetDurationSeconds")}
        />
        <Field id="language" label="Language" value="Vietnamese" readOnly hint="Videos are made in Vietnamese." />
      </div>
      <TextAreaField
        id="objective"
        label="Objective"
        rows={3}
        hint="What the videos are meant to get the viewer to do."
        error={errors.objective?.message}
        {...form.register("objective")}
      />
      <p className="text-sm text-muted-foreground">The brief is fixed once the Project is created.</p>
      {errors.root && (
        <p role="alert" className="text-sm text-destructive">
          {errors.root.message}
        </p>
      )}
      <div className="flex gap-3">
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? "Creating…" : "Create Project"}
        </Button>
        <Button variant="outline" asChild>
          <Link href="/projects">Cancel</Link>
        </Button>
      </div>
    </form>
  );
}
