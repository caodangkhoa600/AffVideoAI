import createClient from "openapi-fetch";
import type { components, paths } from "./schema";

// Same origin: src/proxy.ts passes /api on to the API. The types come from the
// API's OpenAPI description (npm run generate:api).
export const api = createClient<paths>({ baseUrl: "/" });

export type Session = components["schemas"]["SessionResponse"];
export type Member = components["schemas"]["MemberResponse"];

const CHANGES_STATE = new Set(["POST", "PUT", "PATCH", "DELETE"]);

// The API refuses a state-changing request without an anti-forgery token. A
// token stops being valid when the member signs in or out, so one is fetched
// for each such request and never kept.
api.use({
  async onRequest({ request }) {
    if (!CHANGES_STATE.has(request.method)) return;
    const { data } = await api.GET("/api/v1/antiforgery-token");
    if (data) request.headers.set("X-CSRF-TOKEN", data.requestToken);
    return request;
  },
});

/** The messages of a 400 from the API, by field, with the field names in lower case. */
export function fieldErrors(error: unknown): Record<string, string[]> {
  const errors = (error as components["schemas"]["HttpValidationProblemDetails"] | undefined)?.errors ?? {};
  return Object.fromEntries(Object.entries(errors).map(([field, messages]) => [field.toLowerCase(), messages]));
}
