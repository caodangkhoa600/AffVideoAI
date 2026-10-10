"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";
import { Field } from "@/components/field";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { api, fieldErrors, problemDetail, type Fact, type FactRequest, type FactState } from "@/lib/api/client";

// The API's limits and languages (Fact and ContentLanguages in the domain).
const TEXT_MAX_LENGTH = 500;
const SOURCE_MAX_LENGTH = 500;
const LANGUAGES = [
  { code: "vi", name: "Vietnamese" },
  { code: "en", name: "English" },
];

// Far more than a Product is expected to have in one state; the count says so if there are more.
const PAGE_SIZE = 200;

const GROUPS: { state: FactState; empty: string }[] = [
  { state: "Proposed", empty: "No Proposed Facts." },
  { state: "Confirmed", empty: "No Confirmed Facts yet. A script can only use Confirmed Facts." },
  { state: "Withdrawn", empty: "No Withdrawn Facts." },
];

const factsKey = (productId: string) => ["products", "one", productId, "facts"];

/** The Product's Facts, grouped by state: adding one, confirming, editing and withdrawing. */
export function ProductFacts({ productId }: { productId: string }) {
  const queryClient = useQueryClient();
  // A new key empties the form after a Fact is added.
  const [added, setAdded] = useState(0);

  const add = async (request: FactRequest) => {
    const { error, response } = await api
      .POST("/api/v1/products/{productId}/facts", { params: { path: { productId } }, body: request })
      .catch(() => ({ error: undefined, response: undefined }));
    if (!response?.ok) return refusal(error, "The Fact could not be added.");
    await queryClient.invalidateQueries({ queryKey: factsKey(productId) });
    setAdded((count) => count + 1);
  };

  return (
    <section className="flex flex-col gap-6" data-testid="product-facts">
      <div className="flex flex-col gap-1">
        <h2 className="text-xl font-semibold tracking-tight">Facts</h2>
        <p className="text-sm text-muted-foreground">
          One statement each, in the language of the video. A new Fact is Proposed, and a script may use it only once
          it is Confirmed.
        </p>
      </div>
      <FactForm key={added} submitLabel="Add Fact" busyLabel="Adding…" onSubmit={add} />
      {GROUPS.map((group) => (
        <FactGroup key={group.state} productId={productId} state={group.state} empty={group.empty} />
      ))}
    </section>
  );
}

type Refusal = { fields: Record<string, string[]>; message?: string };

// A 400 names the fields at fault; anything else is said once, under the form.
function refusal(error: unknown, fallback: string): Refusal {
  const fields = fieldErrors(error);
  return Object.keys(fields).length > 0 ? { fields } : { fields, message: problemDetail(error) ?? fallback };
}

/** The Product's Facts in one state, oldest first, and how many there are. */
export function useFacts(productId: string, state: FactState) {
  return useQuery({
    queryKey: [...factsKey(productId), state],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/products/{productId}/facts", {
        params: { path: { productId }, query: { state, pageSize: PAGE_SIZE } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
}

function FactGroup({ productId, state, empty }: { productId: string; state: FactState; empty: string }) {
  const facts = useFacts(productId, state);

  return (
    <div className="flex flex-col gap-3" data-testid={`facts-${state.toLowerCase()}`}>
      <h3 className="font-medium">
        {state}
        {facts.data && <span className="text-muted-foreground"> · {facts.data.total}</span>}
      </h3>
      {facts.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The {state} Facts could not be loaded.
        </p>
      ) : !facts.data ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : facts.data.items.length === 0 ? (
        <p className="text-sm text-muted-foreground">{empty}</p>
      ) : (
        <>
          <ul className="flex flex-col divide-y rounded-lg border">
            {facts.data.items.map((fact) => (
              <FactRow key={fact.id} fact={fact} />
            ))}
          </ul>
          {facts.data.total > facts.data.items.length && (
            <p className="text-sm text-muted-foreground">
              Showing the oldest {facts.data.items.length} of {facts.data.total}.
            </p>
          )}
        </>
      )}
    </div>
  );
}

function FactRow({ fact }: { fact: Fact }) {
  const queryClient = useQueryClient();
  const [mode, setMode] = useState<"shown" | "editing" | "withdrawing">("shown");
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState<string>();
  const path = { productId: fact.productId, factId: fact.id };

  // Reloaded whatever the answer: a refusal means the Fact is not in the state shown here.
  const reload = () => queryClient.invalidateQueries({ queryKey: factsKey(fact.productId) });

  const change = async (to: "confirm" | "withdraw", fallback: string) => {
    setBusy(true);
    setFailed(undefined);
    const { error, response } = await api
      .POST(`/api/v1/products/{productId}/facts/{factId}/${to}`, { params: { path } })
      .catch(() => ({ error: undefined, response: undefined }));
    if (!response?.ok) setFailed(problemDetail(error) ?? fallback);
    await reload();
    setBusy(false);
    setMode("shown");
  };

  const replace = async (request: FactRequest) => {
    const { error, response } = await api
      .POST("/api/v1/products/{productId}/facts/{factId}/replace", { params: { path }, body: request })
      .catch(() => ({ error: undefined, response: undefined }));
    if (!response?.ok) {
      if (response?.status === 409) await reload();
      return refusal(error, "The Fact could not be changed.");
    }
    await reload();
    setMode("shown");
  };

  if (mode === "editing") {
    return (
      <li className="flex flex-col gap-3 p-4" data-testid="fact">
        <p className="text-sm text-muted-foreground">
          A Fact&apos;s text is never changed. Saving withdraws this Fact and adds a new Proposed one
          {fact.state === "Confirmed" && ", which has to be Confirmed again"}.
        </p>
        <FactForm
          fact={fact}
          submitLabel="Withdraw and propose new"
          busyLabel="Saving…"
          onSubmit={replace}
          onCancel={() => setMode("shown")}
        />
      </li>
    );
  }
  return (
    <li className="flex flex-col gap-2 p-4" data-testid="fact">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-1">
          <p className={fact.state === "Withdrawn" ? "break-words text-muted-foreground line-through" : "break-words"}>
            {fact.text}
          </p>
          <p className="break-words text-sm text-muted-foreground">
            {languageName(fact.language)}
            {" · "}
            {fact.source ? `Source: ${fact.source}` : "No source"}
          </p>
          {fact.confirmedAt && (
            <p className="text-sm text-muted-foreground" data-testid="fact-confirmed">
              Confirmed by {fact.confirmedByEmail ?? "a member"} on {when(fact.confirmedAt)}
            </p>
          )}
          {fact.withdrawnAt && <p className="text-sm text-muted-foreground">Withdrawn on {when(fact.withdrawnAt)}</p>}
        </div>
        {fact.state !== "Withdrawn" &&
          (mode === "withdrawing" ? (
            <div className="flex items-center gap-2">
              <span className="text-sm">Withdraw this Fact?</span>
              <Button
                variant="destructive"
                size="sm"
                disabled={busy}
                onClick={() => change("withdraw", "The Fact could not be withdrawn.")}
              >
                {busy ? "Withdrawing…" : "Yes, withdraw"}
              </Button>
              <Button variant="outline" size="sm" disabled={busy} onClick={() => setMode("shown")}>
                Cancel
              </Button>
            </div>
          ) : (
            <div className="flex gap-2">
              {fact.state === "Proposed" && (
                <Button size="sm" disabled={busy} onClick={() => change("confirm", "The Fact could not be confirmed.")}>
                  {busy ? "Confirming…" : "Confirm"}
                </Button>
              )}
              <Button variant="outline" size="sm" disabled={busy} onClick={() => setMode("editing")}>
                Edit
              </Button>
              <Button variant="outline" size="sm" disabled={busy} onClick={() => setMode("withdrawing")}>
                Withdraw
              </Button>
            </div>
          ))}
      </div>
      {failed && (
        <p role="alert" className="text-sm text-destructive">
          {failed}
        </p>
      )}
    </li>
  );
}

/** Adds a Fact, or proposes the one that takes the place of the Fact it is given. */
function FactForm({
  fact,
  submitLabel,
  busyLabel,
  onSubmit,
  onCancel,
}: {
  fact?: Fact;
  submitLabel: string;
  busyLabel: string;
  /** Answers why the Fact was refused, or nothing when it was saved. */
  onSubmit: (request: FactRequest) => Promise<Refusal | undefined>;
  onCancel?: () => void;
}) {
  const id = useId();
  const [text, setText] = useState(fact?.text ?? "");
  const [language, setLanguage] = useState(fact?.language ?? LANGUAGES[0].code);
  const [source, setSource] = useState(fact?.source ?? "");
  const [saving, setSaving] = useState(false);
  const [refused, setRefused] = useState<Refusal>({ fields: {} });

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (text.trim() === "") {
      setRefused({ fields: { text: ["Enter the Fact."] } });
      return;
    }
    setSaving(true);
    const answer = await onSubmit({ text, language, source: source.trim() === "" ? null : source });
    // Saved means the form is gone or about to be emptied; only a refusal is shown here.
    if (answer) {
      setRefused(answer);
      setSaving(false);
    }
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
      <Field
        id={`${id}-text`}
        label="Fact"
        value={text}
        maxLength={TEXT_MAX_LENGTH}
        onChange={(event) => setText(event.target.value)}
        error={refused.fields.text?.join(" ")}
        hint="A single statement, such as a battery life or a material."
      />
      <div className="grid gap-4 sm:grid-cols-[12rem_1fr]">
        <div className="flex flex-col gap-2">
          <Label htmlFor={`${id}-language`}>Language</Label>
          <select
            id={`${id}-language`}
            value={language}
            onChange={(event) => setLanguage(event.target.value)}
            aria-invalid={refused.fields.language ? true : undefined}
            className="h-8 w-full rounded-lg border border-input bg-transparent px-2.5 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 md:text-sm dark:bg-input/30"
          >
            {LANGUAGES.map((option) => (
              <option key={option.code} value={option.code}>
                {option.name}
              </option>
            ))}
          </select>
          {refused.fields.language && (
            <p role="alert" className="text-sm text-destructive">
              {refused.fields.language.join(" ")}
            </p>
          )}
        </div>
        <Field
          id={`${id}-source`}
          label="Source (optional)"
          value={source}
          maxLength={SOURCE_MAX_LENGTH}
          onChange={(event) => setSource(event.target.value)}
          error={refused.fields.source?.join(" ")}
          hint="Where it comes from, so that it can be checked again."
        />
      </div>
      {refused.message && (
        <p role="alert" className="text-sm text-destructive">
          {refused.message}
        </p>
      )}
      <div className="flex gap-3">
        <Button type="submit" disabled={saving}>
          {saving ? busyLabel : submitLabel}
        </Button>
        {onCancel && (
          <Button type="button" variant="outline" disabled={saving} onClick={onCancel}>
            Cancel
          </Button>
        )}
      </div>
    </form>
  );
}

const languageName = (code: string) => LANGUAGES.find((language) => language.code === code)?.name ?? code;

const when = (instant: string) => new Date(instant).toLocaleString();
