# Zetruv MVP provider activation — DEV only

## Midtrans Snap in DEV

1. In the Sandbox dashboard, obtain the Server Key for the runtime secret store and set the HTTP payment notification URL to `https://api-dev.zetruv.com/api/v1/payments/webhooks/midtrans`.
2. Set `PAYMENTS_PROVIDER=midtrans`, `MIDTRANS_ENABLED=true`, `MIDTRANS_MODE=Sandbox`, `MIDTRANS_SERVER_KEY`, and `MIDTRANS_RETURN_URL=https://dev.zetruv.com/payment`. Zetruv verifies the signed notification against Midtrans's status API and the order's exact amount before fulfillment begins.
3. Set `MIDTRANS_MERCHANT_ABSORBS_FEES=true` when the merchant covers gateway fees. Choose the supported methods for `MIDTRANS_CHANNEL_0`, `MIDTRANS_CHANNEL_1` or `MIDTRANS_CHANNEL_2`, then enable the matching CMS methods. `GOPAY` uses Snap GoPay, `QRIS` uses Snap Other QRIS, and `BCA_VA` uses Snap BCA Virtual Account. Enable only channels present in the merchant dashboard.
4. Use Snap's hosted checkout in the same tab. Test return to `/payment`, pending, success, expired and retried sessions, webhook signature and amount validation, and duplicate notifications. A closed Snap session can continue on the same URL until expiry, and Zetruv checks Midtrans before allowing a replacement attempt.

Docs: https://docs.midtrans.com/docs/snap-snap-integration-guide · https://docs.midtrans.com/docs/https-notification-webhooks

## CODM Garena pilot with Digiflazz

1. In DEV, set `DIGIFLAZZ_ENABLED=true`, `DIGIFLAZZ_TESTING=true`, `DIGIFLAZZ_USERNAME`, and `DIGIFLAZZ_API_KEY` with the Buyer Development key in the runtime secret store. Use mock payment for backend smoke or Midtrans Sandbox for storefront testing.
2. The draft product uses slug `call-of-duty-mobile-via-id` and input `openid`. In a disposable DEV test, map a test package to Digiflazz's documented test code `xld10` and use its documented synthetic customer numbers. Those transactions verify the provider contract. Verify the real CODM Garena SKU and OpenID syntax using the selected seller before configuring a live listing.
3. In CMS, map the game to provider `digiflazz` with nickname check off, and map the product variant to the exact Buyer SKU. Set the game, product and variant active only when their mappings and package details are ready. The OpenID check confirms format. The supplier confirms delivery.
4. A paid order stays Processing until Digiflazz reports success or rejection. The worker polls with the original SKU, OpenID and reference, at least one minute apart. Keep the Buyer username and mode stable while outstanding orders are being reconciled. Review rejected items and expired references in the Buyer dashboard.
5. For live CODM, use the Buyer live key, `DIGIFLAZZ_TESTING=false`, a positive `DIGIFLAZZ_MAX_PRICE` in IDR, and live Midtrans. Confirm supplier authorization, active SKU, pricing and settlement terms before the first live order.

Use `python3 scripts/check-digiflazz-codm.py` on the allowlisted server with `DIGIFLAZZ_USERNAME` and `DIGIFLAZZ_API_KEY` in its process environment. It reports Buyer deposit and matching CODM package records already configured for that Buyer. An empty result means no CODM SKU is present in the account's API price list. Add a package in the Buyer panel, then repeat the check.

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
