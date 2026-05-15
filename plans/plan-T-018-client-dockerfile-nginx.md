# Implementation Plan: T-018 — Client Dockerfile (node → nginx) and nginx.conf

## Task Reference
- **Task ID:** T-018
- **Type:** DevOps
- **Workflow:** standard
- **Complexity:** S
- **Rationale:** Profile rule: nginx-spa-proxy + container-per-process. DevHub is the single origin in production.

## Overview
Two-stage Dockerfile inside `client/`: Node 20 builds the SPA, nginx 1.27 serves the build output with `try_files` SPA fallback and a `/api/` reverse proxy to the API container. `proxy_buffering off` to keep SSE streams pass-through.

## Implementation Steps

### Step 1: Dockerfile
**File:** `client/Dockerfile`
**Action:** Create
```dockerfile
# syntax=docker/dockerfile:1.7
FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build -- --configuration production

FROM nginx:1.27-alpine AS runtime
RUN rm -rf /usr/share/nginx/html/*
COPY --from=build /app/dist/dev-hub/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
HEALTHCHECK --interval=10s --timeout=3s --retries=3 \
  CMD wget -qO- http://127.0.0.1/ > /dev/null || exit 1
```

### Step 2: nginx.conf
**File:** `client/nginx.conf`
**Action:** Create
```nginx
server {
  listen 80;
  server_name _;
  root /usr/share/nginx/html;
  index index.html;

  # SPA: send everything that isn't a real file to index.html
  location / {
    try_files $uri $uri/ /index.html;
  }

  # Reverse-proxy /api/ to the API container.
  # IMPORTANT: proxy_buffering off — required for SSE streams (FEAT-005 / FEAT-004 stream endpoints).
  location /api/ {
    proxy_pass         http://api:8080/api/;
    proxy_http_version 1.1;
    proxy_set_header   Host              $host;
    proxy_set_header   X-Real-IP         $remote_addr;
    proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
    proxy_set_header   X-Forwarded-Proto $scheme;
    proxy_set_header   Connection        "";

    proxy_buffering         off;
    proxy_request_buffering off;
    proxy_cache             off;
    proxy_read_timeout      1h;   # long enough for streaming responses
    proxy_send_timeout      1h;
  }
}
```

### Step 3: Adjust Angular build output check
**Action:** Verify
Angular 20 outputs to `dist/<project-name>/browser/`. The Dockerfile copies from `dist/dev-hub/browser`. Confirm the project name is `DevHub` (set in T-010); if different, update the COPY path.

### Step 4: Smoke
**Action:** Verify
- `docker build -t devhub-web client/` succeeds.
- `docker network create test-net && docker run --rm --network test-net --name api devhub-api &`
- `docker run --rm --network test-net -p 8080:80 devhub-web`
- `curl http://localhost:8080/` returns the SPA index.
- `curl http://localhost:8080/projects/foo` also returns the SPA index (try_files fallback).
- `curl http://localhost:8080/api/health` reaches the API and returns its JSON.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `client/Dockerfile` | Create | Multi-stage SPA build |
| `client/nginx.conf` | Create | SPA + reverse-proxy config with streaming-safe defaults |

## Edge Cases & Risks
- **proxy_buffering off** — required for SSE in FEAT-004/005; without it, nginx would batch up the response and break real-time. Tested in those features but pinned in this config so the gap doesn't open later.
- **`upstream "api" not found`** — only works when the container is on a network where `api` resolves (the prod compose's `infra` network does). For local SPA-only testing, run with `--add-host api:host-gateway` or skip the `/api/` path.
- **Angular build output path drift** — if the Angular CLI changes the default output structure in a future minor release, the `COPY` path may break. CI catches this immediately.
- **HTTPS termination** — nginx in this image serves HTTP only. TLS termination is expected at the platform edge (load balancer / ingress) in production.

## Acceptance Verification
- [ ] `docker build -t devhub-web client/` succeeds.
- [ ] Image serves `/` with the SPA index and 200.
- [ ] Deep-link `/projects/foo` returns SPA (try_files fallback).
- [ ] `/api/health` is reverse-proxied to the API container on the same Docker network.
- [ ] `proxy_buffering off` is present in nginx.conf (grep verification).
