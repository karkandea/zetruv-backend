# Admin API handoff — DEV

Status: **DEV acceptance only**. STAGING remains untouched until DEV is accepted.

## Target topology

```text
admin-dev.zetruv.com
        |
        v
api-dev.zetruv.com
        |
        v
zetruv_dev
```

Temporary compatibility during frontend migration:

```text
admin.zetruv.dualangka.com -> api-dev.zetruv.com
```

## DEV contract

| Purpose | Value |
| --- | --- |
| Admin frontend primary origin | `https://admin-dev.zetruv.com` |
| Admin frontend legacy origin | `https://admin.zetruv.dualangka.com` |
| API base URL | `https://api-dev.zetruv.com` |
| Canonical CMS prefix | `/api/v1/cms` |
| Login | `POST /api/v1/cms/auth/login` |
| Backend branch | `dev` |
| Database | `zetruv_dev` |
| Bootstrap admin email | `admin-dev@zetruv.com` |

The admin frontend only receives the API base URL, API contract, and a DEV test login shared separately. Do not expose the backend `.env`, PostgreSQL credentials, JWT signing key, webhook secrets, Docker, or VPS access to the frontend.

## Frontend environment

Use a frontend environment variable such as:

```env
VITE_API_BASE_URL=https://api-dev.zetruv.com
```

New DEV admin work must use the canonical `/api/v1/cms` routes. The old `/api/v1/admin` aliases are compatibility routes, not the target integration.

## Authentication

Login request:

```http
POST /api/v1/cms/auth/login
Content-Type: application/json
```

```json
{
  "email": "admin-dev@zetruv.com",
  "password": "<shared separately>"
}
```

The successful response returns a JWT in `accessToken`. Protected CMS calls use:

```http
Authorization: Bearer <accessToken>
```

Without a valid JWT, protected CMS routes must return `401` or `403` as appropriate. Frontend route guards are UX only; backend authorization remains the security boundary.

## CORS

DEV accepts exactly these admin origins during the migration:

- `https://admin-dev.zetruv.com`
- `https://admin.zetruv.dualangka.com` (temporary legacy)

Wildcard CORS must not be used.

## Acceptance

After deploying DEV runtime configuration, run:

```bash
bash scripts/smoke-admin-handoff-dev.sh
```

PASS requires:

1. CORS accepts the new DEV admin origin.
2. A protected CMS request without JWT is rejected.
3. Canonical CMS login succeeds using the DEV bootstrap admin.
4. The returned JWT can access `GET /api/v1/cms/orders`.
5. The legacy admin origin remains accepted during cutover.

Once this passes, backend Admin DEV is ready for the frontend/admin deployment to `admin-dev.zetruv.com`.
