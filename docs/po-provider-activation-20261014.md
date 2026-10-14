# Zetruv MVP — Product Owner activation pack (DEV only)

Status 14 October 2026: provider and payment integration are NOT activated; no game-account passwords or secrets should be sent by chat or committed to source control.

## A. Xendit sandbox — exact PO/operator checklist

1. PO opens the merchant Xendit Dashboard and switches to **Test Mode** (environment selector at top). Confirm merchant access to Payments API v3, QRIS and one-time `GOPAY` channels. Both must be enabled/eligible for that merchant.
2. Under **Settings → Developers / API Keys** (Dashboard naming may change), generate/locate a **Test Mode development secret API key**, not a production key. A trusted operator installs it in protected DEV runtime as `Payments__Xendit__ApiKey`; never reveal it in screenshots, chat or Git.
3. Go to **Settings → Developers → Webhooks / Callbacks**. Set the *Payments API / Payment Request* webhook (not older per-channel QR v1 or eWallet v1 callbacks) to `https://api-dev.zetruv.com/api/v1/payments/webhooks/xendit`. Choose **Save and Test** and confirm an HTTP 2xx. For subaccounts, managed/owned permissions may differ.
4. In Dashboard webhook developer settings obtain the **Webhook Verification Token** (X-CALLBACK-TOKEN). Store in DEV-only protected runtime as `Payments__Xendit__WebhookToken`. Backend compares the token before processing events.
5. Operator sets `Payments__Provider=xendit`, `Payments__Xendit__Enabled=true`, `Payments__Xendit__Mode=Sandbox`, `Payments__Xendit__ReturnUrl=https://dev.zetruv.com/payment`, channel list `QRIS` and `GOPAY`. Do not enable `BCA_VA` or set Live mode.
6. PO signs off on fee policy. `Payments__Xendit__MerchantAbsorbsFees=true` means merchant absorbs method fees; if not approved keep checkout blocked until server fee quotes are implemented. CMS also needs QRIS/GoPay enabled.
7. Verify public GET `/api/v1/checkout/payment-options` returns `isSandbox:true`, only two approved methods, and verified fee quotes. Check other methods unavailable.
8. With two synthetic Zetruv customers, create test orders (correct SKUs/targets) and initiate QRIS/GoPay. Read returned QR String/HTTPS redirect and provider `payment_request_id`; ensure Pending, never mark Paid from redirect alone.
9. From a trusted operator workstation with the development key, call Xendit's documented **test-only** `POST /v3/payment_requests/{payment_request_id}/simulate` using the exact amount. A response saying PENDING is not payment success; await authentic Xendit callback or reconcile via GET.
10. Verify callback `x-callback-token`, reference, amount and currency on backend; check replay, duplicate POST, wrong token, delayed/failed/expired, retries after expiry, refund and item fulfillment behavior. Inspect the Xendit Dashboard **Webhooks** tab for delivery attempt, HTTP response and resend. Confirm only Paid after verified provider evidence.
11. Test real display on desktop and mobile, then mask order/access tokens and provider references in evidence. Keep production keys, credentials and live payments completely isolated.

Official sources:
- https://help.xendit.co/hc/id/articles/360038048871-Bagaimana-Cara-Membuat-dan-Melakukan-Update-pada-Webhook-URL
- https://help.xendit.co/hc/id/articles/360038072991-Bagaimana-cara-melakukan-validasi-bahwa-webhook-berasal-dari-Xendit
- https://docs.xendit.co/apidocs/simulate-payment-test-mode
- https://docs.xendit.co/apidocs/create-payment-request

## B. Provider comparison, based on PUBLIC docs only

| Criterion | Digiflazz Buyer | Melostore H2H |
|---|---|---|
| Buyer API | `/v1/price-list`, `/v1/transaction`, `/v1/cek-saldo` | `/api/v1/h2h/pricelists` with cursor, `/transaction` and `/transaction/{id}` |
| Authentication | username plus MD5 API signature per documented transaction; secrets backend-only | `X-API-Key` and `X-Secret-Key`, backend-only plus IP allowlist |
| SKU availability | Exact `buyer_sku_code`, stock and active seller flags from buyer's account-specific price list | Exact active `sku_code`, inquiry fields and regular/smart catalogue; public site advertises MLBB, PUBG, Valorant, Genshin etc |
| Buy price | `price` per seller and buyer price list, may vary; no verified Zetruv account price available | `price` depends on tier, catalogue/server and provider; no verified Zetruv tier price available |
| Initial deposit | Buyer bank-transfer deposit documentation says minimum **Rp200.000** (confirm current account policy) | Terms say H2H Basic tier via one-time prepay **10.000 MC (~Rp10.000)**; Premium via **100.000.000 MC**; deposit cannot ordinarily be cashed out |
| Response | Sukses/Pending/Gagal; reuse SAME ref_id to check pending | processing/pending/success/failed/refunded; re-query buyer_trx_id, 201 is not delivery |
| Callback | Webhook from configured buyer account, signature verification | HMAC-signed webhook and terminal status reconciliation |
| Settlement | Prepaid buyer deposit, `buyer_last_saldo`, invoice/price reconciliation | Prepaid MC balance, price-charged/cap and refund-on-failure; operator must reconcile |
| Game coverage | Depends on buyer price list, seller enablement and specific approved SKU | Public branded game catalogue broad but each API SKU/game/region still must be confirmed |

Recommendation: **pilot Melostore H2H for a limited AUTO_ID game catalogue**, provisionally, because the documented API offers exact target forms, status lookup, a low Basic-tier starting deposit, and explicit sandbox_mode. This is a PILOT recommendation, not a conclusion that its prices are lower or it is officially authorized to fulfill every game. Confirm usable reseller rights, true tier buy prices/SKU mapping, non-refundable balance exposure and service level. Keep Digiflazz as comparator/backup; it has mature seller-specific SKU/status docs and a higher documented initial deposit. Evaluate actual net margin + delivery rate after both account-specific price lists become available.

Supplier selection gate: PO must provide account-specific price list **without API keys**, permitted game list, region/UID/zone schema, fulfillment authorization, prepaid balance policy, refund SLAs, webhook contract and an approved test mode. Never invent SKU mapping or authorize by public website text alone.

References:
- https://developer.digiflazz.com/api/buyer/daftar-harga/
- https://developer.digiflazz.com/api/buyer/topup/
- https://developer.digiflazz.com/api/buyer/cek-saldo/
- https://id.digiflazz.com/docs/thread/1o6yg8
- https://h2h.melostore.id/en/docs
- https://h2h.melostore.id/en/syarat-ketentuan

## C. Via Login remains MVP — authorization gate stays CLOSED

HoYoverse directs users to official account top-up centers and warns against nonofficial third-party channels. No evidence was found of a broadly available publisher-approved third-party game-password API. Do not assume the customer password can be collected.

PO deliverables: written provider/publisher authorization for the exact login-based service, approved delegated authentication or provider-hosted consent flow, allowed credential handling, test credentials for synthetic accounts ONLY, operator staffing/SLA, paid-only fulfillment and refund policies, secure credential rotation/retention.

Engineering keeps product kind TopUpLogin and fulfillment method MANUAL_LOGIN distinct; no silent Via ID conversion. If official workflow disallows passwords, keep item unavailable until PO approves a legitimate equivalent service model. Never collect customer OTP/2FA/recovery codes.

Source: https://support.hoyoverse.com/hc/en-us/articles/48111014402457-How-should-I-top-up-my-game-account-on-the-website

## D. QA truth table

- Automated backend ephemeral-Postgres integration tests: cart merge/replace, target dedup, owner isolation; payment pending/paid/failed/expired/retry, signed mock webhook replay and reconciliation.
- Frontend unit tests: guest cart, remote/guest segregation, authoritative price projection, refresh/recovery checks.
- Visual QA: desktop baseline inspection + headless Chromium screenshot comparison for 1440×900 and 390×844 via `DEV responsive visual QA (no deploy)` action, if runner can capture.
- **NOT PASSED without real provider:** Xendit payments E2E; real game top-up fulfillment; Via Login provider-authorized task.
