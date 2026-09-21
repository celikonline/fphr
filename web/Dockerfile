# syntax=docker/dockerfile:1
#
# Build context MUST be the repository root, e.g.:
#   docker build -f web/Dockerfile -t senseik-web .
# (web depends on the workspace package @senseik/api-client via pnpm, so the whole
# monorepo manifest - not just web/ - is needed to resolve and install it.)

FROM node:26-alpine AS build
WORKDIR /repo

# Node >= 25 images no longer bundle corepack; install it explicitly so the
# `packageManager` pin in package.json (pnpm@x.y.z) is still what gets used.
RUN npm install -g corepack@latest && corepack enable

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

# Vite inlines import.meta.env.* at build time, so the API URL has to be supplied here, not at
# container-run time. Without this the bundle falls back to web/.env.example's default
# (http://localhost:5000/api), which nothing listens on in this compose stack (the api service
# is published on API_PORT, 8080 by default) and every request from the browser fails outright.
ARG VITE_API_BASE_URL=http://localhost:8080/api
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL

# The login page's "Demo kullanıcıları" hint is hidden in production builds unless this is "true" (D-6).
ARG VITE_SHOW_DEMO_CREDENTIALS=false
ENV VITE_SHOW_DEMO_CREDENTIALS=$VITE_SHOW_DEMO_CREDENTIALS

RUN pnpm --filter web build

FROM nginx:alpine AS runtime

COPY web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /repo/web/dist /usr/share/nginx/html

EXPOSE 8080

# 127.0.0.1, not localhost: musl's resolver on this Alpine image prefers the ::1 entry in
# /etc/hosts, and nginx.conf only listens on IPv4 8080, so "localhost" here always fails
# the healthcheck even though the site serves fine.
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD wget -q -O- http://127.0.0.1:8080/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
