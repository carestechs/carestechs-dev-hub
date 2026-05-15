# Implementation Plan: T-010 — Angular workspace with Tailwind 4 and modern-minimal tokens

## Task Reference
- **Task ID:** T-010
- **Type:** Frontend
- **Workflow:** standard
- **Complexity:** M
- **Rationale:** Foundational frontend; every later UI task lands here. Locks in the design system before any screen work begins.

## Overview
Create the Angular 20+ workspace under `client/`, install and configure Tailwind 4 with the modern-minimal token palette, wire Google Fonts (Poppins + Inter), and add the dev `/api` proxy.

## Implementation Steps

### Step 1: Bootstrap Angular workspace
**Action:** Generate
```
cd <repo root>
npx -p @angular/cli ng new portfolio \
  --directory client \
  --standalone --routing --style=css --skip-tests=false --strict --package-manager=npm
```
Standalone components + routing + strict TS + CSS (Tailwind generates it).

### Step 2: Install Tailwind 4
**File:** `client/package.json`
**Action:** Modify
`cd client && npm i -D tailwindcss @tailwindcss/postcss postcss autoprefixer`.

### Step 3: PostCSS config
**File:** `client/postcss.config.js`
**Action:** Create
```js
module.exports = { plugins: { '@tailwindcss/postcss': {}, autoprefixer: {} } };
```

### Step 4: Tailwind config (CSS-first)
**File:** `client/src/styles.css`
**Action:** Modify
```css
@import "tailwindcss";

@theme {
  --color-primary: #0EA5E9;          /* sky-500 */
  --color-primary-light: #E0F2FE;
  --color-primary-dark: #0284C7;
  --color-secondary: #8B5CF6;
  --color-success: #10B981;
  --color-warning: #F59E0B;
  --color-error: #EF4444;
  --color-info: #0EA5E9;

  --font-heading: "Poppins", system-ui, sans-serif;
  --font-body: "Inter", system-ui, sans-serif;

  --radius-md: 0.5rem;
  --radius-lg: 0.625rem;
  --radius-xl: 1rem;
}

html { font-family: var(--font-body); color: theme(colors.slate.700); background: theme(colors.slate.50); }
h1,h2,h3 { font-family: var(--font-heading); color: theme(colors.slate.900); }
```

### Step 5: Google Fonts
**File:** `client/src/index.html`
**Action:** Modify
In `<head>`:
```html
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=Poppins:wght@600;700&display=swap" rel="stylesheet">
```

### Step 6: Dev proxy
**File:** `client/proxy.conf.json`
**Action:** Create
```json
{
  "/api": { "target": "http://localhost:5000", "secure": false, "changeOrigin": true, "logLevel": "debug" }
}
```

### Step 7: Wire proxy into ng serve
**File:** `client/angular.json`
**Action:** Modify
Under `projects.portfolio.architect.serve.options`, add `"proxyConfig": "proxy.conf.json"`.

### Step 8: Smoke component
**File:** `client/src/app/app.component.html`
**Action:** Modify
Replace generated template with:
```html
<main class="min-h-screen flex items-center justify-center p-8">
  <div class="bg-white rounded-xl shadow-sm p-6 max-w-md text-center">
    <h1 class="text-3xl font-heading mb-2">Portfolio</h1>
    <p class="text-slate-500">Bootstrapping…</p>
  </div>
</main>
```

### Step 9: Verify
**Action:** Verify
- `npm install && ng serve` → no errors, opens on `http://localhost:4200`.
- Smoke component renders Poppins for `<h1>` and Inter for body (devtools: Computed → font-family).
- `ng build` produces `dist/` cleanly.
- `curl http://localhost:4200/api/health` proxies to the API (404 if API isn't running, but the proxy log shows the attempt).

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `client/` | Create | Entire Angular workspace |
| `client/postcss.config.js` | Create | PostCSS + Tailwind 4 plugin |
| `client/src/styles.css` | Modify | Tailwind import + `@theme` tokens + base typography |
| `client/src/index.html` | Modify | Google Fonts preconnect + link |
| `client/proxy.conf.json` | Create | Dev `/api` proxy |
| `client/angular.json` | Modify | `proxyConfig` in serve options |
| `client/src/app/app.component.html` | Modify | Smoke content |

## Edge Cases & Risks
- **Tailwind 4 + Angular 20 esbuild builder** — Tailwind 4's PostCSS plugin works with the Angular CLI esbuild builder; if hot reload misses `@theme` changes, fall back to manually restarting `ng serve` or pin to Tailwind 3.4 and use the classic `tailwind.config.js`. Document the chosen path.
- **Font loading on offline dev** — Google Fonts CDN is the simplest path; if offline-friendliness becomes important, switch to self-hosted via `@fontsource/inter` and `@fontsource/poppins`.
- **CSS variable name collisions** — Tailwind 4's `@theme` keys map to `--color-primary` etc.; ensure no conflicts with Angular Material if it ever lands here (it won't per ADR).

## Acceptance Verification
- [ ] `cd client && npm install && ng serve` succeeds; Smoke component visible at `http://localhost:4200`.
- [ ] `ng build` succeeds and outputs to `client/dist/`.
- [ ] Inspecting the smoke `<h1>` shows `font-family: Poppins`; body shows `font-family: Inter`.
- [ ] `curl http://localhost:4200/api/health` is proxied to `localhost:5000`.
