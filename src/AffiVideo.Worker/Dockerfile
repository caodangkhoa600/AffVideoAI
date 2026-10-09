# Build context: the repository root.
#
# The worker is the only place FFmpeg, ffprobe, Node, Remotion and its Chrome
# Headless Shell exist (ADR 0002), and the only place the cut-out model runs.
# Nothing on the host needs them.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/AffiVideo.Application/AffiVideo.Application.csproj src/AffiVideo.Application/
COPY src/AffiVideo.Domain/AffiVideo.Domain.csproj src/AffiVideo.Domain/
COPY src/AffiVideo.Infrastructure/AffiVideo.Infrastructure.csproj src/AffiVideo.Infrastructure/
COPY src/AffiVideo.Worker/AffiVideo.Worker.csproj src/AffiVideo.Worker/
RUN dotnet restore src/AffiVideo.Worker
COPY src/ src/
RUN dotnet publish src/AffiVideo.Worker -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS dotnet

FROM node:22-trixie-slim

# FFmpeg and ffprobe, the libraries Chrome Headless Shell links against, and
# libicu for the .NET runtime. curl only fetches the model below.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      ffmpeg ca-certificates curl libicu76 \
      libnss3 libdbus-1-3 libatk1.0-0t64 libgbm1 libasound2t64 libxrandr2 libxkbcommon0 \
      libxfixes3 libxcomposite1 libxdamage1 libatk-bridge2.0-0t64 libpango-1.0-0 libcairo2 libcups2t64 \
 && rm -rf /var/lib/apt/lists/*

COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet

# The model that cuts a Product out of its photo: BiRefNet general, lite (MIT
# licence), as published with rembg. It is fetched here, checked against the
# file ticket 25 measured, and run on the CPU, so a render needs no network.
RUN mkdir -p /app/models \
 && curl -fsSL -o /app/models/birefnet-general-lite.onnx \
      https://github.com/danielgatis/rembg/releases/download/v0.0.0/BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx \
 && echo "5600024376f572a557870a5eb0afb1e5961636bef4e1e22132025467d0f03333  /app/models/birefnet-general-lite.onnx" | sha256sum -c -

# Remotion at the version in the lock file, and the Chrome Headless Shell that
# version asks for. The typeface (Be Vietnam Pro, SIL Open Font License) comes
# in with remotion/public, so a render needs no network.
WORKDIR /app/remotion
RUN chown node:node /app/remotion
USER node
COPY --chown=node:node remotion/package.json remotion/package-lock.json ./
RUN npm ci --no-audit --no-fund && npm cache clean --force
RUN npx remotion browser ensure
COPY --chown=node:node remotion/ ./
# The creative templates, type-checked and bundled once. A render starts from a
# copy of this bundle, so no job waits for it to be built.
RUN npx tsc --noEmit && npx remotion bundle src/index.ts --out-dir bundle --log error

WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "AffiVideo.Worker.dll"]
