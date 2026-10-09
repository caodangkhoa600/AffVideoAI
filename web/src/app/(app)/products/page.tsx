"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { api, type ProductStatus } from "@/lib/api/client";

const PAGE_SIZE = 20;

const SELECT =
  "h-8 rounded-lg border border-input bg-transparent px-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30";

export default function ProductsPage() {
  const [search, setSearch] = useState("");
  const [category, setCategory] = useState("");
  // Archived Products are out of the way until asked for.
  const [status, setStatus] = useState<ProductStatus | "">("Active");
  const [page, setPage] = useState(1);

  const categories = useQuery({
    queryKey: ["products", "categories"],
    queryFn: async () => (await api.GET("/api/v1/products/categories")).data ?? [],
  });

  const products = useQuery({
    queryKey: ["products", "list", { search, category, status, page }],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/products", {
        params: {
          query: {
            search: search || undefined,
            category: category || undefined,
            status: status || undefined,
            page,
            pageSize: PAGE_SIZE,
          },
        },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
    // The list stays on screen while the next search or page loads.
    placeholderData: keepPreviousData,
  });

  // Any change to what is being looked for starts again from the first page.
  const fromFirstPage = <T,>(set: (value: T) => void) => (value: T) => {
    set(value);
    setPage(1);
  };

  const pages = products.data ? Math.max(1, Math.ceil(products.data.total / products.data.pageSize)) : 1;
  // The list got shorter (a Product was archived) while a later page was open.
  if (products.data && !products.isPlaceholderData && page > pages) setPage(pages);
  return (
    <>
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-3xl font-semibold tracking-tight">Products</h1>
        <Button asChild>
          <Link href="/products/new">New Product</Link>
        </Button>
      </div>

      <div className="flex flex-wrap items-end gap-4">
        <div className="flex min-w-48 flex-1 flex-col gap-2">
          <Label htmlFor="search">Search by name</Label>
          <Input id="search" type="search" value={search} onChange={(e) => fromFirstPage(setSearch)(e.target.value)} />
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="category">Category</Label>
          <select id="category" className={SELECT} value={category} onChange={(e) => fromFirstPage(setCategory)(e.target.value)}>
            <option value="">All</option>
            {categories.data?.map((name) => (
              <option key={name} value={name}>
                {name}
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="status">Status</Label>
          <select
            id="status"
            className={SELECT}
            value={status}
            onChange={(e) => fromFirstPage(setStatus)(e.target.value as ProductStatus | "")}
          >
            <option value="Active">Active</option>
            <option value="Archived">Archived</option>
            <option value="">All</option>
          </select>
        </div>
      </div>

      {products.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The Products could not be loaded.
        </p>
      ) : (
        <ul className="divide-y rounded-lg border" data-testid="products">
          {!products.data ? (
            <li className="px-4 py-3 text-sm text-muted-foreground">Loading…</li>
          ) : products.data.items.length === 0 ? (
            <li className="px-4 py-3 text-sm text-muted-foreground">No Products match.</li>
          ) : (
            products.data.items.map((product) => (
              <li key={product.id}>
                <Link
                  href={`/products/${product.id}`}
                  className="flex items-center justify-between gap-4 px-4 py-3 hover:bg-muted"
                >
                  <span className="min-w-0 truncate font-medium">{product.name}</span>
                  <span className="shrink-0 text-sm text-muted-foreground">
                    {product.brand} · {product.category}
                    {product.status === "Archived" && " · Archived"}
                  </span>
                </Link>
              </li>
            ))
          )}
        </ul>
      )}

      {products.data && (
        <div className="flex items-center justify-between gap-4 text-sm text-muted-foreground">
          <span data-testid="products-total">
            {products.data.total} {products.data.total === 1 ? "Product" : "Products"}
          </span>
          <div className="flex items-center gap-3">
            <Button variant="outline" size="sm" onClick={() => setPage(page - 1)} disabled={page <= 1}>
              Previous
            </Button>
            <span>
              Page {page} of {pages}
            </span>
            <Button variant="outline" size="sm" onClick={() => setPage(page + 1)} disabled={page >= pages}>
              Next
            </Button>
          </div>
        </div>
      )}
    </>
  );
}
