# Game Voucher encrypted inventory contract

Figma reference: Digital Purchase Flow, node 3064:8.

This contract covers backend + CMS behavior for prepaid Game Voucher products.
Customer storefront UI is intentionally not implemented in this PR.

## Source of truth

For `ProductKind.GameVoucher`, sellable stock is backed only by encrypted voucher-code inventory.
CMS cannot type an arbitrary stock number for a Game Voucher variant.

Creating a Game Voucher variant accepts stock null/0 and stores stock 0.
Updating the variant must preserve the backend stock value.
Importing a previously unseen encrypted code increments sellable stock atomically.
Revoking an unassigned, unreserved code decrements sellable stock atomically.

Migration `AddEncryptedGameVoucherInventory` fails closed for pre-existing Game Voucher variants:
legacy manually-entered stock becomes 0 and active reservations are released without restoring phantom stock.
The catalog stays unavailable until legitimate redeem codes are imported.

## CMS inventory API

Base route:
`/api/v1/cms/catalog/products/{productId}/variants/{variantId}/voucher-codes`

- GET: counts + non-secret inventory metadata.
- POST: bulk import 1-500 codes; duplicate plaintext codes are skipped by keyed hash.
- DELETE /{codeId}: revoke an Available code only.
CMS never receives stored raw code values back from GET.
Assigned codes cannot be revoked.
If remaining Available codes are already committed by payment reservations, revoke returns conflict.

Import and revoke use a per-variant PostgreSQL advisory lock.
Stock changes use atomic database updates so payment reservation and CMS inventory mutation cannot create phantom stock.

## Encryption and duplicate detection

Raw redeem codes are never stored in plaintext.
`GameVoucherCodeProtector` derives a Game-Voucher-specific key from the deployment's existing
`ManualLogin:EncryptionKey` and uses AES-GCM with record-specific associated data.

Duplicate detection uses a keyed HMAC hash rather than a raw SHA hash.
The raw code is not written to fulfillment references, activity logs, CMS responses, public catalog,
payment records, or public order tracking.

## Payment and assignment

Game Voucher checkout requires a signed-in, verified customer account. Guest checkout fails before payment initiation because redeem codes are exposed only through the authenticated owner route.

Payment initiation uses the existing inventory reservation service and decrements variant sellable stock.
A successful Paid transition consumes that reservation.

After Paid, the existing automatic fulfillment dispatch invokes Game Voucher assignment.
For each Processing Game Voucher item, backend atomically selects the required number of Available codes,
marks them Assigned to the OrderItem, and completes that item.

Normal assignment does not decrement stock again: reservation already did it.
If code inventory is inconsistent at Paid time, fulfillment fails closed with
`GAME_VOUCHER_CODE_SHORTAGE`; no fabricated code and no fake Completed state is produced.
CMS may repair that specific shortage by importing replacement code(s), then calling:
`POST /api/v1/cms/orders/{orderId}/items/{orderItemId}/game-voucher-assign`.

The retry re-acquires the newly imported sellable stock before assigning the replacement code.
Generic manual fulfillment is blocked for Game Voucher items, so admin cannot bypass code assignment.

## Customer reveal

Authenticated owner-only route:
`GET /api/v1/me/orders/{orderId}/items/{orderItemId}/voucher-codes`

Requirements:
- CustomerBearer authentication.
- Order belongs to the authenticated customer.
- PaymentStatus is Paid.
- Game Voucher item is Completed.
- Assigned-code count matches ordered quantity.

The response uses `Cache-Control: private, no-store`.
Each reveal increments audit counters and emits a non-secret fulfillment activity.
Another customer receives NotFound instead of ownership details.

Raw codes are not added to guest/public order lookup.
The customer FE should call this endpoint only on the paid/completed order screen.

## Operational limits

This feature manages prepaid code inventory; it does not provide external payment rails.
Real QRIS / virtual-account / e-wallet provider integration remains a separate scope.
Refunding a delivered prepaid code does not automatically rotate or reclaim that code.
Any commercial refund/replacement policy must be handled explicitly rather than making an assigned code sellable again.
