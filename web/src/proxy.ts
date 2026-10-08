import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";

// The browser only ever talks to the web app's own origin, so the session
// cookie is same-site. Requests under /api are passed on to the API here.
// The address is read on each request: it is set when the container starts,
// not when the image is built.
export function proxy(request: NextRequest) {
  const api = process.env.API_INTERNAL_URL;
  if (!api) {
    return NextResponse.json(
      { title: "API_INTERNAL_URL is not set on the web app." },
      { status: 502 },
    );
  }
  const { pathname, search } = request.nextUrl;
  return NextResponse.rewrite(new URL(pathname + search, api));
}

export const config = {
  matcher: "/api/:path*",
};
