# Zetruv Backend

Backend API for Zetruv.

## Current stack

- .NET 10 LTS / ASP.NET Core Web API
- PostgreSQL
- Entity Framework Core + Npgsql
- JWT authentication for the React CMS
- Docker / Docker Compose

The codebase is a modular monolith. Features are grouped by domain so catalog, orders, payments, game-account validation, joki, merchandise, articles, integrations, and future fulfillment providers can evolve independently without creating deployment complexity too early.

## Product ownership

Zetruv owns and manages its catalog. Products, prices, variants, images, stock, and merchandising flags are entered by Zetruv admins through the CMS API.

Third-party services stay behind provider boundaries. Payment, game-account validation, and later shipping/fulfillment providers do not own the Zetruv product or order schema.

## Implemented slices

### Homepage / catalog

- Dynamic hero/banner content with scheduling and ordering
- Homepage section configuration
- Admin-owned categories, games, products, variants, and images
- Product kinds: `TopUpGame`, `TopUpLogin`, `GameVoucher`, `Joki`, `Merchandise`, `GameAccount`
- Variant price, compare-at price, optional stock, and optional physical weight
- Product-level `requiresGameAccountValidation` flag
- Popular games, featured products, scheduled Flash Sale promotions
- Homepage service categories, Flash Sale, Popular Games, Joki, Game Accounts, Merchandise, latest articles, and recent purchases use persisted data

### Orders / checkout

- Guest checkout creates pending orders from product variants and quantities
- Product name, slug, SKU, game, image, kind, and charged price are snapshotted into `OrderItem`
- Prices and active Flash Sale discounts are always resolved server-side
- Inactive catalog data and insufficient stock are rejected
- Checkout requires at least customer email or phone
- Public order lookup powers the `Cek Pesanan` flow using order number plus matching email/phone
- Order lookup does not echo customer contact data and is rate-limited

### Payments / inventory

- Payment integration is behind `IPaymentGateway`
- Payment initiation always uses the persisted order total
- Verified webhook/reconciliation flow supports pending, paid, failed, and refunded states
- Bounded stock is reserved when payment starts, consumed when payment succeeds, and released when payment fails/cancels/expires
- Nullable stock remains unlimited/non-stock-tracked
- Expired reservations are cleaned up in the background
- A `mock` payment gateway is available for development/staging

### Game-account validation

- Validation integration is behind `IGameAccountValidator`
- `POST /api/v1/game-account/validate` accepts provider-agnostic account fields for products that require validation
- Successful validations are short-lived and single-use per checkout line
- Checkout rejects missing, expired, already-used, or wrong-product validation IDs
- Validation input is persisted for later fulfillment, while password/OTP/token/secret-like fields are explicitly rejected
- A `mock` validator is available for development/staging; the production vendor adapter remains separate

### Articles / site

- Article categories and article CRUD with draft/published scheduling
- Public article list/detail and homepage latest articles
- Dynamic site logo, brand description, contact CTA, footer links, social links, and payment methods

## Public API

- `GET /health`
- `GET /api/v1/homepage`
- `GET /api/v1/catalog/categories`
- `GET /api/v1/catalog/games`
- `GET /api/v1/catalog/products`
- `GET /api/v1/catalog/products/{slug}`
- `GET /api/v1/catalog/flash-sale`
- `POST /api/v1/game-account/validate`
- `POST /api/v1/checkout/orders`
- `POST /api/v1/checkout/orders/{orderId}/payment`
- `POST /api/v1/payments/webhooks/{provider}`
- `POST /api/v1/orders/lookup`
- `GET /api/v1/articles/categories`
- `GET /api/v1/articles`
- `GET /api/v1/articles/{slug}`
- `GET /api/v1/site/footer`

For a checkout item whose product has `requiresGameAccountValidation=true`, call the validation endpoint first and pass the returned `validationId` as that checkout line's `gameAccountValidationId`.

## CMS API

Canonical CMS prefix: `/api/v1/cms`.

Authentication:

- `POST /api/v1/cms/auth/login`

Homepage:

- `GET|POST /api/v1/cms/homepage/heroes`
- `PUT|DELETE /api/v1/cms/homepage/heroes/{id}`
- `GET /api/v1/cms/homepage/sections`
- `PUT /api/v1/cms/homepage/sections/{key}`

Catalog / promotions:

- `GET|POST /api/v1/cms/catalog/categories`
- `PUT|DELETE /api/v1/cms/catalog/categories/{id}`
- `GET|POST /api/v1/cms/catalog/games`
- `PUT|DELETE /api/v1/cms/catalog/games/{id}`
- `GET|POST /api/v1/cms/catalog/products`
- `GET|PUT|DELETE /api/v1/cms/catalog/products/{id}`
- `POST /api/v1/cms/catalog/products/{productId}/variants`
- `PUT|DELETE /api/v1/cms/catalog/products/{productId}/variants/{variantId}`
- `POST /api/v1/cms/catalog/products/{productId}/images`
- `PUT|DELETE /api/v1/cms/catalog/products/{productId}/images/{imageId}`
- `GET|POST /api/v1/cms/promotions`
- `PUT|DELETE /api/v1/cms/promotions/{id}`

Orders:

- `GET /api/v1/cms/orders`
- `GET /api/v1/cms/orders/{id}`
- `PUT /api/v1/cms/orders/{id}/status`
- `PUT /api/v1/cms/orders/{id}/payment-status`

Articles / site:

- `GET|POST /api/v1/cms/articles/categories`
- `PUT|DELETE /api/v1/cms/articles/categories/{id}`
- `GET|POST /api/v1/cms/articles`
- `GET|PUT|DELETE /api/v1/cms/articles/{id}`
- `GET|PUT /api/v1/cms/site/settings`
- `GET|POST /api/v1/cms/site/footer-links`
- `PUT|DELETE /api/v1/cms/site/footer-links/{id}`
- `GET|POST /api/v1/cms/site/social-links`
- `PUT|DELETE /api/v1/cms/site/social-links/{id}`
- `GET|POST /api/v1/cms/site/payment-methods`
- `PUT|DELETE /api/v1/cms/site/payment-methods/{id}`

The legacy `/api/v1/admin/auth/...` and `/api/v1/admin/homepage/...` routes remain temporary compatibility aliases. CMS routes require the Bearer token returned by the CMS login endpoint.

## Local run with Docker

```bash
cp .env.example .env
# edit secrets in .env

docker compose up --build
```

API: `http://localhost:8080`

The app applies EF Core migrations on startup and seeds base homepage/catalog/site configuration when the database is empty.

## Environment variables

At minimum for staging/production:

- `ConnectionStrings__Postgres`
- `Jwt__Key` (32+ random characters)
- `CmsAdmin__Email`
- `CmsAdmin__Password`
- `Cors__AllowedOrigins__0` (public frontend)
- `Cors__AllowedOrigins__1` (CMS frontend)
- `Payments__Provider`
- `GameAccountValidation__Provider`

`mock` is intended only for development/staging provider configuration. Do not commit real credentials or production provider secrets.
