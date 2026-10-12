"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { api, problemDetail, type Still, type StillState, type Storyboard, type Variant } from "@/lib/api/client";

/** Where a Still is (StillState in the domain), as a member reads it. There is no percentage to show. */
const STATES: Record<StillState, string> = {
  Queued: "Queued",
  Generating: "Being made",
  ReadyForReview: "Ready for review",
  Approved: "Approved",
  Rejected: "Rejected",
  Failed: "Failed",
};

const WAITING: StillState[] = ["Queued", "Generating"];

// How often the estimate is asked for while a Still is on its way. The worker has it made; this page only asks.
const POLL_MS = 2000;

/** An estimated amount: cents, and finer only when there is something finer. Never an amount anyone was billed. */
const money = (amount: number, currency: string) =>
  `${amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 6 })} ${currency}`;

/**
 * Paid generation for one Hybrid Storyboard version: what it would make and cost, the member's
 * confirmation, and the Still of each generated Scene, to approve or to replace with another.
 * Nothing is generated before the member confirms (ADR 0003).
 */
export function StoryboardGeneration({
  variant,
  storyboard,
  onAdded,
}: {
  variant: Variant;
  storyboard: Storyboard;
  /** A Storyboard version was added: approving a Still makes the version that refers to it. */
  onAdded: () => Promise<void>;
}) {
  const queryClient = useQueryClient();
  const path = { projectId: variant.projectId, variantId: variant.id, version: storyboard.version };
  const key = ["projects", "one", variant.projectId, "variants", variant.id, "storyboards", storyboard.version, "generation"];
  const estimate = useQuery({
    queryKey: key,
    queryFn: async () => {
      const { data, response } = await api.GET(
        "/api/v1/projects/{projectId}/variants/{variantId}/storyboards/{version}/generation",
        { params: { path } },
      );
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
    refetchInterval: (query) =>
      query.state.data?.sceneStills.some((has) => WAITING.includes(has.still.state)) ? POLL_MS : false,
  });
  const [working, setWorking] = useState(false);
  const [refused, setRefused] = useState<string>();
  // One key for one wish to spend: a click sent twice, or sent again after the answer was
  // lost, generates once. A new key is made once the API has answered.
  const idempotencyKey = useRef<string>(undefined);

  // Whatever the API answered, the estimate is asked for again: it says where things stand now.
  const act = async (ask: (key: string) => Promise<{ ok: boolean; error: unknown }>, fallback: string) => {
    setWorking(true);
    setRefused(undefined);
    idempotencyKey.current ??= crypto.randomUUID();
    const { ok, error } = await ask(idempotencyKey.current).catch(() => ({ ok: false, error: undefined }));
    if (ok) {
      idempotencyKey.current = undefined;
    } else {
      setRefused(problemDetail(error) ?? fallback);
    }
    await queryClient.invalidateQueries({ queryKey: key });
    setWorking(false);
  };

  const confirm = (total: number) =>
    act(async (idempotency) => {
      const { error, response } = await api.POST(
        "/api/v1/projects/{projectId}/variants/{variantId}/storyboards/{version}/generation",
        { params: { path, header: { "Idempotency-Key": idempotency } }, body: { confirmedTotal: total } },
      );
      return { ok: response.ok, error };
    }, "The generation could not be started.");

  const approve = (still: Still) =>
    act(async () => {
      const { error, response } = await api.POST("/api/v1/stills/{stillId}/approve", { params: { path: { stillId: still.id } } });
      // The version that uses the Still is the next one, and that is the one to read and render now.
      if (response.ok) await onAdded();
      return { ok: response.ok, error };
    }, "The Still could not be approved.");

  // The button names what another costs now, and that amount is what is confirmed.
  const another = (still: Still, confirmedAmount: number) =>
    act(async (idempotency) => {
      const { error, response } = await api.POST("/api/v1/stills/{stillId}/another", {
        params: { path: { stillId: still.id }, header: { "Idempotency-Key": idempotency } },
        body: { confirmedAmount },
      });
      return { ok: response.ok, error };
    }, "Another Still could not be asked for.");

  const generated = storyboard.scenes.filter((scene) => scene.description !== null);
  const data = estimate.data;

  return (
    <section className="flex flex-col gap-3" data-testid="generation">
      <h3 className="text-lg font-semibold tracking-tight">Generated pictures</h3>
      <p className="text-sm text-muted-foreground">
        In Hybrid a Scene can have a generated backdrop behind the Product. The Product itself is always its photo.
        Each backdrop is a Still, made once for the Project and used by every Variant of it. Making one costs money,
        so nothing is generated until you have seen the estimate and confirmed it. Approving a Still makes the next
        Storyboard version, which uses it: that version is the one to render. A render generates nothing and no provider is paid for it.
      </p>
      {estimate.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The estimate could not be loaded.
        </p>
      ) : !data ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        <>
          {!data.available && (
            <p className="w-fit rounded-md border bg-muted px-2 py-1 text-sm" data-testid="generation-unavailable">
              {data.unavailable}
            </p>
          )}
          {data.available && data.imageProvider === "Fake" && (
            <p className="w-fit rounded-md border bg-muted px-2 py-1 text-sm" data-testid="generation-fake">
              Pictures here come from the fake image provider: a fixed striped picture, not a picture of anything. No
              provider is called and nobody is charged; the amounts stand in for a real provider&apos;s.
            </p>
          )}
          {data.available && generated.length === 0 && (
            <p className="text-sm text-muted-foreground" data-testid="generation-nothing">
              No Scene of this version is generated.
            </p>
          )}
          <p className="text-sm" data-testid="generation-spent">
            <span className="text-muted-foreground">Generation for this Project so far: </span>
            <span className="font-medium">{money(data.spent, data.currency)}</span>
            <span className="text-muted-foreground">
              {" "}
              of a ceiling of {money(data.ceiling, data.currency)}, estimated from configured rates. Rejected takes
              count.
            </span>
          </p>
          {data.stills.length > 0 && (
            <div className="flex flex-col gap-2 rounded-lg border p-4" data-testid="generation-estimate">
              <p className="text-sm font-medium">Confirming will generate:</p>
              <ul className="list-disc pl-5 text-sm">
                {data.stills.map((still) => (
                  <li key={still.scenePosition}>
                    A Still for Scene {still.scenePosition}, estimated at {money(still.estimatedAmount, data.currency)}
                  </li>
                ))}
              </ul>
              <p className="text-sm">
                <span className="text-muted-foreground">Estimated total: </span>
                <span className="font-medium" data-testid="generation-total">
                  {money(data.total, data.currency)}
                </span>
              </p>
              <div>
                <Button onClick={() => confirm(data.total)} disabled={working} data-testid="generation-confirm">
                  {working ? "Starting…" : `Confirm and generate for ${money(data.total, data.currency)}`}
                </Button>
              </div>
            </div>
          )}
          {data.sceneStills.length > 0 && (
            <ul className="flex flex-col divide-y rounded-lg border">
              {data.sceneStills.map(({ scenePosition, still }) => (
                <StillRow
                  key={still.id}
                  scenePosition={scenePosition}
                  still={still}
                  anotherFor={data.anotherStillAmount === null ? undefined : money(data.anotherStillAmount, data.currency)}
                  disabled={working}
                  onApprove={() => approve(still)}
                  onAnother={() => data.anotherStillAmount !== null && another(still, data.anotherStillAmount)}
                />
              ))}
            </ul>
          )}
        </>
      )}
      {refused && (
        <p role="alert" className="text-sm text-destructive" data-testid="generation-refused">
          {refused}
        </p>
      )}
      <FailedStills variant={variant} descriptions={generated.map((scene) => scene.description!)} />
    </section>
  );
}

function StillRow({
  scenePosition,
  still,
  anotherFor,
  disabled,
  onApprove,
  onAnother,
}: {
  scenePosition: number;
  still: Still;
  /** What another Still is estimated at now. Absent when nothing can be generated. */
  anotherFor?: string;
  disabled: boolean;
  onApprove: () => void;
  onAnother: () => void;
}) {
  const made = still.state === "ReadyForReview" || still.state === "Approved";
  return (
    <li className="flex gap-4 p-4" data-testid="still">
      {made && (
        // Not next/image: the file comes from the API, which only serves it to a signed-in member of the Organization.
        // eslint-disable-next-line @next/next/no-img-element
        <img
          src={`/api/v1/stills/${still.id}/content`}
          alt={`The generated backdrop of Scene ${scenePosition}`}
          loading="lazy"
          className="aspect-[9/16] w-28 shrink-0 rounded-lg border bg-muted object-cover"
        />
      )}
      <div className="flex min-w-0 flex-col gap-2">
        <p className="text-sm" aria-live="polite">
          <span className="text-muted-foreground">Still for Scene {scenePosition} · </span>
          <span className="font-medium" data-testid="still-state">
            {STATES[still.state]}
          </span>
        </p>
        {still.fake && (
          <p className="w-fit rounded-md border bg-muted px-2 py-1 text-sm" data-testid="still-fake">
            Fake: made by the fake image provider. It is a fixed picture and shows nothing of the description.
          </p>
        )}
        <p className="break-words text-sm">
          <span className="text-muted-foreground">Description: </span>
          {still.description}
        </p>
        {still.state === "Queued" && still.retryAt && (
          <p className="text-sm text-muted-foreground" data-testid="still-retry">
            Attempt {still.attempt} failed without being charged for. It will be tried again after{" "}
            {new Date(still.retryAt).toLocaleTimeString()}.
          </p>
        )}
        {still.cost && (
          <p className="text-sm text-muted-foreground" data-testid="still-cost">
            Estimated cost: {money(still.cost.estimatedAmount, still.cost.currency)}
            {still.cost.model ? ` · ${still.cost.provider} · ${still.cost.model}` : ""}
          </p>
        )}
        {made && (
          <div className="flex flex-wrap gap-2">
            {still.state === "ReadyForReview" && (
              <Button size="sm" onClick={onApprove} disabled={disabled} data-testid="still-approve">
                Approve
              </Button>
            )}
            {anotherFor && (
              <Button size="sm" variant="outline" onClick={onAnother} disabled={disabled} data-testid="still-another">
                Ask for another for {anotherFor}
              </Button>
            )}
          </div>
        )}
        {made && anotherFor && (
          <p className="text-sm text-muted-foreground">
            Another is a new paid generation. This one is then rejected and kept, with what it cost.
          </p>
        )}
      </div>
    </li>
  );
}

/**
 * Why the newest Still of a picture this version describes was not made, when it was not: the
 * Scene then has no Still, and the estimate above offers it again.
 */
function FailedStills({ variant, descriptions }: { variant: Variant; descriptions: string[] }) {
  const stills = useQuery({
    queryKey: ["projects", "one", variant.projectId, "stills"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects/{projectId}/stills", {
        params: { path: { projectId: variant.projectId }, query: { pageSize: 50 } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
    refetchInterval: POLL_MS * 5,
  });
  // Newest first: only the latest take of each picture says where it stands.
  const failed = descriptions
    .map((description) => stills.data?.items.find((still) => still.description === description))
    .filter((still): still is Still => still?.state === "Failed");
  if (failed.length === 0) return null;

  return (
    <ul className="flex flex-col gap-2">
      {failed.map((still) => (
        <li key={still.id} role="alert" className="text-sm text-destructive" data-testid="still-failure">
          {still.failure?.message ?? "The picture could not be made."}
          {still.failure?.billed ? " The provider charged for the attempt all the same." : ""}
        </li>
      ))}
    </ul>
  );
}
