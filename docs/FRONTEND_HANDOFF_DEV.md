# Frontend API handoff — DEV

Status: **DEV acceptance only**. DEV is moving to the client-owned `zetruv.com` domain. STAGING remains separate and must not be used as a fallback.

## Environment contract

| Purpose | Value |
| --- | --- |
| Frontend primary | `https://dev.zetruv.com` |
| API primary | `https://api-dev.zetruv.com` |
| Temporary frontend alias | `https://dev.zetruv.dualangka.com` |
| Temporary API alias | `https://api-dev.zetruv.dualangka.com` |
| Health | `GET /health` |
| OpenAPI | `GET /openapi/v1.json` |
| Backend source | branch `dev` |
| Database | isolated DEV database `zetruv_dev` |

Frontend only consumes the HTTPS API. Frontend developers do **not** need PostgreSQL credentials, VPS access, Docker access, JWT signing keys, webhook secrets, or the backend runtime `.env`.

Use:

```env
VITE_API_BASE_URL=https://api-dev.zetruv.com
```

Do not hard-code STAGING or the legacy API URL in new DEV frontend work.

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

Successful response contains `accessToken`, `expiresAt`, `email`, and `role`. Protected CMS calls send:

```http
Authorization: Bearer <accessToken>
```

The DEV test password must be shared separately with the CMS frontend developer. Never commit it to a frontend repository.

## Error handling

Frontend should primarily branch on HTTP status. ASP.NET validation/framework failures use Problem Details / validation Problem Details. Some application-level failures return a JSON `message` field. The frontend error normalizer should tolerate `message`, `title`, `detail`, and `errors`.

Important statuses include `400`, `401`, `403`, `404`, `409`, `422` when returned by a feature, and `429` for rate-limited public provider operations.

## CORS

Primary DEV browser origin:

```text
https://dev.zetruv.com
```

During migration, backend DEV also accepts:

```text
https://dev.zetruv.dualangka.com
```

Do not add wildcard CORS.

## Environment isolation rule

```text
CMS/API DEV -> zetruv_dev -> Frontend DEV
```

A banner, product, article, promotion, or other record created in DEV is not expected to appear in STAGING.

## DEV acceptance checklist

Run on the DEV backend host after DNS, deploy, and Nginx/SSL setup:

```bash
bash scripts/smoke-frontend-handoff-dev.sh
```

PASS requires:

1. `https://api-dev.zetruv.com/health` responds.
2. OpenAPI is published on the new API domain.
3. Homepage responds on the new API domain.
4. CORS accepts `https://dev.zetruv.com`.
5. The old API alias remains healthy during cutover.
6. CORS still accepts the old DEV frontend during cutover.

Only after DEV passes should STAGING be migrated to `zetruv.com`.
