// Provider test for the Hybrid video spec (ticket 01). A throwaway prototype:
// it makes Stills, Clips and Vietnamese speech with real providers so the
// founder can judge them by eye and ear. It is not part of the application.
//
//   node --env-file=../../.env test.mjs estimate
//   node --env-file=../../.env test.mjs stills --yes
//   node --env-file=../../.env test.mjs clips --yes
//   node --env-file=../../.env test.mjs voices --yes
//   node test.mjs compose <cut-out.png>
//
// Nothing paid starts without --yes. A scene name or a model name after the
// command limits it: "stills --yes backdrop", "clips --yes backdrop kling-3-pro".

import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { basename, dirname, extname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, "output");

// Prices read on fal.ai/pricing on 2026-10-12. They are only used for the estimate.
const imageModels = [
  { name: "nano-banana-2", endpoint: "fal-ai/nano-banana-2", perImage: 0.08, input: (prompt) => ({ prompt, aspect_ratio: "9:16", resolution: "1K", output_format: "png" }) },
];

const videoModels = [
  {
    name: "h3-max-turbo",
    endpoint: "minimax/h3-max-turbo/image-to-video",
    perSecond: 0.025,
    input: (prompt, image) => ({ prompt, image_url: image, duration: 5, resolution: "768P", prompt_expansion_mode: "disabled" }),
  },
  {
    name: "kling-2.5-turbo-pro",
    endpoint: "fal-ai/kling-video/v2.5-turbo/pro/image-to-video",
    perSecond: 0.07,
    input: (prompt, image) => ({ prompt, image_url: image, duration: "5" }),
  },
  {
    name: "kling-3-pro",
    endpoint: "fal-ai/kling-video/v3/pro/image-to-video",
    perSecond: 0.14,
    input: (prompt, image) => ({ prompt, start_image_url: image, duration: "5", generate_audio: false }),
  },
];
const clipSeconds = 5;

const elevenModels = ["eleven_flash_v2_5", "eleven_v3"];
const googleVoices = ["vi-VN-Chirp3-HD-Kore", "vi-VN-Chirp3-HD-Puck"];

// Scene and model names given after the command. With none of a kind, all of that kind are used.
const named = process.argv.slice(3).filter((argument) => !argument.startsWith("--"));
const chosen = (items) => {
  const picked = items.filter((item) => named.includes(item.name));
  return picked.length > 0 ? picked : items;
};

function loadTest() {
  const path = join(here, "input", "test.json");
  const example = join(here, "input", "test.example.json");
  if (!existsSync(path)) {
    console.log(`No input/test.json, so the example is used. Copy ${basename(example)} to test.json to describe your own Product.`);
  }
  const test = JSON.parse(readFileSync(existsSync(path) ? path : example, "utf8"));
  const unknown = named.filter((name) => ![...test.scenes, ...imageModels, ...videoModels].some((item) => item.name === name));
  if (unknown.length > 0) {
    throw new Error(`Neither a scene nor a model: ${unknown.join(", ")}.`);
  }
  return { ...test, scenes: chosen(test.scenes) };
}

function need(name) {
  const value = process.env[name];
  if (!value) {
    throw new Error(`${name} is not set. Put it in the .env at the repository root and run with --env-file=../../.env.`);
  }
  return value;
}

const sleep = (ms) => new Promise((done) => setTimeout(done, ms));

async function json(response) {
  const text = await response.text();
  if (!response.ok) {
    throw new Error(`${response.status} ${response.statusText}: ${text.slice(0, 500)}`);
  }
  return JSON.parse(text);
}

// fal's queue: submit, poll the status it names, then read the result it names.
async function fal(endpoint, input) {
  const headers = { Authorization: `Key ${need("FAL_KEY")}`, "Content-Type": "application/json" };
  const submitted = await json(await fetch(`https://queue.fal.run/${endpoint}`, { method: "POST", headers, body: JSON.stringify(input) }));
  for (;;) {
    await sleep(5000);
    const status = await json(await fetch(submitted.status_url, { headers }));
    if (status.status === "COMPLETED") {
      if (status.error) {
        throw new Error(`${status.error_type ?? "failed"}: ${JSON.stringify(status.error).slice(0, 500)}`);
      }
      return json(await fetch(submitted.response_url, { headers }));
    }
  }
}

async function download(url, path) {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`Could not download ${url}: ${response.status}`);
  }
  writeFileSync(path, Buffer.from(await response.arrayBuffer()));
}

// Runs every job, reports each failure, and carries on: one refused prompt
// must not lose the rest of a paid run.
async function runAll(jobs) {
  const results = await Promise.allSettled(jobs.map((job) => job.run()));
  results.forEach((result, index) => {
    console.log(result.status === "fulfilled" ? `  made    ${jobs[index].label}` : `  FAILED  ${jobs[index].label}: ${result.reason.message}`);
  });
  if (results.some((result) => result.status === "rejected")) {
    process.exitCode = 1;
  }
}

function stillFiles(test) {
  const directory = join(out, "stills");
  const files = existsSync(directory) ? readdirSync(directory).filter((file) => extname(file) === ".png").sort() : [];
  return files.filter((file) => test.scenes.some((scene) => scene.name === file.split("--")[0]));
}

function estimate(test) {
  const video = chosen(videoModels);
  const stills = test.scenes.length * imageModels.reduce((sum, model) => sum + model.perImage, 0);
  const kept = stillFiles(test).length || test.scenes.length * imageModels.length;
  const clips = kept * video.reduce((sum, model) => sum + model.perSecond * clipSeconds, 0);
  console.log(`stills  ${test.scenes.length} description(s) x ${imageModels.length} image model(s): about $${stills.toFixed(2)}`);
  console.log(`clips   ${kept} Still(s) x ${video.length} video model(s) x ${clipSeconds} s: about $${clips.toFixed(2)}`);
  console.log(`voices  ${test.narration.length} line(s) x up to ${elevenModels.length * 2 + googleVoices.length} voices: a few cents`);
  console.log("These are estimates from list prices. Delete any Still you reject from output/stills before running clips.");
}

async function stills(test) {
  mkdirSync(join(out, "stills"), { recursive: true });
  await runAll(
    test.scenes.flatMap((scene) =>
      imageModels.map((model) => ({
        label: `${scene.name} with ${model.name}`,
        run: async () => {
          const result = await fal(model.endpoint, model.input(scene.still));
          await download(result.images[0].url, join(out, "stills", `${scene.name}--${model.name}.png`));
        },
      })),
    ),
  );
  console.log("Look at output/stills, delete the ones you reject, then run clips.");
}

async function clips(test) {
  mkdirSync(join(out, "clips"), { recursive: true });
  const files = stillFiles(test);
  if (files.length === 0) {
    throw new Error("No Stills to use in output/stills. Run stills first.");
  }
  await runAll(
    files.flatMap((file) => {
      const sceneName = file.split("--")[0];
      const scene = test.scenes.find((candidate) => candidate.name === sceneName);
      if (!scene) {
        throw new Error(`${file} does not belong to a scene in the test file.`);
      }
      const image = `data:image/png;base64,${readFileSync(join(out, "stills", file)).toString("base64")}`;
      return chosen(videoModels).map((model) => ({
        label: `${basename(file, ".png")} with ${model.name}`,
        run: async () => {
          const result = await fal(model.endpoint, model.input(scene.motion, image));
          await download(result.video.url, join(out, "clips", `${basename(file, ".png")}--${model.name}.mp4`));
        },
      }));
    }),
  );
  console.log("Watch output/clips on a phone.");
}

async function elevenVoices(key) {
  if (process.env.ELEVENLABS_VOICE_IDS) {
    return process.env.ELEVENLABS_VOICE_IDS.split(",").map((id) => ({ voice_id: id.trim(), name: id.trim() }));
  }
  const listed = await json(await fetch("https://api.elevenlabs.io/v1/voices", { headers: { "xi-api-key": key } }));
  return listed.voices.slice(0, 2);
}

async function voices(test) {
  mkdirSync(join(out, "voices"), { recursive: true });
  const jobs = [];
  const text = test.narration.join(" ");

  if (process.env.ELEVENLABS_API_KEY) {
    const key = process.env.ELEVENLABS_API_KEY;
    for (const voice of await elevenVoices(key)) {
      for (const model of elevenModels) {
        jobs.push({
          label: `ElevenLabs ${model}, voice ${voice.name}`,
          run: async () => {
            const response = await fetch(`https://api.elevenlabs.io/v1/text-to-speech/${voice.voice_id}?output_format=mp3_44100_128`, {
              method: "POST",
              headers: { "xi-api-key": key, "Content-Type": "application/json" },
              body: JSON.stringify({ text, model_id: model, language_code: "vi" }),
            });
            if (!response.ok) {
              throw new Error(`${response.status}: ${(await response.text()).slice(0, 300)}`);
            }
            const name = voice.name.replace(/[^\p{L}\p{N}]+/gu, "-");
            writeFileSync(join(out, "voices", `elevenlabs--${model}--${name}.mp3`), Buffer.from(await response.arrayBuffer()));
          },
        });
      }
    }
  } else {
    console.log("ELEVENLABS_API_KEY is not set, so ElevenLabs is skipped.");
  }

  if (process.env.GOOGLE_TTS_API_KEY) {
    for (const voice of googleVoices) {
      jobs.push({
        label: `Google ${voice}`,
        run: async () => {
          const spoken = await json(
            await fetch(`https://texttospeech.googleapis.com/v1/text:synthesize?key=${process.env.GOOGLE_TTS_API_KEY}`, {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ input: { text }, voice: { languageCode: "vi-VN", name: voice }, audioConfig: { audioEncoding: "MP3" } }),
            }),
          );
          writeFileSync(join(out, "voices", `google--${voice}.mp3`), Buffer.from(spoken.audioContent, "base64"));
        },
      });
    }
  } else {
    console.log("GOOGLE_TTS_API_KEY is not set, so Google is skipped.");
  }

  if (jobs.length === 0) {
    throw new Error("No speech provider has a key.");
  }
  await runAll(jobs);
  console.log("Listen to output/voices.");
}

// Puts a cut-out of the Product over each backdrop Clip, to judge the two
// together. FFmpeg runs in the worker's image; the host has none.
function compose(cutOut) {
  if (!cutOut || !existsSync(cutOut)) {
    throw new Error("Give the path of a cut-out PNG, for example one from prototypes/day-one-render/output/look.");
  }
  const directory = join(out, "clips");
  const files = existsSync(directory) ? readdirSync(directory).filter((file) => file.startsWith("backdrop") && file.endsWith(".mp4")) : [];
  if (files.length === 0) {
    throw new Error("No backdrop Clips in output/clips. Run clips first.");
  }
  mkdirSync(join(out, "composed"), { recursive: true });
  const cutOutDirectory = dirname(resolve(cutOut));
  for (const file of files) {
    execFileSync(
      "docker",
      [
        "run", "--rm", "--network", "none", "--entrypoint", "ffmpeg",
        "-v", `${out}:/out`,
        "-v", `${cutOutDirectory}:/cut:ro`,
        process.env.FFMPEG_IMAGE ?? "affivideo-worker:latest",
        "-v", "error", "-y",
        "-i", `/out/clips/${file}`,
        "-i", `/cut/${basename(cutOut)}`,
        "-filter_complex",
        "[0:v]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920[bg];[1:v]scale=700:-1[p];[bg][p]overlay=(W-w)/2:(H-h)*0.62",
        "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p",
        `/out/composed/${file}`,
      ],
      { stdio: "inherit" },
    );
    console.log(`  made    composed/${file}`);
  }
}

const [command, ...rest] = process.argv.slice(2);
const paid = { stills, clips, voices };

try {
  if (command === "compose") {
    compose(rest[0]);
  } else if (command === "estimate" || command === undefined) {
    estimate(loadTest());
  } else if (paid[command]) {
    const test = loadTest();
    if (!rest.includes("--yes")) {
      estimate(test);
      console.log(`Nothing was generated. Run "${command} --yes" to spend it.`);
    } else {
      await paid[command](test);
    }
  } else {
    throw new Error(`Unknown command "${command}". Use estimate, stills, clips, voices or compose.`);
  }
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
