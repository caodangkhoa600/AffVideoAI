"use client";

import { useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Button } from "@/components/ui/button";
import { api, type Product } from "@/lib/api/client";
import { useSession } from "@/lib/session";
import { ProductAssets } from "./product-assets";
import { ProductFacts } from "./product-facts";
import { ProductLab } from "./product-lab";
import { ProductMissing } from "./product-missing";
import { ProductProductionCost } from "./product-production-cost";
import { useProduct } from "../use-product";

const LOADING = <p className="text-sm text-muted-foreground">Loading…</p>;

// Which Product is only known once the page is asked for, so it is drawn inside a
// Suspense boundary: the build cannot prerender what depends on the URL.
export default function ProductPage() {
  return (
    <Suspense fallback={LOADING}>
      <RequestedProduct />
    </Suspense>
  );
}

function RequestedProduct() {
  const { productId } = useParams<{ productId: string }>();
  const product = useProduct(productId);

  if (product.isError || product.data === null) return <ProductMissing failed={product.isError} />;
  if (!product.data) return LOADING;
  return <ProductDetails product={product.data} />;
}

function ProductDetails({ product }: { product: Product }) {
  const session = useSession();
  return (
    <>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-col gap-1">
          <h1 className="text-3xl font-semibold tracking-tight" data-testid="product-name">
            {product.name}
          </h1>
          <p className="text-sm text-muted-foreground">
            {product.brand} · {product.category}
            {product.status === "Archived" && <span data-testid="product-archived"> · Archived</span>}
          </p>
        </div>
        <div className="flex gap-3">
          <Button asChild>
            <Link href={`/projects/new?productId=${product.id}`}>New Project</Link>
          </Button>
          <Button variant="outline" asChild>
            <Link href={`/products/${product.id}/edit`}>Edit</Link>
          </Button>
          {product.status === "Active" && <Archive product={product} />}
        </div>
      </div>
      <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-[10rem_1fr]">
        <Detail label="Description">
          <span className="whitespace-pre-wrap">{product.description}</span>
        </Detail>
        <Detail label="Target audience">{product.targetAudience}</Detail>
        <Detail label="Price">
          {product.price === null ? (
            <span className="text-muted-foreground">None</span>
          ) : (
            `${product.price.toLocaleString("en-US", { maximumFractionDigits: 2 })} ${product.currency}`
          )}
        </Detail>
        <Detail label="Original URL">
          <Address url={product.originalUrl} />
        </Detail>
        <Detail label="Affiliate URL">
          {product.affiliateUrl ? <Address url={product.affiliateUrl} /> : <span className="text-muted-foreground">None</span>}
        </Detail>
        <Detail label="Tags">
          {product.tags.length > 0 ? product.tags.join(", ") : <span className="text-muted-foreground">None</span>}
        </Detail>
      </dl>
      <ProductFacts productId={product.id} />
      <ProductAssets productId={product.id} />
      <ProductProductionCost productId={product.id} />
      {session.data?.organization.affiliateLabEnabled && <ProductLab productId={product.id} />}
    </>
  );
}

function Detail({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <>
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="min-w-0 break-words">{children}</dd>
    </>
  );
}

// A link for the member to follow in their own browser. The API only accepts http and https addresses.
function Address({ url }: { url: string }) {
  return (
    <a href={url} target="_blank" rel="noopener noreferrer" className="underline underline-offset-4">
      {url}
    </a>
  );
}

// Asks once more before archiving: nothing in the app brings a Product back yet.
function Archive({ product }: { product: Product }) {
  const queryClient = useQueryClient();
  const [asking, setAsking] = useState(false);
  const [archiving, setArchiving] = useState(false);
  const [failed, setFailed] = useState(false);

  const archive = async () => {
    setArchiving(true);
    setFailed(false);
    const { response } = await api
      .POST("/api/v1/products/{productId}/archive", { params: { path: { productId: product.id } } })
      .catch(() => ({ response: undefined }));
    if (response?.ok) {
      await queryClient.invalidateQueries({ queryKey: ["products"] });
    } else {
      setFailed(true);
    }
    setArchiving(false);
    setAsking(false);
  };

  if (!asking) {
    return (
      <div className="flex items-center gap-3">
        {failed && (
          <span role="alert" className="text-sm text-destructive">
            The Product could not be archived.
          </span>
        )}
        <Button variant="destructive" onClick={() => setAsking(true)}>
          Archive
        </Button>
      </div>
    );
  }
  return (
    <div className="flex items-center gap-3">
      <span className="text-sm">Archive this Product?</span>
      <Button variant="destructive" onClick={archive} disabled={archiving}>
        {archiving ? "Archiving…" : "Yes, archive"}
      </Button>
      <Button variant="outline" onClick={() => setAsking(false)} disabled={archiving}>
        Cancel
      </Button>
    </div>
  );
}
