import createClient from "openapi-fetch";
import type { components, paths } from "./schema";

// Same origin: src/proxy.ts passes /api on to the API. The types come from the
// API's OpenAPI description (npm run generate:api).
export const api = createClient<paths>({ baseUrl: "/" });

export type Session = components["schemas"]["SessionResponse"];
export type Member = components["schemas"]["MemberResponse"];
export type Product = components["schemas"]["ProductResponse"];
export type ProductRequest = components["schemas"]["ProductRequest"];
export type ProductStatus = components["schemas"]["ProductStatus"];
export type ProductAsset = components["schemas"]["ProductAssetResponse"];
export type ProductAssetKind = components["schemas"]["ProductAssetKind"];
export type Fact = components["schemas"]["FactResponse"];
export type FactRequest = components["schemas"]["FactRequest"];
export type FactState = components["schemas"]["FactState"];
export type Project = components["schemas"]["ProjectResponse"];
export type Variant = components["schemas"]["VariantResponse"];
export type CreativeTemplate = components["schemas"]["CreativeTemplate"];
export type VariantAudio = components["schemas"]["VariantAudioResponse"];
export type VariantAudioKind = components["schemas"]["VariantAudioKind"];
export type Storyboard = components["schemas"]["StoryboardResponse"];
export type Scene = components["schemas"]["SceneResponse"];
export type SceneEdit = components["schemas"]["SceneEditRequest"];
export type SceneLayout = components["schemas"]["SceneLayout"];
export type Technique = components["schemas"]["Technique"];
export type ReviewFlag = components["schemas"]["ReviewFlagResponse"];
export type RenderJob = components["schemas"]["RenderJobResponse"];
export type RenderJobState = components["schemas"]["RenderJobState"];
export type RenderedVideo = components["schemas"]["RenderedVideoResponse"];
export type RenderedVideoState = components["schemas"]["RenderedVideoState"];
export type EstimatedAmount = components["schemas"]["EstimatedAmountResponse"];
export type LabProduct = components["schemas"]["LabProductResponse"];
export type Campaign = components["schemas"]["CampaignResponse"];
export type CampaignVariant = components["schemas"]["CampaignVariantResponse"];
export type SocialAccount = components["schemas"]["SocialAccountResponse"];
export type SocialPlatform = components["schemas"]["SocialPlatform"];
export type AffiliateLink = components["schemas"]["AffiliateLinkResponse"];
export type PublishedPost = components["schemas"]["PublishedPostResponse"];
// Only ever an optional query parameter, so the description has it as nullable.
export type RenderedVideoOrder = NonNullable<components["schemas"]["RenderedVideoOrder"]>;

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

/** The messages of a 400 from the API, by field, named as the request names them. */
export function fieldErrors(error: unknown): Record<string, string[]> {
  return (error as components["schemas"]["HttpValidationProblemDetails"] | undefined)?.errors ?? {};
}

/** What a refusal that is not about a field says, such as a 409: the API's own words for the member. */
export function problemDetail(error: unknown): string | undefined {
  return (error as components["schemas"]["ProblemDetails"] | undefined)?.detail ?? undefined;
}
