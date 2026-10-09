"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api/client";
import { formatEstimates } from "../../videos/production-cost";

/** What rendering for the Product is estimated to have cost so far, added up from every attempt at every render. */
export function ProductProductionCost({ productId }: { productId: string }) {
  const cost = useQuery({
    queryKey: ["products", "one", productId, "production-cost"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/products/{productId}/production-cost", {
        params: { path: { productId } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  return (
    <section className="flex flex-col gap-2" data-testid="product-production-cost">
      <h2 className="text-xl font-semibold tracking-tight">Estimated production cost</h2>
      {cost.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The production cost could not be loaded.
        </p>
      ) : !cost.data ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : cost.data.attempts === 0 ? (
        <p className="text-sm text-muted-foreground">Nothing recorded yet. Each render of this Product&apos;s videos adds to it.</p>
      ) : (
        <p>
          <span className="font-medium" data-testid="product-production-cost-total">
            {formatEstimates(cost.data.estimatedTotals)}
          </span>
          <span className="text-sm text-muted-foreground">
            {" "}
            · {cost.data.attempts === 1 ? "1 render attempt" : `${cost.data.attempts} render attempts`}
            {cost.data.failedAttempts > 0 && `, ${cost.data.failedAttempts} failed`}
          </span>
        </p>
      )}
      <p className="text-sm text-muted-foreground">
        Every attempt at rendering a video of this Product, including attempts that failed and videos since deleted. An
        estimate from the rates configured for the worker, not an amount anyone was billed.
      </p>
    </section>
  );
}
