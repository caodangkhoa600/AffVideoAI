import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // The container runs .next/standalone/server.js with no node_modules beside it.
  output: "standalone",
  cacheComponents: true,
  partialPrefetching: true,
  experimental: {
    // An upload passes through src/proxy.ts on its way to the API, and the proxy
    // forwards only this much of a request. The API takes a file of up to 20 MB;
    // the rest is room for the form around it.
    proxyClientMaxBodySize: "21mb",
  },
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
