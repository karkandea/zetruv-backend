# Frontend API handoff — DEV

Status: **DEV acceptance only**. Do not point frontend STAGING at this environment and do not use STAGING as a fallback while DEV is under test.

## Environment contract

| Purpose | Value |
| --- | --- |
| Frontend | `https://dev.zetruv.dualangka.com` |
| API base URL | `https://api-dev.zetruv.dualangka.com` |
| Health | `GET /health` |
| OpenAPI | `GET /openapi/v1.json` |
| Backend source | branch `dev` |
| Database | isolated DEV database `zetruv_dev` |

Frontend only consumes the HTTPS API. Frontend developers do **not** need PostgreSQL credentials, VPS access, Docker access, JWT signing keys, webhook secrets, or the backend runtime `.env`.

Use one frontend environment variable for the base URL, for example:

```env
VITE_API_BASE_URL=https://api-dev.zetruv.dualangka.com
```

Do not hard-code a STAGING or production API URL in DEV source code.

## Public frontend endpoints

Core public routes currently exposed:

- `GET /api/v1/homepage`
- `GET /api/v1/catalog/categories`
- `GET /api/v1/catalog/games`
- `GET /api/v1/catalog/products`
- `GET /api/v1/catalog/products/{slug}`
- `GET /api/v1/catalog/flash-sale`
- `POST /api/v1/game-account/validate`
- `POST /api/v1/checkout/orders`
- `POST /api/v1/checkout/orders/{orderId}/payment`
- `POST /api/v1/orders/lookup`
- `GET /api/v1/articles/categories`
- `GET /api/v1/articles`
- `GET /api/v1/articles/{slug}`
- `GET /api/v1/site/footer`

The OpenAPI document is the canonical source for request and response schemas.

## CMS authentication contract

CMS routes use the canonical prefix `/api/v1/cms`.

Login:

```http
POST /api/v1/cms/auth/login
Content-Type: application/json
```

```json
{
  "email": "admin-dev@zetruv.com",
  "password": "<DEV test password supplied separately>"
}
```

Successful response:

```json
{
  "accessToken": "<jwt>",
  "expiresAt": "2026-09-06T12:00:00Z",
  "email": "admin-dev@zetruv.com",
  "role": "Admin"
}
```

Protected CMS calls send:

```http
Authorization: Bearer <accessToken>
```

The DEV test password must be shared separately with the CMS frontend developer. Never commit it to a frontend repository.

## Error handling

Frontend should primarily branch on HTTP status.

ASP.NET validation/framework failures use Problem Details / validation Problem Details. Some application-level failures return a JSON `message` field. A frontend error normalizer should therefore tolerate these fields when present:

- `message`
- `title`
- `detail`
- `errors`

Important statuses include `400`, `401`, `403`, `404`, `409`, `422` when returned by a feature, and `429` for rate-limited public provider operations.

## CORS

DEV browser origin must be:

```text
https://dev.zetruv.dualangka.com
```

Backend DEV must return `Access-Control-Allow-Origin` for that exact origin. Do not add `*` as a workaround.

## Environment isolation rule

Data written through DEV CMS/API belongs only to DEV.

Example:

```text
CMS/API DEV -> zetruv_dev -> Frontend DEV
```

A banner, product, article, promotion, or other record created in DEV is not expected to appear in STAGING. STAGING has its own API and database and will be accepted separately after DEV passes.

## DEV acceptance checklist

Run on the DEV backend host after deployment:

```bash
bash scripts/smoke-frontend-handoff-dev.sh
```

PASS requires all of the following:

1. `/health` responds.
2. `/openapi/v1.json` is published.
3. `/api/v1/homepage` responds.
4. Browser preflight from `https://dev.zetruv.dualangka.com` is accepted.
5. Frontend only receives API/base URL and contract information; no backend secrets or infrastructure access is handed off.

Only after DEV passes should the equivalent STAGING handoff be prepared and tested.
