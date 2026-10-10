"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useEffect } from "react";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api/client";
import { STAGES } from "./projects/[projectId]/variants/[variantId]/storyboard-render";
import { creativeTemplateName } from "./projects/creative-templates";
import { VIDEO_STATES } from "./videos/rendered-video-actions";

// How many of each the dashboard shows. Each list links to the page with the rest.
const RECENT = 5;
const JOBS_SHOWN = 20;

// How often the jobs are asked for while any is in progress, and while none is: one may be
// started on another page. The worker renders; this page only asks.
const POLL_MS = 2000;
const IDLE_POLL_MS = 10_000;

/** The dashboard: what is being rendered now, and the newest Rendered Videos and Projects, to pick up from. */
export default function Home() {
  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex flex-col gap-1">
          <h1 className="text-3xl font-semibold tracking-tight">AffiVideo</h1>
          <p className="text-muted-foreground">Short vertical product videos from a photo and a few Confirmed Facts.</p>
        </div>
        <Button asChild size="lg">
          <Link href="/create" data-testid="quick-create">
            Create video
          </Link>
        </Button>
      </div>
      <JobsInProgress />
      <RecentVideos />
      <RecentProjects />
      <p className="text-sm text-muted-foreground">
        <Link href="/status" className="underline underline-offset-4">
          System status
        </Link>
      </p>
    </>
  );
}

function Section({
  title,
  all,
  testId,
  children,
}: {
  title: string;
  all?: { href: string; label: string };
  testId: string;
  children: React.ReactNode;
}) {
  return (
    <section className="flex flex-col gap-3" data-testid={testId}>
      <div className="flex items-baseline justify-between gap-4">
        <h2 className="text-xl font-semibold tracking-tight">{title}</h2>
        {all && (
          <Link href={all.href} className="text-sm font-medium underline underline-offset-4">
            {all.label}
          </Link>
        )}
      </div>
      {children}
    </section>
  );
}

const ROW = "flex items-center justify-between gap-4 px-4 py-3 hover:bg-muted";
const NOTE = "px-4 py-3 text-sm text-muted-foreground";

function JobsInProgress() {
  const queryClient = useQueryClient();
  const jobs = useQuery({
    queryKey: ["render-jobs", "in-progress"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/render-jobs/in-progress", {
        params: { query: { pageSize: JOBS_SHOWN } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
    refetchInterval: (query) => ((query.state.data?.total ?? 0) > 0 ? POLL_MS : IDLE_POLL_MS),
  });

  // A job that is no longer in progress may have made a Rendered Video: the list below is asked for again
  // whenever the jobs in progress are not the ones they were.
  const inProgress = jobs.data?.items.map(({ job }) => job.id).join();
  useEffect(() => {
    if (inProgress !== undefined) void queryClient.invalidateQueries({ queryKey: ["rendered-videos"] });
  }, [inProgress, queryClient]);

  return (
    <Section title="Jobs in progress" testId="dashboard-jobs">
      {jobs.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The render jobs could not be loaded.
        </p>
      ) : (
        <ul className="divide-y rounded-lg border">
          {!jobs.data ? (
            <li className={NOTE}>Loading…</li>
          ) : jobs.data.items.length === 0 ? (
            <li className={NOTE}>Nothing is being rendered.</li>
          ) : (
            jobs.data.items.map(({ job, ...of }) => (
              <li key={job.id} data-testid="dashboard-job">
                <Link href={`/projects/${of.projectId}/variants/${of.variantId}`} className={ROW}>
                  <span className="flex min-w-0 flex-col">
                    <span className="truncate font-medium">{of.productName}</span>
                    <span className="truncate text-sm text-muted-foreground">
                      “{of.hook}” · {creativeTemplateName(of.creativeTemplate)} · Storyboard version{" "}
                      {of.storyboardVersion}
                    </span>
                  </span>
                  <span className="shrink-0 text-sm font-medium" data-testid="dashboard-job-stage">
                    {STAGES[job.state]}
                  </span>
                </Link>
              </li>
            ))
          )}
        </ul>
      )}
      {jobs.data && jobs.data.total > jobs.data.items.length && (
        <p className="text-sm text-muted-foreground">
          Showing the newest {jobs.data.items.length} of {jobs.data.total}.
        </p>
      )}
    </Section>
  );
}

function RecentVideos() {
  const videos = useQuery({
    queryKey: ["rendered-videos", "list", { recent: RECENT }],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/rendered-videos", {
        params: { query: { sort: "NewestFirst", pageSize: RECENT } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  return (
    <Section title="Recent Rendered Videos" all={{ href: "/videos", label: "Video library" }} testId="dashboard-videos">
      {videos.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The Rendered Videos could not be loaded.
        </p>
      ) : (
        <ul className="divide-y rounded-lg border">
          {!videos.data ? (
            <li className={NOTE}>Loading…</li>
          ) : videos.data.items.length === 0 ? (
            <li className={NOTE}>No Rendered Videos yet.</li>
          ) : (
            videos.data.items.map((video) => (
              <li key={video.id} data-testid="dashboard-video">
                <Link href={`/projects/${video.projectId}/variants/${video.variantId}`} className={ROW}>
                  <span className="flex min-w-0 flex-col">
                    <span className="truncate font-medium">{video.productName}</span>
                    <span className="truncate text-sm text-muted-foreground">
                      “{video.hook}” · {creativeTemplateName(video.creativeTemplate)} · rendered{" "}
                      {new Date(video.createdAt).toLocaleString()}
                    </span>
                  </span>
                  <span className="shrink-0 text-sm font-medium">
                    {VIDEO_STATES[video.state]}
                    {video.flags.length > 0 && " · Flagged for Review"}
                  </span>
                </Link>
              </li>
            ))
          )}
        </ul>
      )}
    </Section>
  );
}

function RecentProjects() {
  const projects = useQuery({
    queryKey: ["projects", "list", { recent: RECENT }],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects", { params: { query: { pageSize: RECENT } } });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  return (
    <Section title="Recent Projects" all={{ href: "/projects", label: "All Projects" }} testId="dashboard-projects">
      {projects.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The Projects could not be loaded.
        </p>
      ) : (
        <ul className="divide-y rounded-lg border">
          {!projects.data ? (
            <li className={NOTE}>Loading…</li>
          ) : projects.data.items.length === 0 ? (
            <li className={NOTE}>No Projects yet. Creating a video makes the first one.</li>
          ) : (
            projects.data.items.map((project) => (
              <li key={project.id} data-testid="dashboard-project">
                <Link href={`/projects/${project.id}`} className={ROW}>
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
    </Section>
  );
}
