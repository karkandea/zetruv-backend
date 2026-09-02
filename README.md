# Zetruv Backend

Backend API for Zetruv.

## Current stack

- .NET 10 LTS / ASP.NET Core Web API
- PostgreSQL
- Entity Framework Core + Npgsql
- JWT authentication for the React CMS
- Docker / Docker Compose

The codebase is a modular monolith. Features are grouped by domain so catalog, orders, payments, joki, game accounts, merchandise, articles, users, integrations, and leaderboard can evolve independently without creating deployment complexity too early.

## Product ownership

Zetruv owns and manages its catalog. Products, prices, variants, images, stock, and merchandising flags are entered by Zetruv admins through the CMS API.

Third-party services stay behind integration boundaries. Examples planned for later slices:

- Shipping/rates/tracking for physical merchandise (for example RajaOngkir)
- Game-account validation for products that require user ID / zone / username verification

Those providers do not own the Zetruv product schema.

## Implemented slices

Homepage / catalog:

- Dynamic hero/banner content with scheduling and ordering
- Homepage section configuration
- Admin-owned categories, games, products, variants, and images
- Product kinds: `TopUpGame`, `TopUpLogin`, `GameVoucher`, `Joki`, `Merchandise`, `GameAccount`
- Variant price, compare-at price, optional stock, and optional physical weight
- Product-level `requiresGameAccountValidation` flag
- Popular games, featured products, and scheduled Flash Sale promotions
- Homepage service categories, Flash Sale, Popular Games, Joki, Game Accounts, and Merchandise use real catalog data

Orders / transactions:

- Order and immutable order-item snapshots for historical purchase data
- Order status: `Pending`, `Processing`, `Completed`, `Cancelled`
- Payment status: `Pending`, `Paid`, `Failed`, `Refunded`
- Payment transaction records separated from provider implementation
- Admin order list/detail and status management
- Homepage `RecentlyPurchased` is populated only from completed + paid orders
- Public recent-purchase data intentionally excludes customer identity/contact details

Checkout:

- Guest checkout creates a pending order from product variant IDs and quantities
- Product name, slug, SKU, game, image, kind, and charged price are snapshotted into `OrderItem`
- Price is always resolved server-side; the frontend never submits a trusted amount
- Active Flash Sale pricing is applied automatically and reflected in `discountAmount`
- Inactive products/categories/games/variants and insufficient stock are rejected
- Checkout currently requires at least customer email or phone
- Shipping is currently `0` until the shipping-provider slice is implemented
- Stock is validated but not reserved/decremented yet; inventory reservation belongs with the payment/fulfillment slice

Articles:

- Article categories
- Article CRUD with draft/published state
- Scheduled `publishedAt`
- Thumbnail, excerpt, author, content, featured flag, and unique slug
- Public article list with category/search/pagination
- Homepage `LatestArticles` uses published article data

Site / footer:

- Dynamic logo, brand description, copyright text
- Dynamic floating contact-team CTA
- Footer links grouped as `Page`, `Support`, or `Legality`
- Social links
- Payment methods and icon URLs

## Public API

- `GET /health`
- `GET /api/v1/homepage`
- `GET /api/v1/catalog/categories`
- `GET /api/v1/catalog/games`
- `GET /api/v1/catalog/products`
- `GET /api/v1/catalog/products/{slug}`
- `GET /api/v1/catalog/flash-sale`
- `POST /api/v1/checkout/orders`
- `GET /api/v1/articles/categories`
- `GET /api/v1/articles`
- `GET /api/v1/articles/{slug}`
- `GET /api/v1/site/footer`

`POST /api/v1/checkout/orders` accepts customer contact data plus variant IDs and quantities. All totals are recalculated from the current server-side catalog and active Flash Sale data.

## Admin / CMS API

Authentication:

- `POST /api/v1/admin/auth/login`

Homepage:

- `GET|POST /api/v1/admin/homepage/heroes`
- `PUT|DELETE /api/v1/admin/homepage/heroes/{id}`
- `GET /api/v1/admin/homepage/sections`
- `PUT /api/v1/admin/homepage/sections/{key}`

Catalog / promotion:

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

Orders / transactions:

- `GET /api/v1/cms/orders`
- `GET /api/v1/cms/orders/{id}`
- `PUT /api/v1/cms/orders/{id}/status`
- `PUT /api/v1/cms/orders/{id}/payment-status`

Articles:

- `GET|POST /api/v1/cms/articles/categories`
- `PUT|DELETE /api/v1/cms/articles/categories/{id}`
- `GET|POST /api/v1/cms/articles`
- `GET|PUT|DELETE /api/v1/cms/articles/{id}`

Site / footer:

- `GET|PUT /api/v1/cms/site/settings`
- `GET|POST /api/v1/cms/site/footer-links`
- `PUT|DELETE /api/v1/cms/site/footer-links/{id}`
- `GET|POST /api/v1/cms/site/social-links`
- `PUT|DELETE /api/v1/cms/site/social-links/{id}`
- `GET|POST /api/v1/cms/site/payment-methods`
- `PUT|DELETE /api/v1/cms/site/payment-methods/{id}`

CMS routes require a Bearer token returned by the admin login endpoint.

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

Do not commit real credentials.
