# Provider test

A throwaway prototype for ticket 01 of `.scratch/hybrid-video/`. It makes Stills, Clips and Vietnamese speech with real providers so the founder can judge them by eye and ear before Hybrid is built. It is not part of the application and will be deleted once the verdict is in.

It spends real money. Nothing paid starts without `--yes`, and every command prints an estimate first. A full run with the example file is about $2.50.

## Set up

1. Add the provider keys listed at the bottom of the repository's `.env.example` to the `.env` at the repository root. `FAL_KEY` is needed for Stills and Clips; each speech provider is optional.
2. Copy `input/test.example.json` to `input/test.json` and rewrite the descriptions and narration for a real Product. Keep the scene names `backdrop` and `footage`. Neither description may include the Product: in Hybrid the Product is always its photo.

Node 20.6 or later is needed, and nothing is installed. Run the commands from this directory.

## Run

```
node --env-file=../../.env test.mjs estimate
node --env-file=../../.env test.mjs stills --yes
```

Look at `output/stills` and delete the ones you reject. Only the Stills left there become Clips:

```
node --env-file=../../.env test.mjs clips --yes
node --env-file=../../.env test.mjs voices --yes
```

To see the real Product over each backdrop Clip, pass a cut-out PNG, such as one from `prototypes/day-one-render/output/look`. This uses FFmpeg in the worker's image and costs nothing:

```
node test.mjs compose ..\day-one-render\output\look\iphone-17-promax-1-cutout.png
```

## What to judge

- `output/stills`: would you accept this picture before paying for motion?
- `output/clips`: each kept Still as a 5-second Clip from three video models, from cheapest to dearest. Does anything warp, appear or flicker? Is the dearest visibly better?
- `output/composed`: does the real Product sit convincingly on a generated backdrop?
- `output/voices`: the narration in each voice. Are the tones and the pronunciation right?

## What it tries

| Kind | Model | List price on 2026-10-12 |
|---|---|---|
| Still | Nano Banana 2 | $0.08 an image |
| Clip | MiniMax H3 Max Turbo, 768p | $0.025 a second |
| Clip | Kling 2.5 Turbo Pro | $0.07 a second |
| Clip | Kling 3 Pro, without audio | $0.14 a second |
| Speech | ElevenLabs Flash v2.5 and v3 | a few cents |
| Speech | Google Chirp 3 HD, `vi-VN` | a few cents |

Stills and Clips go through fal.ai. The model list is at the top of `test.mjs`.
