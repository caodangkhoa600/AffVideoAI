"use client";

import { useRouter } from "next/navigation";
import { ProductForm } from "../product-form";

export default function NewProductPage() {
  const router = useRouter();
  return (
    <>
      <h1 className="text-3xl font-semibold tracking-tight">New Product</h1>
      <ProductForm cancelHref="/products" onSaved={(product) => router.push(`/products/${product.id}`)} />
    </>
  );
}
