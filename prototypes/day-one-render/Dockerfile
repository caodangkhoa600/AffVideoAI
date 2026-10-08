# Throwaway image for the day-one render test (ticket 01). Not part of the application.
FROM node:22-trixie-slim

RUN apt-get update \
 && apt-get install -y --no-install-recommends ffmpeg ca-certificates curl \
 && rm -rf /var/lib/apt/lists/*

# Be Vietnam Pro: SIL Open Font License, full Vietnamese diacritic coverage.
RUN mkdir -p /fonts \
 && for w in SemiBold Bold; do \
      curl -fsSL -o /fonts/BeVietnamPro-$w.ttf \
        https://raw.githubusercontent.com/google/fonts/main/ofl/bevietnampro/BeVietnamPro-$w.ttf; \
    done

WORKDIR /work
ENTRYPOINT ["node", "/work/render.mjs"]
