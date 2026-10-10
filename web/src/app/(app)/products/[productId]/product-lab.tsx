"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Field, TextAreaField } from "@/components/field";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { api, fieldErrors, type LabProduct } from "@/lib/api/client";

// The API's limit (Product in the domain).
const RESEARCH_NOTES_MAX_LENGTH = 4000;

const SELECT =
  "h-8 w-full rounded-lg border border-input bg-transparent px-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30";

/**
 * What the Affiliate Lab keeps about the Product: the shortlist, the research notes and
 * the commission. Only drawn for an Organization that has the Lab.
 */
export function ProductLab({ productId }: { productId: string }) {
  const lab = useQuery({
    queryKey: ["lab", "products", "one", productId],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/lab/products/{productId}", { params: { path: { productId } } });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  return (
    <section className="flex flex-col gap-4" data-testid="product-lab">
      <div className="flex flex-col gap-1">
        <h2 className="text-xl font-semibold tracking-tight">Affiliate Lab</h2>
        <p className="text-sm text-muted-foreground">
          Your research on this Product, and what the affiliate programme pays for it when you know.
        </p>
      </div>
      {lab.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The Lab&apos;s notes could not be loaded.
        </p>
      ) : !lab.data ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        // Keyed so the form starts again from what was saved.
        <LabForm key={JSON.stringify(lab.data)} product={lab.data} />
      )}
    </section>
  );
}

type CommissionKind = "Unknown" | "Rate" | "Amount";

function LabForm({ product }: { product: LabProduct }) {
  const queryClient = useQueryClient();
  const [shortlisted, setShortlisted] = useState(product.shortlisted);
  const [researchNotes, setResearchNotes] = useState(product.researchNotes);
  const [kind, setKind] = useState<CommissionKind>(
    product.commissionRatePercent !== null ? "Rate" : product.commissionAmount !== null ? "Amount" : "Unknown",
  );
  const [rate, setRate] = useState(product.commissionRatePercent?.toString() ?? "");
  const [amount, setAmount] = useState(product.commissionAmount?.toString() ?? "");
  const [currency, setCurrency] = useState(product.commissionCurrency ?? "VND");
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [refused, setRefused] = useState<Record<string, string>>({});

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    setSaved(false);
    const problems: Record<string, string> = {};
    if (kind === "Rate" && (rate.trim() === "" || Number.isNaN(Number(rate)))) {
      problems.commissionRatePercent = "Enter the rate as a number, such as 12.5.";
    }
    if (kind === "Amount" && (amount.trim() === "" || Number.isNaN(Number(amount)))) {
      problems.commissionAmount = "Enter the amount as a number.";
    }
    setRefused(problems);
    if (Object.keys(problems).length > 0) return;

    setSaving(true);
    const { error, response } = await api
      .PUT("/api/v1/lab/products/{productId}", {
        params: { path: { productId: product.productId } },
        body: {
          shortlisted,
          researchNotes,
          commissionRatePercent: kind === "Rate" ? Number(rate) : null,
          commissionAmount: kind === "Amount" ? Number(amount) : null,
          commissionCurrency: kind === "Amount" ? currency.trim().toUpperCase() : null,
        },
      })
      .catch(() => ({ error: undefined, response: undefined }));
    if (response?.ok) {
      setSaved(true);
      await queryClient.invalidateQueries({ queryKey: ["lab", "products"] });
    } else {
      const fields = Object.fromEntries(Object.entries(fieldErrors(error)).map(([field, messages]) => [field, messages.join(" ")]));
      setRefused(Object.keys(fields).length > 0 ? fields : { form: "The Lab's notes could not be saved." });
    }
    setSaving(false);
  };

  return (
    <form onSubmit={save} noValidate className="flex flex-col gap-4">
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={shortlisted}
          onChange={(event) => setShortlisted(event.target.checked)}
          data-testid="product-shortlisted"
        />
        On the shortlist
      </label>
      <TextAreaField
        id="research-notes"
        label="Research notes"
        value={researchNotes}
        maxLength={RESEARCH_NOTES_MAX_LENGTH}
        onChange={(event) => setResearchNotes(event.target.value)}
        error={refused.researchNotes}
      />
      <div className="grid gap-4 sm:grid-cols-[12rem_1fr_6rem]">
        <div className="flex flex-col gap-2">
          <Label htmlFor="commission-kind">Commission</Label>
          <select
            id="commission-kind"
            className={SELECT}
            value={kind}
            onChange={(event) => setKind(event.target.value as CommissionKind)}
          >
            <option value="Unknown">Not known</option>
            <option value="Rate">A rate</option>
            <option value="Amount">An amount for each order</option>
          </select>
        </div>
        {kind === "Rate" && (
          <Field
            id="commission-rate"
            label="Rate, in percent"
            inputMode="decimal"
            value={rate}
            onChange={(event) => setRate(event.target.value)}
            error={refused.commissionRatePercent}
            hint="From 0 to 100, with at most two decimal places."
          />
        )}
        {kind === "Amount" && (
          <>
            <Field
              id="commission-amount"
              label="Amount"
              inputMode="decimal"
              value={amount}
              onChange={(event) => setAmount(event.target.value)}
              error={refused.commissionAmount}
            />
            <Field
              id="commission-currency"
              label="Currency"
              value={currency}
              maxLength={3}
              onChange={(event) => setCurrency(event.target.value)}
              error={refused.commissionCurrency}
            />
          </>
        )}
      </div>
      {refused.form && (
        <p role="alert" className="text-sm text-destructive">
          {refused.form}
        </p>
      )}
      <div className="flex items-center gap-3">
        <Button type="submit" disabled={saving}>
          {saving ? "Saving…" : "Save"}
        </Button>
        {saved && !saving && (
          <p role="status" className="text-sm text-muted-foreground">
            Saved.
          </p>
        )}
      </div>
    </form>
  );
}
