"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api/client";

/**
 * One Project. `data` is null when there is no such Project in the member's Organization.
 * Nothing is asked for until there is an identifier to ask with.
 */
export function useProject(projectId: string | undefined) {
  return useQuery({
    queryKey: ["projects", "one", projectId],
    enabled: !!projectId,
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects/{projectId}", {
        params: { path: { projectId: projectId! } },
      });
      // 400 is an identifier that is not one: as absent as one that matches nothing.
      if (response.status === 404 || response.status === 400) return null;
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
}

/**
 * One Variant of a Project. `data` is null when the Project has no such Variant in the
 * member's Organization. Nothing is asked for until there are identifiers to ask with.
 */
export function useVariant(projectId: string | undefined, variantId: string | undefined) {
  return useQuery({
    queryKey: ["projects", "one", projectId, "variants", variantId],
    enabled: !!projectId && !!variantId,
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects/{projectId}/variants/{variantId}", {
        params: { path: { projectId: projectId!, variantId: variantId! } },
      });
      // 400 is an identifier that is not one: as absent as one that matches nothing.
      if (response.status === 404 || response.status === 400) return null;
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
}
