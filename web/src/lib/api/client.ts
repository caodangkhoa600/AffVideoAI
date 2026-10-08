import createClient from "openapi-fetch";
import type { paths } from "./schema";

// Same origin: src/proxy.ts passes /api on to the API. The types come from the
// API's OpenAPI description (npm run generate:api).
export const api = createClient<paths>({ baseUrl: "/" });
