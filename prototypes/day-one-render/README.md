# Day-one render test

A throwaway prototype for ticket 01. It renders one Product into a 20-second 1080x1920 MP4 in Product Lock so the founder can judge whether the look is good enough to post. It is not part of the application and will be deleted once the verdict is in.

## Render your own Product

1. Put two or three photos of the Product (`.jpg`, `.png` or `.webp`) in `input/`. They are used in file-name order, one per Scene.
2. Copy `input/product.example.json` to `input/product.json` and fill in the name, Hook, three or four Facts and the call to action, in Vietnamese.
3. From the repository root:

   ```
   docker build -t affivideo-day-one prototypes/day-one-render
   docker run --rm -v "${PWD}\prototypes\day-one-render:/work" affivideo-day-one
   ```

The MP4 and one still frame per Scene are written to `output/`. Photos and output are ignored by git.

## Render the sample

```
docker run --rm -v "${PWD}\prototypes\day-one-render:/work" affivideo-day-one --sample
```

This uses the fictional AirBeat X1 with a generated placeholder photo. It proves the pipeline runs; it says nothing about how a real photo looks.

## Look test: cut-out and backgrounds (ticket 25)

`look.py` cuts the Product out of each photo in `input/` and places it, with a soft shadow, on three designed backgrounds: `studio`, `colour-light` (light in the Product's own colour) and `dark-premium`. It uses the same image; rebuild it first, which also fetches the cut-out model (about 220 MB).

```
docker build -t affivideo-day-one prototypes/day-one-render
docker run --rm --entrypoint /opt/look/bin/python -v "${PWD}\prototypes\day-one-render:/work" affivideo-day-one /work/look.py
```

For each photo, `output/look/` receives:

- `…-styles.png`: the three styles side by side. This is the one to look at.
- `…-compare.png`: the photo next to its cut-out, to check that nothing of the Product was removed or left behind.
- `…-studio.png`, `…-colour-light.png`, `…-dark-premium.png`: the 1080x1920 stills.
- `…-cutout.png`: the cut-out itself.

A photo that already has a transparent background goes through the same checks, and is used as it is when it passes and cut again by the model when it does not. A cut-out that fails the checks (parts of the Product missing, background left around the edge) is not used, and the photo goes uncut onto a card instead. If a cut-out passes the checks but looks wrong in `…-compare.png`, force the card with `--card 2.png`.

Other options: `--sample` (placeholder photo), `--ignore-alpha` (run the model even on a photo that is already cut out), `--model u2net` (another model; it must be in the image, see the Dockerfile, or the run needs network access to fetch it).
