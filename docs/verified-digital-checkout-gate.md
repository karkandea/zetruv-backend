# Figma Digital Purchase verified checkout (backend)

The Figma flow `Cart → Login Gate → Checkout` requires a signed-in, email-verified customer before placing a digital order.

**New strict endpoint:** `POST /api/v1/checkout/verified/orders` with `Authorization: Bearer <customer JWT>`.

- Endpoint is customer-authorized, not guest/CMS; invalid or missing bearer tokens yield HTTP 401. Account must be active and email-verified in the database.
- It delegates to the **same** verified checkout service and price/voucher/idempotency/reservation rules as legacy `POST /api/v1/checkout/orders`. Checkout email is tied to the verified account.
- Successful orders have `CustomerUserId` persisted and can be retrieved only by their owner using `GET /api/v1/me/orders/{id}`; payment access token behavior is unchanged.
- Smoke `scripts/smoke-storefront-scope.sh` proves guest/CMS 401, verified customer 201 and owner-only order read.

## Migration warning — not fully enforced yet
The existing `POST /api/v1/checkout/orders` still supports legacy guest checkout for TopUp/Joki to avoid silently breaking the existing customer frontend. Its guest branch does not meet the full Figma login-gate rule. Customer FE must be migrated to the **strict** endpoint, then legacy guest digital checkout must be disabled through a separate, regression-tested cutover. No customer FE was changed here.

No Xendit work or schema migration.
