"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Field } from "@/components/field";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { api, fieldErrors, type Campaign, type CampaignVariant } from "@/lib/api/client";
import { creativeTemplateName } from "../../../projects/creative-templates";

const LOADING = <p className="text-sm text-muted-foreground">Loading…</p>;

// The most the API serves in one page; the count says so if there are more.
const PAGE_SIZE = 200;

// The API's limit (Campaign in the domain).
const NAME_MAX_LENGTH = 200;

const SELECT =
  "h-8 w-full rounded-lg border border-input bg-transparent px-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30";

// Which Campaign is only known once the page is asked for, so it is drawn inside a
// Suspense boundary: the build cannot prerender what depends on the URL.
export default function CampaignPage() {
  return (
    <Suspense fallback={LOADING}>
      <RequestedCampaign />
    </Suspense>
  );
}

function RequestedCampaign() {
  const { campaignId } = useParams<{ campaignId: string }>();
  const campaign = useQuery({
    queryKey: ["lab", "campaigns", "one", campaignId],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/lab/campaigns/{campaignId}", {
        params: { path: { campaignId } },
      });
      // 400 is an identifier that is not one: as absent as one that matches nothing.
      if (response.status === 404 || response.status === 400) return null;
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  if (campaign.isError || campaign.data === null) {
    return (
      <>
        <h1 className="text-3xl font-semibold tracking-tight">Campaign</h1>
        <p role="alert" className="text-sm text-destructive">
          {campaign.isError ? "The Campaign could not be loaded." : "There is no such Campaign in your Organization."}
        </p>
        <Link href="/lab" className="font-medium underline underline-offset-4">
          Affiliate Lab
        </Link>
      </>
    );
  }
  if (!campaign.data) return LOADING;
  return <CampaignDetails campaign={campaign.data} />;
}

function CampaignDetails({ campaign }: { campaign: Campaign }) {
  const queryClient = useQueryClient();
  const reload = () => queryClient.invalidateQueries({ queryKey: ["lab", "campaigns"] });

  return (
    <>
      <div className="flex flex-col gap-1">
        <h1 className="text-3xl font-semibold tracking-tight" data-testid="campaign-name">
          {campaign.name}
        </h1>
        <p className="text-sm text-muted-foreground">
          Campaign
          {campaign.status === "Archived" && <span data-testid="campaign-archived"> · Archived</span>}
        </p>
      </div>
      {/* Keyed so the form starts again from the saved name. */}
      <Rename key={campaign.name} campaign={campaign} onChanged={reload} />
      <Variants campaign={campaign} onChanged={reload} />
      {campaign.status === "Active" && <Archive campaign={campaign} onChanged={reload} />}
    </>
  );
}

function Rename({ campaign, onChanged }: { campaign: Campaign; onChanged: () => Promise<void> }) {
  const [name, setName] = useState(campaign.name);
  const [saving, setSaving] = useState(false);
  const [refused, setRefused] = useState<string>();

  const rename = async (event: React.FormEvent) => {
    event.preventDefault();
    if (name.trim() === "") {
      setRefused("Enter a name.");
      return;
    }
    setSaving(true);
    const { error, response } = await api
      .PUT("/api/v1/lab/campaigns/{campaignId}", { params: { path: { campaignId: campaign.id } }, body: { name } })
      .catch(() => ({ error: undefined, response: undefined }));
    if (response?.ok) {
      setRefused(undefined);
      await onChanged();
    } else {
      setRefused(fieldErrors(error).name?.join(" ") ?? "The name could not be saved.");
    }
    setSaving(false);
  };

  return (
    <form onSubmit={rename} noValidate className="flex max-w-sm flex-col gap-4">
      <Field
        id="campaign-name"
        label="Name"
        value={name}
        maxLength={NAME_MAX_LENGTH}
        onChange={(event) => setName(event.target.value)}
        error={refused}
      />
      <Button type="submit" disabled={saving || name.trim() === campaign.name} className="self-start">
        {saving ? "Saving…" : "Rename"}
      </Button>
    </form>
  );
}

function Variants({ campaign, onChanged }: { campaign: Campaign; onChanged: () => Promise<void> }) {
  const variants = useQuery({
    queryKey: ["lab", "campaigns", "one", campaign.id, "variants"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/lab/campaigns/{campaignId}/variants", {
        params: { path: { campaignId: campaign.id }, query: { pageSize: PAGE_SIZE } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
  const [failed, setFailed] = useState<string>();

  const change = async (method: "PUT" | "DELETE", variantId: string) => {
    setFailed(undefined);
    const params = { params: { path: { campaignId: campaign.id, variantId } } };
    const path = "/api/v1/lab/campaigns/{campaignId}/variants/{variantId}";
    const { response } = await (method === "PUT" ? api.PUT(path, params) : api.DELETE(path, params)).catch(() => ({
      response: undefined,
    }));
    if (!response?.ok) {
      setFailed(method === "PUT" ? "The Variant could not be added." : "The Variant could not be removed.");
    }
    await onChanged();
  };

  const grouped = new Set(variants.data?.items.map((variant) => variant.variantId));
  return (
    <section className="flex flex-col gap-4" data-testid="campaign-variants">
      <div className="flex flex-col gap-1">
        <h2 className="text-xl font-semibold tracking-tight">
          Variants
          {variants.data && <span className="text-muted-foreground"> · {variants.data.total}</span>}
        </h2>
        <p className="text-sm text-muted-foreground">
          The Variants this Campaign groups, from any of your Products. Removing one from the Campaign leaves the
          Variant, its Storyboards and its videos as they are.
        </p>
      </div>
      {failed && (
        <p role="alert" className="text-sm text-destructive">
          {failed}
        </p>
      )}
      {variants.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The Variants could not be loaded.
        </p>
      ) : !variants.data ? (
        LOADING
      ) : variants.data.items.length === 0 ? (
        <p className="text-sm text-muted-foreground">No Variants in this Campaign yet.</p>
      ) : (
        <ul className="flex flex-col divide-y rounded-lg border">
          {variants.data.items.map((variant) => (
            <GroupedVariant key={variant.variantId} variant={variant} onRemove={() => change("DELETE", variant.variantId)} />
          ))}
        </ul>
      )}
      <AddVariant grouped={grouped} onAdd={(variantId) => change("PUT", variantId)} />
    </section>
  );
}

function GroupedVariant({ variant, onRemove }: { variant: CampaignVariant; onRemove: () => Promise<void> }) {
  const [removing, setRemoving] = useState(false);
  return (
    <li className="flex flex-wrap items-start justify-between gap-3 p-4" data-testid="campaign-variant">
      <div className="flex min-w-0 flex-col gap-1">
        <Link
          href={`/projects/${variant.projectId}/variants/${variant.variantId}`}
          className="break-words font-medium underline-offset-4 hover:underline"
        >
          {variant.hook}
        </Link>
        <p className="text-sm text-muted-foreground">
          {variant.productName} · {creativeTemplateName(variant.creativeTemplate)}
        </p>
      </div>
      <Button
        variant="outline"
        size="sm"
        disabled={removing}
        onClick={async () => {
          setRemoving(true);
          await onRemove();
          setRemoving(false);
        }}
      >
        {removing ? "Removing…" : "Remove from Campaign"}
      </Button>
    </li>
  );
}

/** Picks a Project, then one of its Variants that the Campaign does not group yet. */
function AddVariant({ grouped, onAdd }: { grouped: Set<string>; onAdd: (variantId: string) => Promise<void> }) {
  const [projectId, setProjectId] = useState("");
  const [adding, setAdding] = useState<string>();

  const projects = useQuery({
    queryKey: ["projects", "list", { pageSize: PAGE_SIZE }],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects", { params: { query: { pageSize: PAGE_SIZE } } });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
  const variants = useQuery({
    queryKey: ["projects", "one", projectId, "variants"],
    enabled: projectId !== "",
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects/{projectId}/variants", {
        params: { path: { projectId }, query: { pageSize: PAGE_SIZE } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  return (
    <div className="flex flex-col gap-4 rounded-lg border p-4">
      <div className="flex flex-col gap-2">
        <Label htmlFor="add-from-project">Add Variants from a Project</Label>
        <select
          id="add-from-project"
          className={SELECT}
          value={projectId}
          onChange={(event) => setProjectId(event.target.value)}
        >
          <option value="">Choose a Project</option>
          {projects.data?.items.map((project) => (
            <option key={project.id} value={project.id}>
              {project.productName} · {project.objective}
            </option>
          ))}
        </select>
        {projects.isError && (
          <p role="alert" className="text-sm text-destructive">
            The Projects could not be loaded.
          </p>
        )}
      </div>
      {projectId !== "" &&
        (variants.isError ? (
          <p role="alert" className="text-sm text-destructive">
            The Variants could not be loaded.
          </p>
        ) : !variants.data ? (
          LOADING
        ) : variants.data.items.length === 0 ? (
          <p className="text-sm text-muted-foreground">This Project has no Variants.</p>
        ) : (
          <ul className="flex flex-col divide-y">
            {variants.data.items.map((variant) => (
              <li key={variant.id} className="flex flex-wrap items-center justify-between gap-3 py-2">
                <span className="flex min-w-0 flex-col">
                  <span className="break-words">{variant.hook}</span>
                  <span className="text-sm text-muted-foreground">{creativeTemplateName(variant.creativeTemplate)}</span>
                </span>
                {grouped.has(variant.id) ? (
                  <span className="text-sm text-muted-foreground">In this Campaign</span>
                ) : (
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={adding !== undefined}
                    onClick={async () => {
                      setAdding(variant.id);
                      await onAdd(variant.id);
                      setAdding(undefined);
                    }}
                  >
                    {adding === variant.id ? "Adding…" : "Add to Campaign"}
                  </Button>
                )}
              </li>
            ))}
          </ul>
        ))}
    </div>
  );
}

// Asks once more before archiving: nothing in the app brings a Campaign back yet.
function Archive({ campaign, onChanged }: { campaign: Campaign; onChanged: () => Promise<void> }) {
  const [asking, setAsking] = useState(false);
  const [archiving, setArchiving] = useState(false);
  const [failed, setFailed] = useState(false);

  const archive = async () => {
    setArchiving(true);
    setFailed(false);
    const { response } = await api
      .POST("/api/v1/lab/campaigns/{campaignId}/archive", { params: { path: { campaignId: campaign.id } } })
      .catch(() => ({ response: undefined }));
    if (response?.ok) {
      await onChanged();
    } else {
      setFailed(true);
    }
    setArchiving(false);
    setAsking(false);
  };

  if (!asking) {
    return (
      <div className="flex items-center gap-3">
        <Button variant="destructive" onClick={() => setAsking(true)}>
          Archive
        </Button>
        {failed && (
          <span role="alert" className="text-sm text-destructive">
            The Campaign could not be archived.
          </span>
        )}
      </div>
    );
  }
  return (
    <div className="flex flex-wrap items-center gap-3">
      <span className="text-sm">Archive this Campaign? Its Variants are kept.</span>
      <Button variant="destructive" onClick={archive} disabled={archiving}>
        {archiving ? "Archiving…" : "Yes, archive"}
      </Button>
      <Button variant="outline" onClick={() => setAsking(false)} disabled={archiving}>
        Cancel
      </Button>
    </div>
  );
}
