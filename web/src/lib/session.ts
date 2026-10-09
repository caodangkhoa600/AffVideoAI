"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api/client";

/**
 * Who is signed in. The session itself is an HttpOnly cookie the page cannot
 * read; this asks the API who it belongs to.
 */
export function useSession() {
  return useQuery({
    queryKey: ["session"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/session");
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
    retry: false,
  });
}
