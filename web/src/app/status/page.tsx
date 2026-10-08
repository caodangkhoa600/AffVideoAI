"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api/client";

type Reachability = "checking" | "reachable" | "unreachable" | "unknown";

const BADGE: Record<Reachability, { label: string; colour: string }> = {
  checking: { label: "Checking…", colour: "bg-zinc-100 text-zinc-600" },
  reachable: { label: "Reachable", colour: "bg-emerald-100 text-emerald-800" },
  unreachable: { label: "Unreachable", colour: "bg-red-100 text-red-800" },
  unknown: { label: "Unknown", colour: "bg-zinc-100 text-zinc-600" },
};

const reachability = (reachable: boolean): Reachability => (reachable ? "reachable" : "unreachable");

export default function StatusPage() {
  const status = useQuery({
    queryKey: ["status"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/status");
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
    refetchInterval: 5000,
    retry: false,
  });

  // The database and object storage are only known through the API.
  const behindApi = (reachable: boolean | undefined): Reachability => {
    if (status.isPending) return "checking";
    if (status.isError || reachable === undefined) return "unknown";
    return reachability(reachable);
  };

  const rows: { name: string; state: Reachability }[] = [
    { name: "API", state: status.isPending ? "checking" : reachability(status.isSuccess) },
    { name: "Database", state: behindApi(status.data?.database.reachable) },
    { name: "Object storage", state: behindApi(status.data?.objectStorage.reachable) },
  ];

  return (
    <main className="mx-auto flex w-full max-w-xl flex-1 flex-col gap-6 px-6 py-16">
      <h1 className="text-3xl font-semibold tracking-tight">System status</h1>
      <ul className="divide-y divide-zinc-200 rounded-lg border border-zinc-200">
        {rows.map(({ name, state }) => (
          <li key={name} className="flex items-center justify-between px-4 py-3">
            <span className="font-medium">{name}</span>
            <span
              data-testid={`status-${name.toLowerCase().replaceAll(" ", "-")}`}
              className={`rounded-full px-3 py-1 text-sm font-medium ${BADGE[state].colour}`}
            >
              {BADGE[state].label}
            </span>
          </li>
        ))}
      </ul>
      <p className="text-sm text-zinc-500">Checked every five seconds.</p>
    </main>
  );
}
