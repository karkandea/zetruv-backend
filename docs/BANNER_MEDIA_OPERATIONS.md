# Zetruv CMS banner media — DEV rollout

## Contract and storage

- `POST /api/v1/cms/media/banner` accepts one multipart `file`, protected by the CMS admin policy.
- `GET /api/v1/cms/media/banner/capabilities` is used by the frontend to show the uploader only after the backend is available.
- The server checks actual PNG/JPG/WebP bytes, decodes the image, rejects invalid/oversized dimensions, and re-encodes it to WebP before storage. Input limit: 3 MiB; output target: 500 KiB; maximum 4096 px on either edge and 8 megapixels.
- `MediaAssets` holds only metadata and a `banners/yyyy/MM/{guid}.webp` storage key. No image blobs are saved in PostgreSQL.
- Maximum 50 **banner records** includes disabled banners. The image storage quota is 100 MiB per environment. The number of image files may temporarily be greater than 50 during replacements.
- `PUT /api/v1/cms/homepage/heroes/reorder` and `/sections/reorder` update ordering in one transaction, without rewriting banner or section content.
- The new CMS editor only uses stored uploads. Existing REST clients keep support for syntactically valid HTTPS or local image paths, while malformed strings (e.g. `asofdjtogjesf`) cannot be published. Stored banner assets are checked against the database. Client-side load errors also prevent a broken image from being saved as active.
- `DELETE /api/v1/cms/media/{id}` refuses to remove an uploaded banner image still referenced by any saved banner (HTTP 409).

## DEV/PROD separation

The existing Compose `media_data:/app/media` volume is persistent and lives outside container layers. The Compose project name isolates `zetruv-dev_media_data` and `zetruv-prod_media_data`; verify actual names with `docker volume ls` before deploying. Never mount the same host media folder into both environments.

The backend container serves `/media/` from this directory. Keep `Media__LocalPath=/app/media` and the volume mount through rebuilds. Admin upload starts only after `GET /cms/media/banner/capabilities` succeeds.

Nginx has a separate 4 MiB client limit for the banner upload route; existing API/media uploads retain their previous limits. The API also applies a 4 MiB request limit and the stricter 3 MiB file check.

## Cleanup and backup

1. When replacing a banner, save the new image first and verify the storefront. Keep the prior file until backup or retention policies permit removal.
2. Identify old banner assets by `StorageKey` with prefix `banners/`. Check references from `HomeHeroes.ImageUrl` before calling authenticated `DELETE /cms/media/{id}`. The backend rejects deletion while referenced. **Never delete volume files directly** or run generic `docker volume prune`.
3. Back up both the PostgreSQL database and the corresponding environment's `media_data` volume in one maintenance window. Verify restore to a disposable environment before considering backups complete.
4. Avoid automatic deletion during a deployment. Schedule a separately audited, aged-orphan cleanup job if volume usage warrants it; it must recheck DB references immediately before every delete.

There is deliberately no automatic purge of old media in this change: an image can still be referenced by an active banner or awaiting editorial review. Deletion is guarded and explicit.
