"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { Field } from "@/components/field";
import { Button } from "@/components/ui/button";
import { api, fieldErrors, type LabProduct } from "@/lib/api/client";

// The most the API serves in one page; the count says so if there are more.
const PAGE_SIZE = 200;

// The API's limit (Campaign in the domain).
const CAMPAIGN_NAME_MAX_LENGTH = 200;

export default function LabPage() {
  return (
    <>
      <div className="flex flex-col gap-1">
        <h1 className="text-3xl font-semibold tracking-tight">Affiliate Lab</h1>
        <p className="text-sm text-muted-foreground">
          The Products you are considering, and the Campaigns that group Variants for an experiment.
        </p>
        <Link href="/lab/published-posts" className="self-start font-medium underline underline-offset-4">
          Published Posts
        </Link>
      </div>
      <Shortlist />
      <Campaigns />
    </>
  );
}

/** What the programme pays for the Product, as it was typed in: a rate, an amount, or not known. */
function commissionOf(product: LabProduct) {
  if (product.commissionRatePercent !== null) return `${product.commissionRatePercent}%`;
  if (product.commissionAmount !== null) {
    return `${product.commissionAmount.toLocaleString("en-US", { maximumFractionDigits: 2 })} ${product.commissionCurrency} an order`;
  }
  return "Commission not known";
}

function Shortlist() {
  const shortlist = useQuery({
    queryKey: ["lab", "products", { shortlisted: true }],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/lab/products", {
        params: { query: { shortlisted: true, pageSize: PAGE_SIZE } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  return (
    <section className="flex flex-col gap-4" data-testid="lab-shortlist">
      <div className="flex flex-col gap-1">
        <h2 className="text-xl font-semibold tracking-tight">
          Shortlist
          {shortlist.data && <span className="text-muted-foreground"> · {shortlist.data.total}</span>}
        </h2>
        <p className="text-sm text-muted-foreground">
          Products you might promote. Put one here, with your research notes and its commission, from the Product&apos;s
          own page.
        </p>
      </div>
      {shortlist.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The shortlist could not be loaded.
        </p>
      ) : !shortlist.data ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : shortlist.data.items.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          No Product is shortlisted.{" "}
          <Link href="/products" className="font-medium underline underline-offset-4">
            All Products
          </Link>
        </p>
      ) : (
        <ul className="divide-y rounded-lg border">
          {shortlist.data.items.map((product) => (
            <li key={product.productId}>
              <Link href={`/products/${product.productId}`} className="flex flex-col gap-1 px-4 py-3 hover:bg-muted">
                <span className="flex items-center justify-between gap-4">
                  <span className="min-w-0 truncate font-medium">{product.name}</span>
                  <span className="shrink-0 text-sm text-muted-foreground">
                    {commissionOf(product)}
                    {product.status === "Archived" && " · Archived"}
                  </span>
                </span>
                {product.researchNotes && (
                  <span className="line-clamp-2 text-sm text-muted-foreground">{product.researchNotes}</span>
                )}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function Campaigns() {
  const queryClient = useQueryClient();
  const campaigns = useQuery({
    queryKey: ["lab", "campaigns", "list"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/lab/campaigns", { params: { query: { pageSize: PAGE_SIZE } } });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
  const [name, setName] = useState("");
  const [saving, setSaving] = useState(false);
  const [refused, setRefused] = useState<string>();

  const create = async (event: React.FormEvent) => {
    event.preventDefault();
    if (name.trim() === "") {
      setRefused("Enter a name.");
      return;
    }
    setSaving(true);
    const { error, response } = await api
      .POST("/api/v1/lab/campaigns", { body: { name } })
      .catch(() => ({ error: undefined, response: undefined }));
    if (response?.ok) {
      setName("");
      setRefused(undefined);
      await queryClient.invalidateQueries({ queryKey: ["lab", "campaigns"] });
    } else {
      setRefused(fieldErrors(error).name?.join(" ") ?? "The Campaign could not be created.");
    }
    setSaving(false);
  };

  return (
    <section className="flex flex-col gap-4" data-testid="lab-campaigns">
      <div className="flex flex-col gap-1">
        <h2 className="text-xl font-semibold tracking-tight">
          Campaigns
          {campaigns.data && <span className="text-muted-foreground"> · {campaigns.data.total}</span>}
        </h2>
        <p className="text-sm text-muted-foreground">
          A Campaign groups Variants from any of your Products for one experiment. It refers to them and never owns
          them.
        </p>
      </div>
      <form onSubmit={create} noValidate className="flex max-w-sm flex-col gap-4">
        <Field
          id="campaign-name"
          label="Name of a new Campaign"
          value={name}
          maxLength={CAMPAIGN_NAME_MAX_LENGTH}
          onChange={(event) => setName(event.target.value)}
          error={refused}
        />
        <Button type="submit" disabled={saving} className="self-start">
          {saving ? "Creating…" : "Create Campaign"}
        </Button>
      </form>
      {campaigns.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The Campaigns could not be loaded.
        </p>
      ) : !campaigns.data ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : campaigns.data.items.length === 0 ? (
        <p className="text-sm text-muted-foreground">No Campaigns yet.</p>
      ) : (
        <ul className="divide-y rounded-lg border">
          {campaigns.data.items.map((campaign) => (
            <li key={campaign.id}>
              <Link
                href={`/lab/campaigns/${campaign.id}`}
                className="flex items-center justify-between gap-4 px-4 py-3 hover:bg-muted"
              >
                <span className="min-w-0 truncate font-medium">{campaign.name}</span>
                <span className="shrink-0 text-sm text-muted-foreground">
                  {campaign.variantCount === 1 ? "1 Variant" : `${campaign.variantCount} Variants`}
                  {campaign.status === "Archived" && " · Archived"}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
