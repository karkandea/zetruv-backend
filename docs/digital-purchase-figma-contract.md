# Digital purchase contract — Figma Digital Purchase Flow

Reference: https://www.figma.com/design/HVAurT3YUzZfZb7b5yUBla/Zetruv---Redesign--Web-Phase-1-?node-id=3064-8

Scope of this PR: backend contract and existing admin CMS. This PR does **not** implement customer storefront screens or claim visual 1:1 of those screens.

## Verified in this PR
- Per-variant optional `groupName` (e.g. Diamonds / Starlight): configured by CMS, returned in CMS product detail and public PDP. Existing variants remain ungrouped, with no destructive data migration.
- Digital order creation requires customer WhatsApp/phone. Merchandise-only shipping flow keeps existing validation.
- The existing dynamic per-product account validation fields, checkout-time encrypted MANUAL_LOGIN fields, per-item fulfillment state machine, inventory reservations, provider reconciliation and paid-order history remain in place and regression-tested.
- Automated backend integration suite exercises old and new flows, including grouped SKU round-trip and missing WhatsApp rejection.

## Exact Figma gaps not yet implemented
- CMS-configured discount **voucher codes** with validity, eligibility, usage and concurrency-safe redemption; current promotion CMS only handles scheduled flash-sale prices. Do not fake applied discounts.
- Redeemable **Game Voucher stock/code delivery** and post-payment reveal; GameVoucher products currently use MANUAL fulfillment.
- Selected payment-channel API (QRIS, BCA virtual account, GoPay), real Xendit integration and provider availability. Current gateway abstraction has `mock` only. No fake QR/VA or paid-state simulation.
- Idempotent create-order key and same-SKU/different-destination cart rows; existing cart is keyed by customer+variant only.
- Explicit price-changed acknowledgment, pending payment outcome/status polling contract and complete customer-facing state fidelity.
- Customer FE route/render changes are intentionally out of scope. This Figma page illustrates customer screens, not CMS mockups; CMS layout reuses the existing admin design system instead of asserting an unavailable 1:1 admin design.

## Safety / operational rules
- Admin UI must never mutate payment status to simulate provider settlement. Payment comes from provider/webhook/reconciliation.
- Per-item fulfillment only after Paid and using system-supported status transitions.
- Game Account remains unique inventory quantity=1 with reservation and release/consume handled by backend.
- Login credentials stay encrypted; no raw credential storage in cart or operational notes.
