# Customer order detail — Figma Digital Purchase

## GET /api/v1/me/orders/{orderId}

Customer bearer authorization required. Only the *owning verified session* may see this order. Other customers receive 404; guest and CMS tokens are rejected. Cache-Control is `private, no-store`. This endpoint is read-only and cannot settle payments or trigger fulfillment.

Each order item has its own stable `id`, item `kind`, `variantName`, `quantity`, `unitPrice`, `lineTotal`, `fulfillmentStatus` and optional `target` snapshot (`accountDisplayName`, `fields`). This distinguishes purchases of the same SKU for two different User ID / Zone destinations without exposing encrypted MANUAL_LOGIN credentials. Target values are read from already-validated AUTO_ID destination snapshots; credential-like keys are defensively redacted.

The response also includes payment/fulfillment-facing order status, totals and purchase timestamps. No CMS-specific payment transaction metadata, shipping customer address or sensitive login payload is exposed.

Covered by `scripts/smoke-customer-auth.sh`: signed-in owner can see both same-SKU item targets, unrelated verified user cannot, guest and CMS tokens cannot, unexpected sensitive destination fields are not returned.

Backend-only. Customer FE is intentionally unchanged and must wire this endpoint separately when implementing the Figma order detail screen.