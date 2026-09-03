# Nginx deployment notes

The legacy `zetruv-api.conf` snippet is used by the original single-stack deployment.

For the new non-production environment layout, use `scripts/install-runtime-nginx.sh` instead. It owns two independent server blocks:

- `/etc/nginx/sites-available/zetruv-api-dev` -> `api-dev.zetruv.dualangka.com` -> `127.0.0.1:8081`
- `/etc/nginx/sites-available/zetruv-api-staging` -> `api-staging.zetruv.dualangka.com` -> `127.0.0.1:8082`

Do not point both domains at port 8080 and do not share a vhost between DEV and STAGING. The environment installer validates DNS, local backend health, Nginx syntax, and HTTPS when Certbot is installed.
