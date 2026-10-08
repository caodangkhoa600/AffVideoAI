# Throwaway image for the day-one render test (ticket 01) and the look work
# (tickets 25 and 26). Not part of the application.
FROM node:22-trixie-slim

RUN apt-get update \
 && apt-get install -y --no-install-recommends ffmpeg ca-certificates curl python3 python3-venv \
 && rm -rf /var/lib/apt/lists/*

# Be Vietnam Pro: SIL Open Font License, full Vietnamese diacritic coverage.
RUN mkdir -p /fonts \
 && for w in SemiBold Bold; do \
      curl -fsSL -o /fonts/BeVietnamPro-$w.ttf \
        https://raw.githubusercontent.com/google/fonts/main/ofl/bevietnampro/BeVietnamPro-$w.ttf; \
    done

# rembg (MIT) runs the cut-out model locally on the CPU.
RUN python3 -m venv /opt/look \
 && /opt/look/bin/pip install --no-cache-dir "rembg[cpu]==2.0.67"

# The model is fetched here so that a run needs no network. To compare others:
#   --build-arg CUTOUT_MODELS="birefnet-general-lite u2net isnet-general-use"
ENV U2NET_HOME=/models
ARG CUTOUT_MODELS="birefnet-general-lite"
RUN for m in $CUTOUT_MODELS; do \
      /opt/look/bin/python -c "import sys; from rembg import new_session; new_session(sys.argv[1])" $m; \
    done

# Ticket 26 builds the same video with FFmpeg filters and with Remotion.
# Everything below is what Remotion needs on top of FFmpeg: the libraries
# Chrome Headless Shell links against, the packages, and the browser itself.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      libnss3 libdbus-1-3 libatk1.0-0t64 libgbm1 libasound2t64 libxrandr2 libxkbcommon0 \
      libxfixes3 libxcomposite1 libxdamage1 libatk-bridge2.0-0t64 libpango-1.0-0 libcairo2 libcups2t64 \
 && rm -rf /var/lib/apt/lists/*
COPY remotion/package.json /opt/remotion/package.json
RUN cd /opt/remotion && npm install --no-audit --no-fund && npm cache clean --force
RUN cd /opt/remotion && npx remotion browser ensure

# The heaviest weight of the same typeface, for the Hook.
RUN curl -fsSL -o /fonts/BeVietnamPro-ExtraBold.ttf \
      https://raw.githubusercontent.com/google/fonts/main/ofl/bevietnampro/BeVietnamPro-ExtraBold.ttf

WORKDIR /work
ENTRYPOINT ["node", "/work/render.mjs"]
