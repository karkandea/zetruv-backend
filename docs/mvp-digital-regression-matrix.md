# Zetruv MVP digital checkout regression matrix

Internal smoke evidence is NOT proof of an authorized Xendit or supplier transaction.

PASS conditions covered by existing tests:
- PDP/cart pricing parity: FE shared getDigitalPricing and cart tests (PDP test to be added).
- Same SKU/different target and idempotent order: smoke-storefront-scope.sh.
- Owner-only interrupted POST recovery: GET /api/v1/checkout/orders/recovery, smoke-storefront-scope.sh.
- NotStarted / Pending / Expired / retry / Paid / Failed: smoke-payment-order-access.sh using synthetic mock provider.
- Event replay and signature/replay conflict: smoke-payment-webhook-event-ledger.sh.
- Manual-login encrypted paid-only task: smoke-manual-login-credentials.sh.
- Xendit QR/redirect/expiry/attempt: tests/XenditContractSmoke/Program.cs, synthetic HTTP handler.

REQUIRED LATER in Xendit sandbox: two actual test orders, real QR string and provider redirect, webhook callback authorization, wrong amount/currency/reference, replay, user refresh and same-order retry, mobile/desktop screenshots, full status and fulfillment flow.

NOT IMPLEMENTED YET: real provider AUTO_ID, legally authorized Via Login workflow, complete cross-device customer cart synchronization.
Do not advertise synthetic test success as real payment or delivery.
