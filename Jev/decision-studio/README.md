# Jev / Decision Studio

Three interactive demonstrations of **real Jev composition** through OpenRouter Decisions and json-render's experimental composer:

- PR review: security alert, reviewer team, changed files and checks.
- Incident room: error rate, latency, affected services and response tasks.
- Revenue room: orders, metrics and a weekly chart.

Each has an initial composition and a follow-up edit. The client renders the actual returned Specs through `@json-render/react`; it does not map model labels onto hardcoded full-page templates. Component recipes and synthetic data are app-owned. Jev chooses inclusion, placement and order.

## Honest static hosting

GitHub Pages cannot protect an API key or execute a server-side evaluation. This demo therefore **replays real recordings**, generated in a GitHub Actions job. It does not pretend to make live requests. Replay is slowed down for inspection; the displayed composition duration is the measured real run. No free-text input claims to call an API. Checklist interactions remain local and are reset on version/scenario changes.

The three datasets and all labels are synthetic/prepared. Jev does not invent their contents. Confidence, when reported, is not a calibrated guarantee of composition quality. Generation refuses incomplete or invalid results; failed requests are not replaced with fabricated model output.

## Run and build

Node 24+:

```sh
npm ci
npm test
npm run dev
```

For local rendering, download the `jev-studio-site` artifact from the **Jev Decision Studio** workflow and copy its `runs.json` to `public/runs.json`. Without it the client displays an explicit missing-recording state.

The native Decisions adapter is tested with the pinned @openrouter/sdk version. It validates every returned choice against the offered criteria and records provider-reported usage and cost.

The workflow reads the repository secret `JEV_OPENROUTER_API_KEY`, executes `npm run generate`, validates recordings, builds Vite and uploads `jev-studio-site`. The key exists only in the generation step's environment, not in browser code, logs, artifacts or source control. Generation is sequential and bounded: three creations plus three edits, at most 14 evaluations per composition and a 90-second deadline. Do not log request headers or environment variables. Provider pricing/availability can change.

To publish from a repository supporting Pages, enable **GitHub Actions** as its Pages source, then dispatch the workflow with `publish_pages=true`. The artifact root is just this demo's `dist`, never the repository root. Relative asset paths permit either project or user Pages URLs. A public artifact repository can host only the built site without exposing this private laboratory's other experiments.

## Reproducible experimental dependencies

The Jev composition API was unreleased on npm at implementation time. Vendored npm archives are built from upstream commit **3ad381881194e7011ad3ccd6d668033495a06c29** (core and React both 0.21.0). `package-lock.json` records archive integrity. No experimental upstream source was changed.

To rebuild archives from that revision:

```sh
git clone https://github.com/vercel-labs/json-render.git
cd json-render
git checkout 3ad381881194e7011ad3ccd6d668033495a06c29
pnpm install --frozen-lockfile --filter @json-render/core --filter @json-render/react...
pnpm --filter @json-render/core build
pnpm --filter @internal/react-state build
pnpm --filter @json-render/react build
pnpm --filter @json-render/core pack --pack-destination /absolute/path/to/vendor
pnpm --filter @json-render/react pack --pack-destination /absolute/path/to/vendor
```

Upstream packages are Apache-2.0 licensed; LICENSE files are included in the archives. Fonts: Google Fonts, DM Sans and Space Grotesk (system fallback if unavailable).

## Architecture

`catalog.mjs â†’ experimental_composeSpec â†’ OpenRouter alpha.decisions.create(typesafe/jev-1.13) â†’ validated snapshots â†’ runs.json â†’ React Renderer`

The server-only generator is not imported into the app. There is no browser credential entry, localStorage credential persistence, runtime code evaluation, external business action or real customer data. The catalog has no actions. If adapting to a live app, use an authenticated backend with input validation and quotas rather than putting the evaluator in Pages.

References: [json-render Jev](https://json-render.dev/docs/jev), [OpenRouter model](https://openrouter.ai/typesafe/jev-1.13), [upstream revision](https://github.com/vercel-labs/json-render/tree/3ad381881194e7011ad3ccd6d668033495a06c29).
