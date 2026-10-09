"use client";

import { useParams, useRouter } from "next/navigation";
import { Suspense } from "react";
import { ProductForm } from "../../product-form";
import { ProductMissing } from "../product-missing";
import { useProduct } from "../../use-product";

const LOADING = <p className="text-sm text-muted-foreground">Loading…</p>;

// Inside a Suspense boundary for the same reason as the Product page.
export default function EditProductPage() {
  return (
    <Suspense fallback={LOADING}>
      <EditRequestedProduct />
    </Suspense>
  );
}

function EditRequestedProduct() {
  const { productId } = useParams<{ productId: string }>();
  const router = useRouter();
  const product = useProduct(productId);

  if (product.isError || product.data === null) return <ProductMissing failed={product.isError} />;
  return (
    <>
      <h1 className="text-3xl font-semibold tracking-tight">Edit Product</h1>
      {product.data ? (
        <ProductForm
          product={product.data}
          cancelHref={`/products/${productId}`}
          onSaved={() => router.push(`/products/${productId}`)}
        />
      ) : (
        LOADING
      )}
    </>
  );
}
