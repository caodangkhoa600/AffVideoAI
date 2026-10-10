/** The steps of creating a video, in order, as a member reads them. */
export const STEPS = [
  { id: "product", title: "Product" },
  { id: "photos", title: "Photos" },
  { id: "facts", title: "Facts" },
  { id: "direction", title: "Creative direction" },
  { id: "storyboard", title: "Storyboard" },
  { id: "sound", title: "Sound" },
  { id: "video", title: "Video" },
] as const;

export type Step = (typeof STEPS)[number]["id"];

/**
 * Where a member is in creating one video: the step, and what the video has been given so far.
 * It is all in the page's address, so the address alone brings a member back to the same step.
 */
export type Position = { step: Step; productId?: string; projectId?: string; variantId?: string };

const isStep = (value: string | null): value is Step => STEPS.some((step) => step.id === value);

/** The position an address names. Nothing when it names no step: the address of the wizard itself. */
export function positionOf(query: string): Position | undefined {
  const params = new URLSearchParams(query);
  const step = params.get("step");
  if (!isStep(step)) return undefined;
  const projectId = params.get("projectId") ?? undefined;
  return {
    step,
    productId: params.get("productId") ?? undefined,
    projectId,
    // A Variant is only ever found under its Project.
    variantId: projectId ? (params.get("variantId") ?? undefined) : undefined,
  };
}

export function hrefOf(position: Position): string {
  const params = new URLSearchParams({ step: position.step });
  if (position.productId) params.set("productId", position.productId);
  if (position.projectId) params.set("projectId", position.projectId);
  if (position.variantId) params.set("variantId", position.variantId);
  return `/create?${params}`;
}

/**
 * Whether a step can be shown yet. The Product comes first; a Storyboard, its sound and a video
 * need the creative direction to have been chosen, which is when the video gets its Variant.
 */
export function reachable(step: Step, position: Position): boolean {
  if (step === "product") return true;
  if (step === "storyboard" || step === "sound" || step === "video") return !!position.variantId;
  return !!position.productId;
}

// The position is remembered in this browser, for each member apart, so that the
// "Create video" button leads back to the video being made. It holds identifiers
// and a step name: nothing that signs anyone in.
const storageKey = (memberId: string) => `affivideo.create-video.${memberId}`;

/** Where this member last was in the wizard, in this browser. Nothing when they have no video under way. */
export function savedPosition(memberId: string): Position | undefined {
  try {
    return positionOf(window.localStorage.getItem(storageKey(memberId)) ?? "");
  } catch {
    // Storage is switched off: the wizard starts from the beginning.
    return undefined;
  }
}

export function savePosition(memberId: string, position: Position) {
  try {
    window.localStorage.setItem(storageKey(memberId), hrefOf(position).split("?")[1]);
  } catch {
    // Storage is switched off: the address still holds the position.
  }
}

export function forgetPosition(memberId: string) {
  try {
    window.localStorage.removeItem(storageKey(memberId));
  } catch {
    // Nothing was remembered.
  }
}
