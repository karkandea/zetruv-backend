# MVP acceptance gate: Top Up via Login + Xendit payments

Updated 13 October 2026. DEV only. Do not turn on production payments without the checklist and provider permission.

## What already exists
- `MANUAL_LOGIN` catalog variants with dynamic `LoginCredential` input-field schema and strict schema validation at checkout.
- AES-256-GCM encrypted order-item credentials, paid-only CMS reveal, reveal audit and retention cleanup.
- CMS manual fulfillment queue / status transitions after verified Paid status. Completion/cancellation clears credentials.
- Idempotent verified-customer order creation and provider-webhook/reconciliation-authoritative settlement.
- Public per-product readiness: `GET /api/v1/checkout/manual-login/readiness/{productId}`.
- Real Xendit Payments API v3 adapter (QRIS, GoPay) behind explicit configuration. NOT verified in sandbox yet; no live payment state should be advertised without configured test keys and real callbacks.
- AUTO_ID requires server-issued `GameAccountValidationId`, which the storefront now requests before order creation.

## Required operator/provider decision — NOT a code toggle

The official HoYoverse help center instructs players to use its own authenticated top-up center. It warns against non-official third-party top-up channels. An account-password resale/processing service MUST NOT be launched based solely on a working password form.

Before marking any Genshin Impact product authorized, the product owner must provide written authorization or an officially supported provider agreement/workflow that permits the exact planned service, including handling credentials. Prefer official account-ID transfer or provider-approved delegated authentication that does not expose the game account password.

If the supplier cannot approve account access, migrate that product to an authorized `AUTO_ID` product/provider flow; do not silently take payment for an unavailable manual-login product.

## Enabling *a specific approved* Via Login product

Store these in protected environment variables, not the repo, only after authorization and an on-duty fulfillment process are confirmed:

```dotenv
ManualLogin__EncryptionKey=<base64-encoded-32-byte-key>
ManualLogin__ProviderAuthorizationConfirmed=true
ManualLogin__OperatorsReady=true
ManualLogin__AuthorizedProductIds__0=<approved-product-uuid>
```

The public readiness endpoint returns `Available: true` only if all gates and active catalog/input-field criteria pass. Order creation rejects unsupported `MANUAL_LOGIN` regardless of frontend state.

The CMS manual task must be fulfilled after Paid and before the configured credential retention expires. Confirm operational staffing, failure/refund policies and protected access to credential reveal.

## Xendit test mode — no real payments

An actual test account/API key and callback token must be obtained from the payment account owner. Use the official Xendit Payments API v3 webhook endpoint `https://api-dev.zetruv.com/api/v1/payments/webhooks/xendit` and configure the Dashboard Payments API callbacks (not per-channel duplicates).

```dotenv
Payments__Provider=xendit
Payments__Xendit__Enabled=true
Payments__Xendit__Mode=Sandbox
Payments__Xendit__ApiKey=<xnd_development_secret_key>
Payments__Xendit__WebhookToken=<unique-dashboard-callback-token>
Payments__Xendit__ReturnUrl=https://dev.zetruv.com/payment
Payments__Xendit__Channels__0=QRIS
Payments__Xendit__Channels__1=GOPAY
Payments__Xendit__MerchantAbsorbsFees=true
```

The merchant absorption policy must be explicitly confirmed; otherwise the method has no customer fee quote and checkout remains blocked. For GoPay, open the provider payment URL in the **same browser tab** (not a new tab) and configure its success return URL as `https://dev.zetruv.com/payment` so the tab-scoped, non-sensitive order session can be restored. Returning to `/checkout` loses the payment-focused recovery UI. The readiness response exposes `IsSandbox` to prevent the UI from claiming real payments.

`BCA_VA` remains unsupported by this adapter until a dedicated **VA account-number/payment instruction** model and frontend are implemented and tested. Never list VA as available just because a CMS icon exists.

## Required sandbox acceptance testing

1. Confirm public payment readiness returns `IsSandbox: true` and only actually configured CMS-enabled channels.
2. Make a test payment request using only the Xendit development key and confirm QR payload or redirect.
3. Simulate a paid provider event from Xendit, with a real Xendit callback token; verify the order remains Pending until receipt and only then moves to Paid and fulfillment Processing.
4. Reject spoofed/unsigned, wrong-amount, wrong-currency, mismatched-reference and duplicate callbacks; repeat the callback to check deduplication.
5. Verify reconciliation and timeout recovery, one order and one payment attempt for repeated clicks, and that non-approved services cannot create orders.
6. For approved manual-login synthetic account test, verify post-Paid CMS queue/reveal/audit and clearing on completion. No actual game passwords in any test fixture.
7. Run `scripts/smoke-manual-login-credentials.sh` and all backend integration tests. Confirm customer-facing order tracking is restricted to the owning customer.

**Outstanding outside-repo prerequisites:** provider authorization, legitimate fulfillment workflow/operator and refund SLA, Xendit test credentials and callback config, test simulation proof, final pricing/payment method charges, BCA VA instruction contract.

Security: never put test/provider keys, game passwords or callback tokens in GitHub, logs, screenshots, or frontend environment variables.
