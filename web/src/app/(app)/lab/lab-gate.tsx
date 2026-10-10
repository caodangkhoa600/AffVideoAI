"use client";

import Link from "next/link";
import { useSession } from "@/lib/session";

/**
 * Shows the Lab's pages to a member whose Organization has the Affiliate Lab, and
 * to anyone else what a page that does not exist would show. The API refuses the
 * same members on its own; this only keeps them from an empty page.
 */
export function LabGate({ children }: { children: React.ReactNode }) {
  const session = useSession();

  if (!session.data) return <p className="text-sm text-muted-foreground">Loading…</p>;
  if (!session.data.organization.affiliateLabEnabled) {
    return (
      <>
        <h1 className="text-3xl font-semibold tracking-tight">Page not found</h1>
        <p className="text-sm text-muted-foreground" data-testid="lab-unavailable">
          There is no such page.
        </p>
        <Link href="/" className="font-medium underline underline-offset-4">
          Home
        </Link>
      </>
    );
  }
  return children;
}
