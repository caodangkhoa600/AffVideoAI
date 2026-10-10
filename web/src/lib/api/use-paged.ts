import { useQuery } from "@tanstack/react-query";

export function usePaged<T>(queryKey: unknown[], load: () => Promise<{ data?: T; response: Response }>) {
  return useQuery({
    queryKey,
    queryFn: async () => {
      const { data, response } = await load();
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
}
