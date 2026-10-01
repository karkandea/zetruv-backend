# Customer cart target-line contract

Reference: Digital Purchase customer flow, Figma node 3064:8.

This contract fixes the old cart behavior where one customer + one variant
could only have one row. AUTO_ID digital products may now keep the same SKU
for multiple validated destination accounts.

## Supported customer flow

1. Validate the game account through `POST /api/v1/game-account/validate`.
2. Add/update the SKU through `PUT /api/v1/me/cart/items/{variantId}` with:
   - `productVariantId`
   - `quantity`
   - `gameAccountValidationId`
3. Repeat with the same variant and a different validation ID to create a
   separate cart line.
4. Read `GET /api/v1/me/cart`; each line exposes its cart-line ID and a
   non-sensitive `target` snapshot.
5. At checkout, send each line as a separate checkout item using its own
   `gameAccountValidationId`.

The checkout service already groups by variant + validation ID, so different
targets remain distinct order items.
## Line identity and persistence

`customer_cart_items` now stores:
- nullable `GameAccountValidationId`
- non-secret internal `LineKey`

LineKey is deterministic:
- ordinary product: `{variant}:default`
- validated AUTO_ID target: `{variant}:{validation}`

The unique constraint is customer + LineKey, replacing customer + variant.
Existing rows are migrated to their default LineKey without changing quantity.

The cart still supports at most 50 distinct lines per customer.

## Validation rules

For an AUTO_ID product with `RequiresGameAccountValidation=true`:
- validation ID is required before the line can be added;
- validation must belong to the selected product;
- validation must be unconsumed, unattached to an order item, and unexpired.

Products that do not require AUTO_ID account validation reject a cart target.
An expired or consumed validation does not silently disappear from the cart:
the line remains visible with `isAvailable=false`, so customer UI can ask
the customer to validate the destination again.
## Cart response

Target-aware lines return:
- validationId
- accountDisplayName
- fields
- expiresAt

AccountValidation schema explicitly forbids password/sensitive fields, and the
validation service also blocks transient/session keys. Therefore this response
contains only the non-sensitive destination fields already submitted for
account validation.

Raw MANUAL_LOGIN credentials are intentionally NOT persisted in cart.
For MANUAL_LOGIN products, credentials continue to be supplied only at
checkout and handled by the existing encrypted credential flow.

## Delete behavior

Preferred line-specific removal:
`DELETE /api/v1/me/cart/lines/{cartItemId}`

Backward-compatible old route:
`DELETE /api/v1/me/cart/items/{variantId}`

The old route intentionally removes every cart line for that SKU. New customer
FE should use the cart-line route when target-aware rows are rendered.

`DELETE /api/v1/me/cart` still clears the whole authenticated customer's cart.
## Stock and checkout behavior

Adding a cart line does not reserve inventory. Existing payment initiation
remains the point that creates stock reservations.

The authoritative checkout re-checks:
- product/variant availability,
- target validation ownership-by-capability and expiry,
- requested quantity,
- current price/promotion,
- payment inventory reservation.

After a validation is consumed by an order, any old cart line using that
validation becomes unavailable. Reusing the consumed validation is rejected.

## Migration rollback

Rolling back to the legacy one-row-per-variant schema cannot preserve multiple
target rows. The Down migration therefore keeps only the newest row for each
customer + variant before recreating the old unique constraint.

## Verification

`smoke-storefront-scope.sh` covers:
- same SKU + two distinct validated account targets,
- target metadata projection,
- upsert of one target without overwriting its sibling,
- missing/wrong target rejection,
- checkout into two distinct order items,
- consumed target becoming unavailable,
- line-specific delete,
- legacy variant delete compatibility.
