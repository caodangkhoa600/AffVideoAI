# Build context: the repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/AffiVideo.Application/AffiVideo.Application.csproj src/AffiVideo.Application/
COPY src/AffiVideo.Contracts/AffiVideo.Contracts.csproj src/AffiVideo.Contracts/
COPY src/AffiVideo.Infrastructure/AffiVideo.Infrastructure.csproj src/AffiVideo.Infrastructure/
COPY src/AffiVideo.Api/AffiVideo.Api.csproj src/AffiVideo.Api/
RUN dotnet restore src/AffiVideo.Api
COPY src/ src/
# The OpenAPI description is written by a developer's build and committed, not by the image build.
RUN dotnet publish src/AffiVideo.Api -c Release -o /app --no-restore -p:OpenApiGenerateDocuments=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# curl is only here for the container's health check.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "AffiVideo.Api.dll"]
