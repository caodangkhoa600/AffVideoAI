"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import type { ReviewFlag } from "@/lib/api/client";

/**
 * The Flagged for Review mark on a Storyboard version or a Rendered Video: which Withdrawn
 * Facts it used, in the words it used, and the button that clears the mark once a member
 * has reviewed the work. Draws nothing for work that is not flagged.
 */
export function FlaggedForReview({
  flags,
  subject,
  onClear,
}: {
  flags: ReviewFlag[];
  subject: "Storyboard version" | "Rendered Video";
  /**
   * Clears the flags of these Withdrawn Facts: the ones on screen, and not one raised since.
   * Answers why it could not, in words for the member, or nothing once it has.
   */
  onClear: (factIds: string[]) => Promise<string | undefined>;
}) {
  const [clearing, setClearing] = useState(false);
  const [failed, setFailed] = useState<string>();
  if (flags.length === 0) return null;

  const clear = async () => {
    setClearing(true);
    setFailed(undefined);
    setFailed(await onClear(flags.map((flag) => flag.factId)));
    setClearing(false);
  };

  return (
    <div
      className="flex flex-col gap-2 rounded-md border border-destructive/50 bg-destructive/5 px-3 py-2 text-sm"
      data-testid="flagged-for-review"
    >
      <p className="font-medium">
        Flagged for Review: this {subject} used {flags.length === 1 ? "a Fact that has" : "Facts that have"} since been
        Withdrawn.
      </p>
      <ul className="list-disc pl-5">
        {flags.map((flag) => (
          <li key={flag.factId} className="break-words">
            “{flag.text}” <span className="text-muted-foreground">· Withdrawn on {new Date(flag.withdrawnAt).toLocaleString()}</span>
          </li>
        ))}
      </ul>
      <p className="text-muted-foreground">
        It is kept as it is. Review it, then clear the flag; clearing is recorded in the audit log.
      </p>
      <div>
        <Button size="sm" variant="outline" onClick={clear} disabled={clearing} data-testid="flag-clear">
          {clearing ? "Clearing…" : "Clear the flag"}
        </Button>
      </div>
      {failed && (
        <p role="alert" className="text-destructive">
          {failed}
        </p>
      )}
    </div>
  );
}
