"use client";

import { useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api/client";
import { useSession } from "@/lib/session";

const LINKS = [
  { href: "/", label: "Home" },
  { href: "/products", label: "Products" },
  { href: "/members", label: "Members" },
  { href: "/settings", label: "Settings" },
] as const;

export function AppHeader() {
  const session = useSession();
  const queryClient = useQueryClient();
  const router = useRouter();
  const [signingOut, setSigningOut] = useState(false);
  const [signOutFailed, setSignOutFailed] = useState(false);

  // The session ended while the page was open.
  useEffect(() => {
    if (session.isError) router.replace("/sign-in");
  }, [session.isError, router]);

  const signOut = async () => {
    setSigningOut(true);
    setSignOutFailed(false);
    const { response } = await api.DELETE("/api/v1/session").catch(() => ({ response: undefined }));
    // Only leave once the API has ended the session: otherwise the cookie
    // still works and the member would wrongly believe they had signed out.
    if (!response?.ok) {
      setSigningOut(false);
      setSignOutFailed(true);
      return;
    }
    queryClient.clear();
    router.replace("/sign-in");
  };

  return (
    <header className="border-b">
      <div className="mx-auto flex w-full max-w-3xl flex-wrap items-center gap-x-6 gap-y-2 px-6 py-3">
        <span className="font-semibold" data-testid="organization-name">
          {session.data?.organization.name ?? "AffiVideo"}
        </span>
        <nav className="flex gap-4 text-sm">
          {LINKS.map(({ href, label }) => (
            <Link key={href} href={href} className="text-muted-foreground hover:text-foreground">
              {label}
            </Link>
          ))}
        </nav>
        <div className="ml-auto flex items-center gap-3 text-sm">
          {signOutFailed && (
            <span role="alert" className="text-destructive">
              Could not sign out. You are still signed in; try again.
            </span>
          )}
          {session.data && (
            <span className="text-muted-foreground" data-testid="signed-in-as">
              {session.data.member.email} · {session.data.member.role}
            </span>
          )}
          <Button variant="outline" size="sm" onClick={signOut} disabled={signingOut}>
            Sign out
          </Button>
        </div>
      </div>
    </header>
  );
}
