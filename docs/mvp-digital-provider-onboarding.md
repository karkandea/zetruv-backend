# Zetruv MVP provider activation — DEV only

Updated 13 October 2026. These are prerequisite checks, NOT approval to activate real transactions.

## Xendit Sandbox — Product Owner checklist
- Merchant Xendit TEST account: Payments API v3, Indonesian QRIS and GOPAY eligibility verified.
- In trusted secrets manager ONLY: development API secret key (xnd_development_), Xendit dashboard callback token. Never chat, source control, logs, customer-facing JSON, or screenshots.
- Operator registers one Payments API webhook URL: https://api-dev.zetruv.com/api/v1/payments/webhooks/xendit.
- Return URL: https://dev.zetruv.com/payment. GoPay redirect must remain in the same tab to retain payment session.
- PO confirms who pays channel fees. MerchantAbsorbsFees=true only with explicit signoff; otherwise implement a real fee quote before enabling checkout.
- Enable only QRIS/GOPAY in Xendit AND Zetruv CMS; BCA_VA stays off until VA instructions are supported.
- Validate callbacks, test key rotation, reconciliation alert owner, failed/expired payments, refunds and dispute SLA.

Trusted operator runtime variable names (NEVER commit values):
Payments__Provider=xendit
Payments__Xendit__Enabled=true
Payments__Xendit__Mode=Sandbox
Payments__Xendit__ApiKey=(development secret in secret store)
Payments__Xendit__WebhookToken=(callback token in secret store)
Payments__Xendit__ReturnUrl=https://dev.zetruv.com/payment
Payments__Xendit__Channels__0=QRIS
Payments__Xendit__Channels__1=GOPAY
Payments__Xendit__MerchantAbsorbsFees=true (only after fee policy approval)

Acceptance checklist: payment-options returns isSandbox=true and correct fee; request real test-mode QRIS and GOPAY; verify Pending until authentic webhook/reconciliation; reject wrong callback token/amount/currency/reference; ignore replay; handle failed/expired and same-order retry; verify no duplicate provider payment when refreshing; capture desktop/mobile screenshots with token/IDs masked.
Reference: https://developers.xendit.co/api-reference/payments-api/

## CODM Garena pilot with Digiflazz

1. In DEV, set `DIGIFLAZZ_ENABLED=true`, `DIGIFLAZZ_TESTING=true`, `DIGIFLAZZ_USERNAME`, and `DIGIFLAZZ_API_KEY` with the Buyer Development key in the runtime secret store. Use mock payment for backend smoke or Xendit Sandbox for storefront testing.
2. The draft product uses slug `call-of-duty-mobile-via-id` and input `openid`. In a disposable DEV test, map a test package to Digiflazz's documented test code `xld10` and use its documented synthetic customer numbers. Those transactions verify the provider contract. Verify the real CODM Garena SKU and OpenID syntax using the selected seller before configuring a live listing.
3. In CMS, map the game to provider `digiflazz` with nickname check off, and map the product variant to the exact Buyer SKU. Set the game, product and variant active only when their mappings and package details are ready. The OpenID check confirms format. The supplier confirms delivery.
4. A paid order stays Processing until Digiflazz reports success or rejection. The worker polls with the original SKU, OpenID and reference, at least one minute apart. Keep the Buyer username and mode stable while outstanding orders are being reconciled. Review rejected items and expired references in the Buyer dashboard.
5. For live CODM, use the Buyer live key, `DIGIFLAZZ_TESTING=false`, a positive `DIGIFLAZZ_MAX_PRICE` in IDR, and live Xendit. Confirm supplier authorization, active SKU, pricing and settlement terms before the first live order.

Docs: https://developer.digiflazz.com/api/buyer/topup/ · https://developer.digiflazz.com/api/buyer/cek-status/ · https://developer.digiflazz.com/api/buyer/test-case/

## Alternative provider
Melostore H2H:
- Base https://api.melostore.id/api/v1/h2h; server-only X-API-Key and X-Secret-Key; GET /profile and paginated /pricelists/{category_slug}.
- POST /transaction with exact sku_code, customer_target, optional customer_target_zone and additional_data; durable buyer_trx_id; sandbox_mode=true; optional max_price cap.
- 201/200 may mean pending/processing, NOT delivered. GET /transaction/{id} to recover uncertain result. Final success/failed/refunded via status and webhook.
- Verify webhook X-H2H-Signature HMAC-SHA256 raw body; dedup by final id:status and action event_id/version. Never treat action_change as fulfillment.
- Prepaid H2H MC balance, max_price hold vs price_charged, failed-order refunds and invoice settlement require reconciliation. Production IP allowlisting.
Docs: https://h2h.melostore.id/en/docs

PO decision: pick and contract an authorized supplier; provide test account, game catalog and exact SKU mappings (game, nominal, UID, zone), reseller authorization, pricing and settlement/refund agreement. No provider call may use guessed SKU or production credentials. Prefer one provider pilot, preserve provider reference per order item, dedup webhook and reconcile only after successful payment.

## Via Login — mandatory MVP; keep authorization gate
- Written game publisher or authorized distributor agreement for this exact method and scope, plus permitted authentication protocol and permissible account information.
- Prefer official delegated authorization/provider-hosted login where offered; never assume a game supports OAuth. Do not request or store OTP, 2FA or recovery codes.
- Identify on-duty operators, CMS role separation, encryption/retention policy, SLA, audit and refund handling. Use synthetic test accounts.
- Via Login remains a separate MVP product kind. Do not silently migrate to Via ID; PO must explicitly approve any product/fulfillment scope change.
- Keep per-product backend readiness NotReady until permission, operator capacity and secure workflow are demonstrably fulfilled.

## Cart persistence boundary
- Frontend uses localStorage draft cart; backend /api/v1/me/cart is separate authenticated customer cart. Current system does NOT provide full cross-device synchronization.
- Backend requires fresh GameAccountValidationId for AUTO_ID per target. Blind mirroring before validation would corrupt same SKU/different target lines.
- MANUAL_LOGIN raw credentials must never go to browser cart or server cart; backend cart default SKU line cannot distinguish multiple login identities for one SKU.
- Next engineering work: persistent customer auth in storefront; explicit account-target-aware sync after validation; conflict UI, delete by line ID, duplicate target tests, cross-device race tests. Until then create-order backend is authoritative for price and target.
