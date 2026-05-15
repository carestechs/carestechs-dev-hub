# syntax=docker/dockerfile:1.7
ARG NODE_IMAGE=node:20-alpine
ARG NGINX_IMAGE=nginx:1.27-alpine

# -----------------------------------------------------------------------------
# Build stage — install deps with the lockfile, then ng build production.
# -----------------------------------------------------------------------------
FROM ${NODE_IMAGE} AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build -- --configuration production

# -----------------------------------------------------------------------------
# Runtime stage — nginx serves the SPA and reverse-proxies /api/ to the API.
# -----------------------------------------------------------------------------
FROM ${NGINX_IMAGE} AS runtime
RUN rm -rf /usr/share/nginx/html/*
COPY --from=build /app/dist/dev-hub/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
HEALTHCHECK --interval=10s --timeout=3s --retries=3 \
  CMD wget -qO- http://127.0.0.1/ > /dev/null 2>&1 || exit 1
