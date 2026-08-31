# Zetruv Backend

Backend API for Zetruv.

## Current stack

- .NET 10 LTS / ASP.NET Core Web API
- PostgreSQL
- Entity Framework Core
- Npgsql
- JWT authentication for the React CMS
- Docker / Docker Compose

The codebase starts as a modular monolith. Features are grouped by domain so catalog, orders, payments, joki, game accounts, merchandise, articles, users, and leaderboard can evolve independently without creating deployment complexity too early.

## First vertical slice: Homepage CMS

Based on the approved homepage design, the first slice includes:

- Dynamic hero/banner content
- Hero scheduling (`startsAt`, `endsAt`), ordering, and active state
- Homepage section configuration (title, subtitle, CTA, enabled state, order, item limit)
- Public homepage endpoint
- Protected CMS endpoints
- Initial CMS admin seeding from environment variables

Product-driven sections such as Flash Sale, Popular Games, Joki, Game Accounts, Merchandise, and Articles are represented as homepage section configuration only for now. Their item data will come from the real domain tables instead of being duplicated into CMS JSON.

## API

Public:

- `GET /health`
- `GET /api/v1/homepage`

CMS:

- `POST /api/v1/admin/auth/login`
- `GET /api/v1/admin/homepage/heroes`
- `POST /api/v1/admin/homepage/heroes`
- `PUT /api/v1/admin/homepage/heroes/{id}`
- `DELETE /api/v1/admin/homepage/heroes/{id}`
- `GET /api/v1/admin/homepage/sections`
- `PUT /api/v1/admin/homepage/sections/{key}`

CMS routes require a Bearer token returned by the admin login endpoint.

## Local run with Docker

```bash
cp .env.example .env
# edit secrets in .env

docker compose up --build
```

API: `http://localhost:8080`

The app applies EF Core migrations on startup.

## Environment variables

At minimum for staging/production:

- `ConnectionStrings__Postgres`
- `Jwt__Key` (32+ random characters)
- `CmsAdmin__Email`
- `CmsAdmin__Password`
- `Cors__AllowedOrigins__0` (public frontend)
- `Cors__AllowedOrigins__1` (CMS frontend)

Do not commit real credentials.
