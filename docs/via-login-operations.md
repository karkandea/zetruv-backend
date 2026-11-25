# Top Up via Login — operating contract

Scope: Zetruv DEV/STAGING only until exact publisher/provider authorization and operator readiness are documented. A published catalog item is not approval to sell. Never turn on readiness globally to make checkout work.

## Customer journey

1. Choose an active package; PDP and checkout distinguish SKU availability from service readiness.
2. Checkout validates the product-specific schema and contact. Existing checkout safeguards stay in force; no passwords or verification codes in cart, support messages, analytics, receipts, or evidence. Prefer publisher-approved delegated/official checkout methods and use only authorized access procedures.
3. Midtrans status reconciliation or verified notification confirms payment; the paid order becomes Processing. Payment is not evidence of delivered currency.
4. An authorized operator checks the intended account, package and amount, uses the publisher-approved fulfillment channel and verifies the result. Record the official receipt/transaction reference and retain redacted before/after evidence according to the applicable retention policy. Do not store recovery codes or authentication secrets in operational evidence.
5. CMS may mark Completed only with a nonblank transaction/receipt reference; the server clears existing encrypted login data. Customer order detail reflects completion and requests checking the in-game balance.
6. If access expires or the operation fails, show Failed / needs support and keep the paid order visible. Do not blindly retry or collect verification codes in chat. Support must resolve via an approved process, or arrange a refund according to actual payment and fulfillment status.

## Activation checklist (per product)

- Written authorization for this SKU, game/platform and purchase route; verify publisher terms.
- Approved operator access method, operator availability, least privilege, access audit, and redacted evidence handling.
- Confirm inventory, pricing and expected processing time.
- Encryption and expiry configuration, no plaintext in logs/storage.
- Sandbox payment confirmation and synthetic workflow test through completion/failure/expiry/cancellation.
- Review refund policy and handling; no fabricated authorization, fulfillment evidence, or Paid/Completed state.

The existing readiness allowlist, ProviderAuthorizationConfirmed, OperatorsReady and credential protection checks remain fail closed. No environment flags or production settings are changed by this patch. Completion reference is an operator attestation, not automated supplier receipt verification. No new credential collection/reveal surface or provider integration is added.

## Regression

- Existing isolated `smoke-manual-login-credentials.sh`: synthetic authorized fixture, missing fields / one-time secret rejection, paid-only handling, metadata only, completion reference required, cleanup, expired paid item failure and blocked retry.
- Frontend keeps existing PDP/cart/checkout/admin layout, adds readiness notice and order guidance, requires receipt reference on CMS completion.
