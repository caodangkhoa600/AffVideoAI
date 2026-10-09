import type { CreativeTemplate } from "@/lib/api/client";

/** The creative templates a Variant can have (CreativeTemplate in the domain), as a member reads them. */
export const CREATIVE_TEMPLATES: { value: CreativeTemplate; name: string }[] = [
  { value: "ProductShowcase", name: "Product Showcase" },
  { value: "LuxuryCinematic", name: "Luxury Cinematic" },
  { value: "ProblemSolution", name: "Problem–Solution" },
];

export const creativeTemplateName = (value: CreativeTemplate) =>
  CREATIVE_TEMPLATES.find((template) => template.value === value)?.name ?? value;
