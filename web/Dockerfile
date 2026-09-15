# syntax=docker/dockerfile:1
#
# Build context MUST be the repository root, e.g.:
#   docker build -f web/Dockerfile -t senseik-web .
# (web depends on the workspace package @senseik/api-client via pnpm, so the whole
# monorepo manifest - not just web/ - is needed to resolve and install it.)

FROM node:20-alpine AS build
WORKDIR /repo

RUN corepack enable

# Copy only the manifests first so `pnpm install` is cached until a dependency actually
# changes. pnpm needs every workspace member's package.json to resolve the workspace
# graph, even when only installing the "web" subset below.
COPY package.json pnpm-workspace.yaml pnpm-lock.yaml ./
COPY web/package.json web/package.json
COPY packages/api-client/package.json packages/api-client/package.json
COPY apps/senseik-mobile/package.json apps/senseik-mobile/package.json

RUN --mount=type=cache,target=/root/.local/share/pnpm/store \
    pnpm install --frozen-lockfile --filter web...

COPY web/ web/
COPY packages/api-client/ packages/api-client/

RUN pnpm --filter web build

FROM nginx:alpine AS runtime

COPY web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /repo/web/dist /usr/share/nginx/html

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD wget -q -O- http://localhost:8080/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
