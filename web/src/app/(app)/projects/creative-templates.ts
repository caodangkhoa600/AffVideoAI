import type { CreativeTemplate } from "@/lib/api/client";

/** The creative templates a Variant can have (CreativeTemplate in the domain), as a member reads them. */
export const CREATIVE_TEMPLATES: { value: CreativeTemplate; name: string; about: string }[] = [
  {
    value: "ProductShowcase",
    name: "Product Showcase",
    about: "Four Scenes: the Hook, the Product, its Facts, then a call to action.",
  },
  {
    value: "LuxuryCinematic",
    name: "Luxury Cinematic",
    about: "Three slower Scenes: the Hook, the Facts, then a call to action.",
  },
  {
    value: "ProblemSolution",
    name: "Problem–Solution",
    about: "The Hook states a problem and the Product arrives as the answer, then its Facts and a call to action.",
  },
];

export const creativeTemplateName = (value: CreativeTemplate) =>
  CREATIVE_TEMPLATES.find((template) => template.value === value)?.name ?? value;
