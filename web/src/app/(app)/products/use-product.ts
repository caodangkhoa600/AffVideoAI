"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api/client";

/**
 * One Product. `data` is null when there is no such Product in the member's Organization.
 * Nothing is asked for until there is an identifier to ask with.
 */
export function useProduct(productId: string | undefined) {
  return useQuery({
    queryKey: ["products", "one", productId],
    enabled: !!productId,
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/products/{productId}", {
        params: { path: { productId: productId! } },
      });
      // 400 is an identifier that is not one: as absent as one that matches nothing.
      if (response.status === 404 || response.status === 400) return null;
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
}
