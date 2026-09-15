# syntax=docker/dockerfile:1
#
# Build context MUST be the repository root, e.g.:
#   docker build -f src/Sense.Api/Dockerfile -t senseik-api .
# (Sense.Api references sibling projects under src/Modules and src/Shared, and relies on
# the repo-root Directory.Build.props / Directory.Packages.props for central package
# management, so the whole src/ tree - not just this folder - needs to be in the build
# context.)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Repo-root MSBuild config first: these rarely change, so they cache well independently
# of application source.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/ src/

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/Sense.Api/Sense.Api.csproj

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/Sense.Api/Sense.Api.csproj \
      --configuration Release \
      --no-restore \
      --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The base runtime image ships without curl; needed for the HEALTHCHECK below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

# The base image already defines an unprivileged "app" user (see
# https://github.com/dotnet/dotnet-docker/blob/main/documentation/README.security.md).
USER app

HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Sense.Api.dll"]
