"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api/client";

const PAGE_SIZE = 20;

export default function ProjectsPage() {
  const [page, setPage] = useState(1);

  const projects = useQuery({
    queryKey: ["projects", "list", { page }],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects", {
        params: { query: { page, pageSize: PAGE_SIZE } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
    // The list stays on screen while the next page loads.
    placeholderData: keepPreviousData,
  });

  const pages = projects.data ? Math.max(1, Math.ceil(projects.data.total / projects.data.pageSize)) : 1;
  // The list got shorter (a Project was deleted) while a later page was open.
  if (projects.data && !projects.isPlaceholderData && page > pages) setPage(pages);
  return (
    <>
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-3xl font-semibold tracking-tight">Projects</h1>
        <Button asChild>
          <Link href="/projects/new">New Project</Link>
        </Button>
      </div>

      {projects.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The Projects could not be loaded.
        </p>
      ) : (
        <ul className="divide-y rounded-lg border" data-testid="projects">
          {!projects.data ? (
            <li className="px-4 py-3 text-sm text-muted-foreground">Loading…</li>
          ) : projects.data.items.length === 0 ? (
            <li className="px-4 py-3 text-sm text-muted-foreground">
              No Projects yet. A Project is one Product plus one brief.
            </li>
          ) : (
            projects.data.items.map((project) => (
              <li key={project.id}>
                <Link
                  href={`/projects/${project.id}`}
                  className="flex items-center justify-between gap-4 px-4 py-3 hover:bg-muted"
                >
                  <span className="flex min-w-0 flex-col">
                    <span className="truncate font-medium">{project.productName}</span>
                    <span className="truncate text-sm text-muted-foreground">{project.objective}</span>
                  </span>
                  <span className="shrink-0 text-sm text-muted-foreground">
                    {project.targetDurationSeconds} s · {project.variantCount}{" "}
                    {project.variantCount === 1 ? "Variant" : "Variants"} ·{" "}
                    {new Date(project.createdAt).toLocaleDateString()}
                  </span>
                </Link>
              </li>
            ))
          )}
        </ul>
      )}

      {projects.data && (
        <div className="flex items-center justify-between gap-4 text-sm text-muted-foreground">
          <span data-testid="projects-total">
            {projects.data.total} {projects.data.total === 1 ? "Project" : "Projects"}
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
