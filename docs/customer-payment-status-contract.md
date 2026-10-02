# Customer payment status — Figma Digital Purchase

Reference: Figma Digital Purchase, node 3064:8, states Pending / Paid / Failed / Expired; payment can be retried on **the same order**.

## Authenticated order status
`GET /api/v1/checkout/orders/{orderId}/payment`

Requires the `X-Order-Access-Token` issued with checkout, like the existing `POST .../payment` endpoint. Missing/invalid/cross-order tokens return 404. The response is `private, no-store`, rate-limited to 60 requests/minute per IP.

Returns `orderId`, `orderNumber`, authoritative `paymentStatus` (Pending, Paid, Failed, Refunded), customer-facing `state` (NotStarted, Pending, Paid, Failed, Expired, Refunded, Cancelled), `canRetry`, `hasActivePaymentSession`, `provider`, `providerReference`, `expiresAt`, `paidAt`, and `checkedAt`. Payment URL / QR payload are returned **only** for a currently active pending session, never expired links.

`canRetry` is a preflight hint (order unpaid, not cancelled, no active payment session, MANUAL_LOGIN credentials usable), not a guarantee of inventory or gateway availability; `POST .../payment` remains authoritative.

The GET action is read-only; it **never** marks an order paid/failed, calls gateway mutation APIs, consumes inventory, or starts fulfillment. Server-side provider webhooks and the reconciliation worker remain the sole source of payment settlement.

## Local regression
`bash scripts/smoke-payment-order-access.sh`: no token / cross-order denied, new order = NotStarted, active attempt = Pending, elapsed deadline = Expired without payment details, same-order retry = Pending again.

This is backend-only. Xendit live payment-channel selection and customer FE states remain separate work.
