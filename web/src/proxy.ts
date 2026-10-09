import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";

// Pages anyone may open. Every other page needs a session.
const PUBLIC_PAGES = ["/sign-in", "/status"];

// The browser only ever talks to the web app's own origin, so the session
// cookie is same-site. Requests under /api are passed on to the API here.
// The address is read on each request: it is set when the container starts,
// not when the image is built.
export async function proxy(request: NextRequest) {
  const api = process.env.API_INTERNAL_URL;
  const { pathname, search } = request.nextUrl;

  if (pathname.startsWith("/api/")) {
    if (!api) {
      return NextResponse.json(
        { title: "API_INTERNAL_URL is not set on the web app." },
        { status: 502 },
      );
    }
    return NextResponse.rewrite(new URL(pathname + search, api));
  }

  if (PUBLIC_PAGES.includes(pathname) || (await signedIn(api, request))) {
    return NextResponse.next();
  }

  const signIn = request.nextUrl.clone();
  signIn.pathname = "/sign-in";
  signIn.search = pathname === "/" ? "" : `?next=${encodeURIComponent(pathname + search)}`;
  return NextResponse.redirect(signIn);
}

// The API decides whether the cookie is a live session; its presence alone proves nothing.
async function signedIn(api: string | undefined, request: NextRequest) {
  const cookie = request.headers.get("cookie");
  if (!api || !cookie) return false;
  try {
    const session = await fetch(new URL("/api/v1/session", api), {
      headers: { cookie },
      cache: "no-store",
    });
    return session.ok;
  } catch {
    return false;
  }
}

export const config = {
  matcher: "/((?!_next/static|_next/image|favicon.ico|healthz).*)",
};
