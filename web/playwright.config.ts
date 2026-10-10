import { defineConfig, devices } from "@playwright/test";

// The one browser test (e2e/create-video.spec.ts) drives a system that is already
// running: the web app, the API, the worker, the database and object storage, as
// `docker compose up` starts them. See "Test" in ../README.md.
export default defineConfig({
  testDir: "e2e",
  // A render takes a minute or two, and longer the first time the worker cuts a Product out.
  timeout: 12 * 60_000,
  expect: { timeout: 20_000 },
  workers: 1,
  reporter: "list",
  outputDir: "e2e/results",
  use: {
    baseURL: process.env.AFFIVIDEO_WEB_URL ?? "http://localhost:3000",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
