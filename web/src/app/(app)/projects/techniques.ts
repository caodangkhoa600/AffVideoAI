import type { Technique } from "@/lib/api/client";

/** How a Scene is produced (Technique in the domain), as a member reads it. */
export const TECHNIQUES: Record<Technique, string> = {
  StaticImage: "Static image",
  ImageMotion: "Image motion",
  ImageToVideo: "Image-to-video",
  VideoAsset: "Video asset",
  TextAnimation: "Text animation",
  ThreeDRender: "3D render",
};
