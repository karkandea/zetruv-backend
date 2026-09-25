# Digital purchase discount voucher contract

Figma reference: Digital Purchase Flow, node 3064:8.
Scope: real backend voucher rules and CMS configuration. Customer FE remains a separate implementation.

## CMS and customer API
- GET /api/v1/cms/discount-vouchers: list codes, schedule, kind, usage counts and hasClaims; CMS JWT.
- POST /api/v1/cms/discount-vouchers: create using code, type (Fixed/Percentage), value, maximumDiscount (percentage only), minimumSpend, applicableKind (optional ProductKind), maxUses, maxUsesPerCustomer, isActive, startsAt, endsAt.
- PUT /api/v1/cms/discount-vouchers/{id}: edit; immutable code/discount/per-customer rules after any claim (including released claims). Disabling leaves earlier order totals unchanged.
- DELETE /api/v1/cms/discount-vouchers/{id}: soft-disable; no redemption deletion.
- POST /api/v1/checkout/vouchers/preview: {code,items:[{productVariantId,quantity}],customerEmail?,customerPhone?}. Returns code, subtotal (post-flash-sale), eligibleSubtotal, discountAmount, total. Rejects invalid/early/expired/ineligible/over-limit codes and unavailable SKUs. Rate-limited. Preview does not claim usage.
- POST /api/v1/checkout/orders: optional voucherCode on the existing checkout request. Checkout recalculates authoritative price and claims voucher capacity in the same DB transaction as order creation; no trust in client preview amount. Response includes voucherCode, voucherDiscountAmount and existing subtotal/discountAmount/grandTotal. Payment uses actual GrandTotal.

## Accounting and limits
- Order subtotal and discountAmount keep their existing semantics: subtotal is regular-price sum; discountAmount is flash-sale difference PLUS voucher discount.
- Voucher discount applies to post-flash-sale eligible line totals; shipping is never discounted. Discount is capped by eligible/payable item total. Checkout rejects zero-amount payment.
- Claimed orders reserve usage capacity. Unpaid cancellation releases capacity exactly once; paid/refunded claims remain consumed. Existing failed-payment orders may retry payment without a second coupon claim.
- A PostgreSQL transaction-level advisory lock serializes concurrent claims for a code. Unique redemption per order and code uniqueness are enforced in DB.
- Per-customer usage uses a hash of the provided normalized email (or phone if email absent). Guest identity is not independently verified: limits are not an anti-fraud guarantee.
- Expired unpaid order claims do not auto-release yet; admin cancellation is supported. This is a follow-up operational enhancement.
- Game voucher *product code inventory/delivery* is separate from discount voucher codes and is NOT implemented by this PR.
- Real payment channels/provider integration and customer FE voucher UI are separate gaps. Never synthesize a successful payment or a redeemable product code.

## Tests
- Expanded storefront smoke: CMS CRUD validation, preview, actual order grand total and CMS audit, duplicate customer limit, total cap, rollback on invalid claim, unpaid cancellation release, disable, and two simultaneous claims for the last slot.
- Existing full backend integration suite must pass before merge.
