# Checkout price-change state — Figma Digital Purchase

Reference: Digital Purchase Flow, node `3064:8`, state `PRICE CHANGE` (promo/price changed; show new price, require acknowledgment).

For each `POST /api/v1/checkout/orders` item, callers can supply `expectedUnitPrice` (decimal, the effective displayed SKU price from PDP/cart) and `acknowledgePriceChange` (boolean, defaults to false).

When a supplied expected price differs from the current effective SKU price (including an active flash sale), the API returns **409 Conflict** with `code: PRICE_CHANGED` and `priceChanges: [{ productVariantId, expectedUnitPrice, currentUnitPrice }]`, before persisting any new order, claiming a discount voucher or consuming a validation.

After displaying the updated price, the caller retries with **both** `expectedUnitPrice` set to the **new `currentUnitPrice`** returned in the 409 response **and** `acknowledgePriceChange: true`. This acknowledgement cannot bypass a later price change: a stale quoted value still returns a fresh 409 `PRICE_CHANGED`, and an acknowledgement without an expected price returns 400. Checkout always charges the current server-side price. A matching price proceeds normally. The idempotency key should be reused for a retry that has not created an order, while the request should remain unchanged when replaying an already-created order.

The optional fields preserve old clients that do not send price expectations, but full Figma UX enforcement requires a customer FE integration (outside this backend-only scope). FE is not changed here.
