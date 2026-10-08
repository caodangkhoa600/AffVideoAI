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
