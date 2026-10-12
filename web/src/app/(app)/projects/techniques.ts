import type { RenderMode, Technique } from "@/lib/api/client";

/** How a Scene is produced (Technique in the domain), as a member reads it. */
export const TECHNIQUES: Record<Technique, string> = {
  StaticImage: "Static image",
  ImageMotion: "Image motion",
  GeneratedBackdrop: "Generated backdrop",
  GeneratedFootage: "Generated footage",
  VideoAsset: "Video asset",
  TextAnimation: "Text animation",
  ThreeDRender: "3D render",
};

/** The Render Modes a Variant can be made in (RenderMode in the domain), as a member reads them. */
export const RENDER_MODES: { value: RenderMode; name: string; about: string }[] = [
  {
    value: "ProductLock",
    name: "Product Lock",
    about: "Nothing is generated. The Product is its photo, cut out and only ever scaled, moved and rotated.",
  },
  {
    value: "Hybrid",
    name: "Hybrid",
    about:
      "The Hook Scene can have a generated backdrop behind the Product, which is still its photo. Generating costs money and is confirmed first. A render generates nothing.",
  },
];

export const renderModeName = (value: RenderMode) => RENDER_MODES.find((mode) => mode.value === value)?.name ?? value;
