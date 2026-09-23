# Storefront dynamic business contract (Figma Homepage Final)

Scope confirmed: maximum **10 hero records total** (including inactive and scheduled),
popular games are **CMS-curated**, recent purchases are **personal**, leaderboard
is **customer spend**, cart is **account-persistent**, and product statistics must
be derived from real orders and approved reviews.

This branch is stacked on customer-auth PR #56; merge that PR before this one.
Email provider setup is client-owned. No deployment is included in this PR.

## Homepage and banners

- `GET /api/v1/homepage`: public/cacheable homepage. `heroes` is an **ordered array**,
  at most 10. Banner is visible only when active and within its scheduled interval.
  `recentlyPurchased` is deliberately empty to prevent customer activity leaking
  through a shared public cache.
- `GET /api/v1/homepage/personal`: CustomerBearer required, private/no-store,
  same homepage sections with only that customer's paid/completed purchases.
  A disabled `recently_purchased` section yields an empty list.
- `GET /api/v1/me/recent-purchases?limit=5`: alternative, authenticated standalone list.
  Guest orders are **not** automatically attributed by email to registered accounts.
- CMS hero CRUD: `/api/v1/cms/homepage/heroes`. The 11th record returns
  `409 HERO_LIMIT_REACHED`, even when all 10 records are inactive.
  Delete an old banner to create another. A transaction-scoped PostgreSQL advisory
  lock serializes concurrent creates. CTA URLs must be internal rooted paths or
  HTTPS URLs; CTA label/URL must be both set or both omitted.
- Existing CMS section settings control title/subtitle, CTA, visible state,
  display order and item limit. Existing catalog CMS controls the six service categories,
  manually curated popular games, featured products and scheduled flash sales.

**Frontend/CMS wiring still required:** Render all `heroes` as slider, use CMS
media picker instead of URL-only image input, handle zero banners, and avoid slider
arrows/indicators when fewer than three flash-sale cards exist. Do not use mock
cards or show `TERSEDIA` when `isAvailable=false`.

## Account cart and checkout

- `GET /api/v1/me/cart`, `PUT /api/v1/me/cart/items/{variantId}`
  body `{ "productVariantId": "uuid", "quantity": 2 }`,
  `DELETE /api/v1/me/cart/items/{variantId}`, `DELETE /api/v1/me/cart`.
- Customer JWT only. Unique cart row per account+variant, maximum 50 distinct
  variants and 99 units/variant (game accounts: 1), live availability and flash-sale
  unit prices on read. Cart contains **no login credentials**.
- `POST /api/v1/checkout/orders` keeps guest checkout. With CustomerBearer,
  validates customer session, binds `CustomerUserId`, and rejects a different
  checkout email. The server still independently validates prices, stock and
  fulfillment inputs. An authenticated cart is not implicitly cleared by creating
  an unpaid order; FE should clear accepted items only after its intended checkout state.
- Existing/guest orders are not retroactively linked by email.

## Leaderboard

`GET /api/v1/leaderboard` returns `podium`, `weekly`, `monthly`,
`currency=IDR`, `timeZone=Asia/Jakarta`, and period start timestamps.
Weekly period starts Monday 00:00 WIB; monthly starts day 1 00:00 WIB.
Ranked by `Subtotal - DiscountAmount` of paid, noncancelled/nonrefunded
account orders with `PaidAt` within the period; **shipping is excluded**.
Only active verified accounts qualify; guests are excluded. Amount descending,
then customer ID for deterministic ties. Top 10 with masked first names,
never raw emails or complete identities. Empty periods return empty arrays.
Since the scope is **top spenders**, FE must not retain misleading heading
"Most Purchased Items".

## Search and product cards

- `GET /api/v1/search/suggestions?q=...`: fewer than 3 chars returns empty;
  grouped product-kind results (max 20), total matches and up to 5 suggestions.
  `GET /api/v1/catalog/products?q=...` continues to support full paginated results.
  CMS-curated popular games are not represented as measured "most searched".
- Public catalog list/detail and homepage product cards include `soldQuantity`,
  nullable `rating`, `reviewCount` and nullable `accountDetails`.
  Sold quantity sums paid noncancelled/nonrefunded order item quantities, including
  guest purchases. Rating is the average of approved ratings **from completed,
  paid purchases tied to the reviewing account**, rounded to one decimal.
  No approved reviews = `rating: null`; never display an invented 4.9.
- `PUT /api/v1/cms/catalog/products/{productId}/game-account-details`
  manages rank, skin count, region, level, extra info. Only GameAccount products.
  These appear on public detail and homepage cards; availability is determined
  from real variant stock/visibility. Unique game account listings require exactly one stock unit; quantity greater than one or unlimited stock is rejected by CMS, cart and checkout.
- `POST /api/v1/me/reviews` requires purchased order item; one review per
  order item. New reviews are unapproved. CMS lists/moderates under
  `/api/v1/cms/catalog/reviews`; only approved reviews contribute to rating.
- Article list/detail include estimated `readTimeMinutes` based on content
  length (approximately 1000 characters per minute, minimum 1).

**Remaining FE integration:** Existing Hero, ServiceCategories, LatestArticles,
GameAccounts, Footer and Leaderboard currently have static/fallback content.
FE needs to consume the documented APIs and show truthful empty states.
The client-owned email provider is intentionally not configured here.
