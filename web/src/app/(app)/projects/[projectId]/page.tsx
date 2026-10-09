"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { Suspense, useState } from "react";
import { Button } from "@/components/ui/button";
import { api, problemDetail, type Project } from "@/lib/api/client";
import { ProjectVariants } from "./project-variants";

const LOADING = <p className="text-sm text-muted-foreground">Loading…</p>;

// Which Project is only known once the page is asked for, so it is drawn inside a
// Suspense boundary: the build cannot prerender what depends on the URL.
export default function ProjectPage() {
  return (
    <Suspense fallback={LOADING}>
      <RequestedProject />
    </Suspense>
  );
}

function RequestedProject() {
  const { projectId } = useParams<{ projectId: string }>();
  // `data` is null when there is no such Project in the member's Organization.
  const project = useQuery({
    queryKey: ["projects", "one", projectId],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects/{projectId}", {
        params: { path: { projectId } },
      });
      // 400 is an identifier that is not one: as absent as one that matches nothing.
      if (response.status === 404 || response.status === 400) return null;
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  if (project.isError || project.data === null) {
    return (
      <>
        <h1 className="text-3xl font-semibold tracking-tight">Project</h1>
        <p role="alert" className="text-sm text-destructive">
          {project.isError ? "The Project could not be loaded." : "There is no such Project in your Organization."}
        </p>
        <Link href="/projects" className="font-medium underline underline-offset-4">
          All Projects
        </Link>
      </>
    );
  }
  if (!project.data) return LOADING;
  return <ProjectDetails project={project.data} />;
}

function ProjectDetails({ project }: { project: Project }) {
  return (
    <>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-col gap-1">
          <h1 className="text-3xl font-semibold tracking-tight" data-testid="project-product">
            {project.productName}
          </h1>
          <p className="text-sm text-muted-foreground">
            Project created on {new Date(project.createdAt).toLocaleString()}
          </p>
        </div>
        <Delete project={project} />
      </div>
      <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-[10rem_1fr]">
        <Detail label="Product">
          <Link href={`/products/${project.productId}`} className="underline underline-offset-4">
            {project.productName}
          </Link>
        </Detail>
        <Detail label="Audience">{project.audience}</Detail>
        <Detail label="Language">{project.language === "vi" ? "Vietnamese" : project.language}</Detail>
        <Detail label="Target duration">{project.targetDurationSeconds} seconds</Detail>
        <Detail label="Objective">
          <span className="whitespace-pre-wrap">{project.objective}</span>
        </Detail>
      </dl>
      <ProjectVariants projectId={project.id} />
    </>
  );
}

function Detail({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <>
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="min-w-0 break-words">{children}</dd>
    </>
  );
}

// Asks once more before deleting: the Project's Variants go with it and nothing brings them back.
function Delete({ project }: { project: Project }) {
  const queryClient = useQueryClient();
  const router = useRouter();
  const [asking, setAsking] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [failed, setFailed] = useState<string>();

  const remove = async () => {
    setDeleting(true);
    setFailed(undefined);
    const { error, response } = await api
      .DELETE("/api/v1/projects/{projectId}", { params: { path: { projectId: project.id } } })
      .catch(() => ({ error: undefined, response: undefined }));
    // 404 is a Project someone has already deleted: gone, which is what was asked for.
    if (response?.ok || response?.status === 404) {
      // Only the lists: this page's own Project is gone, and asking for it again would say so before the page is left.
      await queryClient.invalidateQueries({ queryKey: ["projects", "list"] });
      router.replace("/projects");
      return;
    }
    // A 409 says in the API's own words why the Project is kept: it has a Rendered Video.
    setFailed(problemDetail(error) ?? "The Project could not be deleted.");
    setDeleting(false);
    setAsking(false);
  };

  if (!asking) {
    return (
      <div className="flex items-center gap-3">
        {failed && (
          <span role="alert" className="text-sm text-destructive">
            {failed}
          </span>
        )}
        <Button variant="destructive" onClick={() => setAsking(true)}>
          Delete
        </Button>
      </div>
    );
  }
  return (
    <div className="flex flex-wrap items-center gap-3">
      <span className="text-sm">
        Delete this Project
        {project.variantCount > 0 &&
          ` and its ${project.variantCount === 1 ? "Variant" : `${project.variantCount} Variants`}`}
        ? This cannot be undone.
      </span>
      <Button variant="destructive" onClick={remove} disabled={deleting}>
        {deleting ? "Deleting…" : "Yes, delete"}
      </Button>
      <Button variant="outline" onClick={() => setAsking(false)} disabled={deleting}>
        Cancel
      </Button>
    </div>
  );
}
