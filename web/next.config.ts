import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // The container runs .next/standalone/server.js with no node_modules beside it.
  output: "standalone",
  cacheComponents: true,
  partialPrefetching: true,
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
