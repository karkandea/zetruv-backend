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

## Homepage + catalog slice

The current slice includes:

- Dynamic hero/banner content
- Hero scheduling (`startsAt`, `endsAt`), ordering, and active state
- Homepage section configuration
- Admin-owned categories and games
- Products with variants and images
- Product kinds: `TopUpGame`, `TopUpLogin`, `GameVoucher`, `Joki`, `Merchandise`, `GameAccount`
- Variant price, compare-at price, optional stock, and optional physical weight
- Product-level `requiresGameAccountValidation` flag for the future validation integration
- Popular games and featured products
- Scheduled Flash Sale promotions with per-variant sale prices
- Homepage data sourced from the real catalog for service categories, Flash Sale, Popular Games, Joki, Game Accounts, and Merchandise

`recently_purchased` will be populated from the future Order/Transaction domain, and `articles` will be populated from the Article domain. They are intentionally not duplicated as static homepage JSON.

## Public API

- `GET /health`
- `GET /api/v1/homepage`
- `GET /api/v1/catalog/categories`
- `GET /api/v1/catalog/games`
- `GET /api/v1/catalog/products`
- `GET /api/v1/catalog/products/{slug}`
- `GET /api/v1/catalog/flash-sale`

Product listing supports filters for category, game, product kind, search text, page, and page size.

## Admin / CMS API

Authentication:

- `POST /api/v1/admin/auth/login`

Homepage:

- `GET /api/v1/admin/homepage/heroes`
- `POST /api/v1/admin/homepage/heroes`
- `PUT /api/v1/admin/homepage/heroes/{id}`
- `DELETE /api/v1/admin/homepage/heroes/{id}`
- `GET /api/v1/admin/homepage/sections`
- `PUT /api/v1/admin/homepage/sections/{key}`

Catalog:

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

CMS routes require a Bearer token returned by the admin login endpoint.

## Local run with Docker

```bash
cp .env.example .env
# edit secrets in .env

docker compose up --build
```

API: `http://localhost:8080`

The app applies EF Core migrations on startup and seeds the default homepage/catalog category configuration when the database is empty.

## Environment variables

At minimum for staging/production:

- `ConnectionStrings__Postgres`
- `Jwt__Key` (32+ random characters)
- `CmsAdmin__Email`
- `CmsAdmin__Password`
- `Cors__AllowedOrigins__0` (public frontend)
- `Cors__AllowedOrigins__1` (CMS frontend)

Do not commit real credentials.
