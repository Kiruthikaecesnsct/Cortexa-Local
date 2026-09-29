# Production image: static build served by nginx with an SPA fallback and an
# /api reverse proxy to api-gateway. Build context is this directory
# (frontend/), consistent with every other service's Dockerfile in this repo.
FROM node:24-slim AS build
RUN corepack enable
WORKDIR /app
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY . .
# VITE_APP_VERSION defaults to the current git SHA inside vite.config.ts when
# unset; CI can override it. No API base URL build arg - the app always talks
# same-origin to /api, proxied server-side (nginx here, Vite dev server in
# preview mode) to avoid the gateway's origin-locked CORS policy.
ARG VITE_APP_VERSION
ENV VITE_APP_VERSION=${VITE_APP_VERSION}
RUN pnpm run build

FROM nginx:1.30-alpine AS final
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
