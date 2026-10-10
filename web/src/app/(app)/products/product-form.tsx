"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { Field, TextAreaField } from "@/components/field";
import { Button } from "@/components/ui/button";
import { api, fieldErrors, type Product, type ProductRequest } from "@/lib/api/client";

const MAX_TAGS = 20;
const TAG_MAX_LENGTH = 50;

const text = (max: number, missing: string) =>
  z.string().trim().min(1, missing).max(max, `Use at most ${max} characters.`);

const WEB_ADDRESS = "Enter a full web address starting with http:// or https://.";

function isWebAddress(value: string) {
  try {
    const { protocol } = new URL(value);
    return protocol === "http:" || protocol === "https:";
  } catch {
    return false;
  }
}

const splitTags = (tags: string) =>
  tags
    .split(",")
    .map((tag) => tag.trim())
    .filter((tag) => tag.length > 0);

// The same rules the API applies, so most mistakes are caught before a request
// is made. The API's answer is still shown next to the field when it disagrees.
const schema = z
  .object({
    name: text(200, "Enter a name."),
    category: text(100, "Enter a category."),
    brand: text(100, "Enter a brand."),
    description: text(4000, "Enter a description."),
    targetAudience: text(500, "Say who the Product is for."),
    originalUrl: z.string().trim().max(2048, "Use at most 2048 characters.").refine(isWebAddress, WEB_ADDRESS),
    affiliateUrl: z
      .string()
      .trim()
      .max(2048, "Use at most 2048 characters.")
      .refine((value) => value === "" || isWebAddress(value), WEB_ADDRESS),
    // Text, not a number input: a price is never rounded on its way to the API.
    price: z
      .string()
      .trim()
      .regex(/^(\d{1,13}(\.\d{1,2})?)?$/, "Enter a price of zero or more, with at most two decimal places."),
    currency: z
      .string()
      .trim()
      .toUpperCase()
      .regex(/^([A-Z]{3})?$/, "Use a three-letter currency code, such as VND."),
    tags: z.string().refine((tags) => {
      const all = splitTags(tags);
      return all.length <= MAX_TAGS && all.every((tag) => tag.length <= TAG_MAX_LENGTH);
    }, `Use at most ${MAX_TAGS} tags, each of at most ${TAG_MAX_LENGTH} characters.`),
  })
  .refine((values) => values.price === "" || values.currency !== "", {
    path: ["currency"],
    message: "A price needs a currency.",
  });

type Values = z.infer<typeof schema>;

const FIELDS = Object.keys(schema.shape) as (keyof Values)[];

// The currency only means something next to a price, so it is sent only with one.
function toRequest(values: Values): ProductRequest {
  const priced = values.price !== "";
  return {
    name: values.name,
    category: values.category,
    brand: values.brand,
    description: values.description,
    targetAudience: values.targetAudience,
    originalUrl: values.originalUrl,
    affiliateUrl: values.affiliateUrl === "" ? null : values.affiliateUrl,
    price: priced ? Number(values.price) : null,
    currency: priced ? values.currency : null,
    tags: splitTags(values.tags),
  };
}

/** Creates a Product, or edits the one it is given. */
export function ProductForm({
  product,
  cancelHref,
  onCancel,
  onSaved,
}: {
  product?: Product;
  /** Where Cancel leads, on a page of its own. */
  cancelHref?: string;
  /** What Cancel does, where the form is part of a page that stays. */
  onCancel?: () => void;
  onSaved: (saved: Product) => void;
}) {
  const queryClient = useQueryClient();
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: product?.name ?? "",
      category: product?.category ?? "",
      brand: product?.brand ?? "",
      description: product?.description ?? "",
      targetAudience: product?.targetAudience ?? "",
      originalUrl: product?.originalUrl ?? "",
      affiliateUrl: product?.affiliateUrl ?? "",
      price: product?.price?.toString() ?? "",
      currency: product?.currency ?? "VND",
      tags: product?.tags.join(", ") ?? "",
    },
  });

  const save = form.handleSubmit(async (values) => {
    const body = toRequest(values);
    const { data, error } = await (product
      ? api.PUT("/api/v1/products/{productId}", { params: { path: { productId: product.id } }, body })
      : api.POST("/api/v1/products", { body })
    ).catch(() => ({ data: undefined, error: undefined }));
    if (data) {
      await queryClient.invalidateQueries({ queryKey: ["products"] });
      onSaved(data);
      return;
    }
    const refused = fieldErrors(error);
    const named = FIELDS.filter((field) => refused[field]);
    for (const field of named) form.setError(field, { message: refused[field].join(" ") });
    if (named.length === 0) form.setError("root", { message: "The Product could not be saved." });
  });

  const { errors, isSubmitting } = form.formState;
  return (
    <form onSubmit={save} noValidate className="flex max-w-xl flex-col gap-4">
      <Field id="name" label="Name" error={errors.name?.message} {...form.register("name")} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field id="category" label="Category" error={errors.category?.message} {...form.register("category")} />
        <Field id="brand" label="Brand" error={errors.brand?.message} {...form.register("brand")} />
      </div>
      <TextAreaField
        id="description"
        label="Description"
        rows={4}
        error={errors.description?.message}
        {...form.register("description")}
      />
      <Field
        id="targetAudience"
        label="Target audience"
        error={errors.targetAudience?.message}
        {...form.register("targetAudience")}
      />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field
          id="price"
          label="Price (optional)"
          inputMode="decimal"
          error={errors.price?.message}
          {...form.register("price")}
        />
        <Field
          id="currency"
          label="Currency"
          hint="Three letters, such as VND. Kept only with a price."
          error={errors.currency?.message}
          {...form.register("currency")}
        />
      </div>
      <Field
        id="originalUrl"
        label="Original URL"
        type="url"
        hint="Kept as text. AffiVideo never opens it."
        error={errors.originalUrl?.message}
        {...form.register("originalUrl")}
      />
      <Field
        id="affiliateUrl"
        label="Affiliate URL (optional)"
        type="url"
        error={errors.affiliateUrl?.message}
        {...form.register("affiliateUrl")}
      />
      <Field
        id="tags"
        label="Tags (optional)"
        hint="Separate tags with commas."
        error={errors.tags?.message}
        {...form.register("tags")}
      />
      {errors.root && (
        <p role="alert" className="text-sm text-destructive">
          {errors.root.message}
        </p>
      )}
      <div className="flex gap-3">
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? "Saving…" : product ? "Save changes" : "Create Product"}
        </Button>
        {cancelHref && (
          <Button variant="outline" asChild>
            <Link href={cancelHref}>Cancel</Link>
          </Button>
        )}
        {onCancel && (
          <Button type="button" variant="outline" disabled={isSubmitting} onClick={onCancel}>
            Cancel
          </Button>
        )}
      </div>
    </form>
  );
}
