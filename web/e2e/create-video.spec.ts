import { readFile } from "node:fs/promises";
import path from "node:path";
import { deflateSync } from "node:zlib";
import { expect, test } from "@playwright/test";

// The Owner the seed command creates (README, "Sign in").
const EMAIL = process.env.AFFIVIDEO_EMAIL ?? "owner@demo.affivideo.local";
const PASSWORD = process.env.AFFIVIDEO_PASSWORD ?? "demo-owner-password";

const FACTS = ["Pin dùng liên tục 30 giờ", "Chống nước chuẩn IPX5"];
const HOOK = "Nghe nhạc cả ngày không lo hết pin";

// A second of tone, which the API's own tests upload as narration too.
const NARRATION = path.join(__dirname, "..", "..", "tests", "AffiVideo.Api.Tests", "Fixtures", "tone.mp3");

// How long a render may take before the test gives up on it.
const LONGEST_RENDER_MS = 9 * 60_000;

/**
 * The whole way a member makes a video, through the create video wizard, with the
 * mock planner and the local renderer: no paid AI and no credentials but the member's.
 */
test("a member signs in and takes a new Product to an approved, downloaded video", async ({ page, context }) => {
  // A name of its own, so the test can be run again against the same system.
  const name = `Tai nghe AirBeat ${Date.now()}`;

  // Sign in. A page asked for without a session leads to the sign-in page.
  await page.goto("/");
  await expect(page).toHaveURL(/\/sign-in/);
  await page.getByLabel("Email").fill(EMAIL);
  await page.getByLabel("Password").fill(PASSWORD);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByTestId("signed-in-as")).toContainText(EMAIL);

  // The dashboard's quick create button starts the wizard at its first step.
  await page.getByTestId("quick-create").click();
  await expect(page).toHaveURL(/\/create\?step=product/);

  // Create the Product. A member with none yet is shown the form straight away.
  const addProduct = page.getByTestId("wizard-add-product");
  const nameField = page.getByLabel("Name", { exact: true });
  await expect(addProduct.or(nameField)).toBeVisible();
  if (await addProduct.isVisible()) await addProduct.click();
  await nameField.fill(name);
  await page.getByLabel("Category").fill("Tai nghe");
  await page.getByLabel("Brand").fill("AirBeat");
  await page.getByLabel("Description").fill("Tai nghe không dây cho người hay di chuyển.");
  await page.getByLabel("Target audience").fill("Người đi làm bằng xe buýt");
  await page.getByLabel("Original URL").fill("https://example.com/airbeat");
  await page.getByRole("button", { name: "Create Product" }).click();

  // Upload a photo. Next waits for it.
  await expect(page).toHaveURL(/step=photos/);
  await expect(page.getByTestId("wizard-next")).toBeDisabled();
  await page.getByLabel("Add photos").setInputFiles({ name: "airbeat.png", mimeType: "image/png", buffer: productPhoto() });
  await expect(page.getByTestId("product-asset")).toHaveCount(1);
  await page.getByTestId("wizard-next").click();

  // Add Facts and Confirm them. Next waits for a Confirmed one.
  await expect(page).toHaveURL(/step=facts/);
  await expect(page.getByTestId("wizard-next")).toBeDisabled();
  for (const fact of FACTS) {
    await page.getByLabel("Fact", { exact: true }).fill(fact);
    await page.getByRole("button", { name: "Add Fact" }).click();
    await page.getByTestId("facts-proposed").getByRole("button", { name: "Confirm", exact: true }).click();
    await expect(page.getByTestId("facts-confirmed")).toContainText(fact);
  }
  await page.getByTestId("wizard-next").click();

  // Choose the creative template, the Hook and the duration.
  await expect(page).toHaveURL(/step=direction/);
  await page.getByLabel("Creative template").selectOption("ProblemSolution");
  await expect(page.getByTestId("wizard-template-about")).toContainText("problem");
  await page.getByLabel("Creative template").selectOption("ProductShowcase");
  await page.getByLabel("Hook").fill(HOOK);
  await page.getByLabel("Duration (seconds)").fill("15");
  await expect(page.getByLabel("Audience")).toHaveValue("Người đi làm bằng xe buýt");
  await page.getByLabel("Objective").fill("Bấm vào liên kết để mua");
  await page.getByTestId("wizard-next").click();

  // Generate the Storyboard: Product Showcase has four Scenes, written by the mock planner from the Confirmed Facts.
  await expect(page).toHaveURL(/step=storyboard/);
  await expect(page.getByTestId("wizard-next")).toBeDisabled();
  await page.getByRole("button", { name: "Generate Storyboard" }).click();
  await expect(page.getByTestId("scene")).toHaveCount(4);
  await expect(page.getByTestId("storyboard-planner")).toContainText("mock planner");
  await expect(page.getByTestId("storyboard")).toContainText(HOOK);
  for (const fact of FACTS) await expect(page.getByTestId("storyboard")).toContainText(fact);
  await page.getByTestId("wizard-next").click();

  // Add narration. It is the member's own file, and is only taken once they confirm they hold the rights to it.
  await expect(page).toHaveURL(/step=sound/);
  const narration = page.getByTestId("variant-audio-narration");
  await narration.getByRole("checkbox").check();
  await narration.getByLabel("Upload narration").setInputFiles(NARRATION);
  await expect(narration.getByTestId("variant-audio-details")).toBeVisible();
  await page.getByTestId("wizard-next").click();
  await expect(page).toHaveURL(/step=video/);

  // Leaving the wizard and returning through the dashboard resumes at the same step.
  const videoStep = page.url();
  await page.goto("/");
  await page.getByTestId("quick-create").click();
  await expect(page).toHaveURL(videoStep);

  // Render. While the worker renders, the dashboard shows the job in progress.
  await page.getByRole("button", { name: "Render video" }).click();
  await expect(page.getByTestId("render-stage")).toBeVisible();
  const dashboard = await context.newPage();
  await dashboard.goto("/");
  await expect(dashboard.getByTestId("dashboard-job").filter({ hasText: name })).toBeVisible();
  await expect(dashboard.getByTestId("dashboard-project").filter({ hasText: name })).toBeVisible();
  await dashboard.close();

  const stage = page.getByTestId("render-stage");
  await expect(stage).toHaveText(/^(Rendered|Failed|Cancelled)$/, { timeout: LONGEST_RENDER_MS });
  await expect(stage, await page.getByTestId("render").innerText()).toHaveText("Rendered");

  // Preview: the page plays the file the API serves, which is an MP4. The browser this
  // test runs in may not decode H.264, so the file is read as the player would ask for it.
  const player = page.getByTestId("rendered-video");
  await expect(player).toBeVisible();
  const preview = await page.request.get((await player.getAttribute("src"))!);
  expect(preview.status()).toBe(200);
  expect(preview.headers()["content-type"]).toBe("video/mp4");
  expect(isMp4(await preview.body())).toBe(true);
  // The narration added in the wizard is in this video.
  await expect(page.getByTestId("rendered-video-audio")).toHaveText("narration");

  // Approve, which is what allows the download.
  await expect(page.getByTestId("rendered-video-state")).toHaveText("Ready for review");
  await expect(page.getByTestId("rendered-video-download")).toHaveCount(0);
  await page.getByTestId("rendered-video-approve").click();
  await expect(page.getByTestId("rendered-video-state")).toContainText(`Approved by ${EMAIL}`);

  // Download.
  const downloading = page.waitForEvent("download");
  await page.getByTestId("rendered-video-download").click();
  const download = await downloading;
  expect(download.suggestedFilename()).toMatch(/\.mp4$/);
  const file = await readFile(await download.path());
  expect(file.length).toBeGreaterThan(10_000);
  expect(isMp4(file)).toBe(true);

  // The dashboard shows the Rendered Video, approved, and no job of it still in progress.
  await page.goto("/");
  await expect(page.getByTestId("dashboard-video").filter({ hasText: name })).toContainText("Approved");
  await expect(page.getByTestId("dashboard-job").filter({ hasText: name })).toHaveCount(0);
});

// An MP4 starts with a box of the type "ftyp".
const isMp4 = (file: Buffer) => file.subarray(4, 8).toString("latin1") === "ftyp";

/**
 * A photo to upload: 800 by 1000, a dark bottle with a lighter cap standing on a pale
 * backdrop. A PNG written out by hand, so the test needs no image file beside it.
 */
function productPhoto(): Buffer {
  const width = 800;
  const height = 1000;
  // Each row is a filter byte, then three bytes for each pixel.
  const rows = Buffer.alloc(height * (1 + width * 3));
  for (let y = 0; y < height; y++) {
    const row = y * (1 + width * 3);
    for (let x = 0; x < width; x++) {
      const cap = y >= 180 && y < 280 && x >= 330 && x < 470;
      const body = y >= 280 && y < 860 && x >= 260 && x < 540;
      const [red, green, blue] = cap ? [200, 160, 60] : body ? [30, 60, 110] : [238, 236, 232];
      rows.set([red, green, blue], row + 1 + x * 3);
    }
  }
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header.set([8, 2, 0, 0, 0], 8); // 8 bits a channel, RGB
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", header),
    chunk("IDAT", deflateSync(rows)),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

function chunk(type: string, data: Buffer): Buffer {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const typed = Buffer.concat([Buffer.from(type, "latin1"), data]);
  const check = Buffer.alloc(4);
  check.writeUInt32BE(crc32(typed));
  return Buffer.concat([length, typed, check]);
}

function crc32(bytes: Buffer): number {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
  }
  return (crc ^ 0xffffffff) >>> 0;
}
