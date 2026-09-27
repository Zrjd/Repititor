# Web UI

React SPA for the Repetitor API: auth, dashboard, catalog, grammar, dictionary, my words and SRS practice.

## Requirements

- Node.js `20.19+` or `22.12+`
- API running (default `http://localhost:8080`)

## Local development

```bash
npm install
npm run dev
```

The app is served on `http://localhost:5173`. Vite proxies `/api` to the API, so no CORS setup is needed in development.

Point the dev proxy at another API instance with `VITE_API_PROXY`:

```bash
VITE_API_PROXY=http://192.168.1.10:8080 npm run dev
```

## Scripts

| Script | Description |
| --- | --- |
| `npm run dev` | Vite dev server with HMR |
| `npm run typecheck` | `tsc -b` type check |
| `npm run lint` | oxlint |
| `npm run build` | Type check and production build into `dist/` |
| `npm run preview` | Serve the production build locally |

## Configuration

| Variable | Default | Description |
| --- | --- | --- |
| `VITE_API_PROXY` | `http://localhost:8080` | API target for the dev server proxy |
| `VITE_API_BASE_URL` | empty | API origin for production builds; requests go to `${VITE_API_BASE_URL}/api/v1` |

For a production build served from the same origin as the API (the Docker image does this through nginx), leave `VITE_API_BASE_URL` empty.

## Docker

```bash
docker build -t repetitor/web:local -f web/Dockerfile web
docker run --rm -p 8081:80 repetitor/web:local
```

Or through the root compose file, which also starts the API and proxies `/api` to it:

```bash
docker compose up -d --build web
```

The UI is then available on `http://localhost:8081`.

## Structure

```
src/
  api/         typed client, endpoint modules, response types
  auth/        session context (login, register, refresh, logout)
  components/  layout, feedback states, page header, language toggle
  i18n/        ru/en dictionaries, plural forms, provider
  pages/       route components
```

Authentication uses JWT access and refresh tokens in `localStorage`; the client refreshes the access token once per failed request and clears the session when the refresh token is rejected.
