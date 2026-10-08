# Throwaway image for the day-one render test (ticket 01) and the look work
# (ticket 25). Not part of the application.
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

WORKDIR /work
ENTRYPOINT ["node", "/work/render.mjs"]
