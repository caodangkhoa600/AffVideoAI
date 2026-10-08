# 25: Look: product cut-out and designed backgrounds

**What to build:** Still in the throwaway prototype, the Product is cut out of its photo and placed on a clean, designed background with a soft shadow, replacing the blurred copy of the photo that left dark blotches behind it. The founder sees the same Product on three background styles side by side and picks a direction. This is prototype work, judged by eye.

**Blocked by:** 01

**Status:** done

- [x] The Product is cut out of each photo by a tool that runs locally in the container, with no paid service
- [x] The cut-out only removes background: the Product's own pixels, colours, logo and edges are not repainted, and a side-by-side with the original photo shows this
- [x] A photo whose cut-out is poor (parts of the Product missing, halo around the edge) is detected or easy to spot, and falls back to the uncut photo on a card
- [x] Three background styles are produced without generative AI, for example a studio gradient, a soft coloured light taken from the Product's own colour, and a dark premium style
- [x] The Product sits on a soft shadow so it does not look pasted on
- [x] Stills of the founder's Product on each of the three styles are written to the output folder
- [x] The founder has picked one or two styles to carry into ticket 26
- [x] What was learned (tool, model size, time per photo, failure cases) is written into this ticket's comments

## Comments

- 2026-10-08: `prototypes/day-one-render/look.py` cuts the Product out and writes stills on three styles (`studio`, `colour-light`, `dark-premium`) to `output/look/`. Run on the founder's two iPhone photos, in the container with the network off. Status is ready-for-human: the founder looks at the two `…-styles.png` sheets and picks one or two styles for ticket 26.
- Learned:
  - Tool: rembg 2.0.67 (MIT) on onnxruntime, CPU only. Only its mask is used. The mask becomes the alpha of the photo's own pixels, so the Product is never repainted. The script compares every visible pixel of the cut-out with the photo file, which guards the cut-out step only; the stills scale the cut-out, and that is not checked.
  - Both of the founder's photos already had a transparent background. One was clean and is used as it is. The other had a blurred alpha (19% of the kept area half-transparent), which is what caused the glow around the Product in the day-one render. So a photo's own transparency is checked like any other cut-out, and when it fails the model cuts the photo again. The photo must not be flattened onto white with its bad alpha first: that blends the Product's edge with white, and a first version did exactly this and left a pale rim. The model is given the photo's colours as they are, with white only where the photo was fully transparent.
  - Models, scored against the clean photo's own alpha and timed on this machine's CPU (photos of 1080 and 1920 px square):

    | Model | File | Time per photo | Result |
    | --- | --- | --- | --- |
    | `birefnet-general-lite` (MIT) | 224 MB | 12–20 s | Best. IoU 0.995, 0.4% of the Product missing, no halo. Chosen. |
    | `u2net` (Apache 2.0) | 176 MB | 0.5–1 s | IoU 0.993, nothing missing, but a visible pale halo on a white background. |
    | `isnet-general-use` (Apache 2.0) | 179 MB | 1.5–2 s | Failed: removed most of the phone's screen (12% of the Product missing). |

    Loading the model adds 3–5 s per run. The time is per photo, once; the cut-out can be stored with the asset.
  - Failure cases and how they are caught: "parts of the Product missing" is caught by a stand-in, not directly: a large half-transparent share of the kept area (isnet 12–32%, good cut-outs under 2%; limit 8%). A part removed with a crisp edge would pass and has to be spotted by eye. A halo shows up as edge pixels still the colour of the background (u2net 22%, birefnet under 1%; limit 12%), but this can only be measured when the background is plain. On a busy background a halo is not detected and has to be spotted in the `…-compare.png` sheet, then forced to a card with `--card`. A screen showing a pale wallpaper is the hard case for these models: it looks like background.
  - Not tested: photos taken on a busy background, glass or transparent Products, and Products the same colour as their background. The founder's photos were both clean catalogue photos.
  - Cost to the image: it went from 0.97 GB to 2.44 GB on disk with the one model (224 MB); the rest is Python, rembg and its dependencies. The real worker could instead run the same ONNX file through the .NET ONNX runtime and skip Python.
  - Backgrounds are gradients and soft lights drawn with numpy, with a little noise added before rounding; without it the gradients band. They are drawn around where the Product stands, so the floor line and the light follow it.
  - The colour for `colour-light` is the most common saturated colour of the cut-out. Behind a light Product it is used dark, behind a dark one pale, or the Product disappears into it. A yellow-green used dark turns olive.
  - The shadow is two layers: a wide blurred copy of the cut-out's alpha shifted down, and a tight one under whatever touches the lowest rows. The tight one is what makes the Product stand on the floor; with two objects in one photo it follows each.
- 2026-10-08: The founder picked **`studio`** (the light seamless backdrop) to carry into ticket 26. `colour-light` and `dark-premium` are not taken forward.
