# Running ImmichFrame internet-facing

ImmichFrame terminates plain HTTP on port `8080`. For an internet-facing deployment
(e.g. `frame.gadgetusaf.com`) put it behind a reverse proxy that terminates TLS and
forwards the standard `X-Forwarded-*` headers. ImmichFrame already honors those
(`UseForwardedHeaders`), so cookies are marked `Secure` and links resolve correctly.

## Hardening checklist

- **Lock the global content API — this is the big one.** By default the global `/api/*`
  endpoints (your **entire** photo library, plus weather/calendar) are reachable by **anyone
  who can hit the host, with no login or PIN**. The per-link PIN/viewer gates only protect
  `/[slug]` slideshow links — they do **not** cover the global library. If you only intend to
  publish `/[slug]` links, set **`IMMICHFRAME_REQUIRE_SECRET=true`** (or configure an
  `AuthenticationSecret`): the global `/api/*` endpoints then return `401` while your `/[slug]`
  links keep working. This is the difference between *"only my PIN/viewer links are public"*
  and *"my whole library is public,"* so decide deliberately before exposing the host.
- **Bootstrap the admin before exposing it.** Set `ADMIN_USERNAME` / `ADMIN_PASSWORD`
  so the first-run setup screen is never reachable by the public. (After first start
  the setup endpoint returns 409, but bootstrapping closes the window entirely.)
- **Persist the Config volume.** It holds the SQLite database *and* the Data Protection
  keys that decrypt your stored API keys. Losing it means re-entering everything.
- **Tell ImmichFrame which proxy to trust.** Set `IMMICHFRAME_TRUSTED_PROXIES` to the
  reverse proxy's own IP(s)/CIDR network(s) (comma-separated, e.g. the Docker bridge
  subnet `172.16.0.0/12`). Only then are `X-Forwarded-For` / `-Proto` honoured, so the
  per-IP rate limit and HTTPS-cookie detection see the **real client IP**. Without it,
  ImmichFrame ignores forwarded headers (so it can't be spoofed) — but every visitor then
  looks like the proxy and shares **one** rate-limit bucket, so a single attacker's failed
  logins can lock everyone out. Leave it unset only when the container is exposed directly.
- **Built-in protections** (already on): API keys encrypted at rest; admin/viewer login,
  first-run setup, and slideshow PIN unlock are rate-limited per client IP (10 attempts /
  5 min → HTTP 429, using the real client IP once `IMMICHFRAME_TRUSTED_PROXIES` is set);
  baseline security headers (CSP, `X-Frame-Options`, `nosniff`, `Referrer-Policy`,
  `Permissions-Policy`, and HSTS when the request is HTTPS).
- **Per-link access:** prefer **viewer login** or a strong **PIN** for anything private.
  PINs are inherently low-entropy — rate limiting slows brute force but use viewer
  accounts for sensitive links.

## Caddy (simplest — automatic TLS)

`Caddyfile`:

```caddyfile
frame.gadgetusaf.com {
    reverse_proxy immichframe:8080
}
```

Caddy obtains/renews certificates automatically and sets `X-Forwarded-Proto`/`-For`.

`docker-compose.yml`:

```yaml
name: immichframe
services:
  immichframe:
    image: ghcr.io/gadgetusaf/immichframe:latest   # your fork's image
    restart: unless-stopped
    environment:
      ADMIN_USERNAME: admin
      ADMIN_PASSWORD: ${IMMICHFRAME_ADMIN_PASSWORD}
      # Trust X-Forwarded-* only from the proxy. Use your compose network's subnet.
      IMMICHFRAME_TRUSTED_PROXIES: "172.16.0.0/12"
      TZ: "America/New_York"
    volumes:
      - ./config:/app/Config        # SQLite DB + Data Protection keys live here
    expose:
      - "8080"

  caddy:
    image: caddy:2
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile
      - caddy_data:/data
      - caddy_config:/config

volumes:
  caddy_data:
  caddy_config:
```

## Traefik (compose labels)

```yaml
services:
  immichframe:
    image: ghcr.io/gadgetusaf/immichframe:latest
    restart: unless-stopped
    environment:
      ADMIN_USERNAME: admin
      ADMIN_PASSWORD: ${IMMICHFRAME_ADMIN_PASSWORD}
      IMMICHFRAME_TRUSTED_PROXIES: "172.16.0.0/12"   # the proxy/compose network
    volumes:
      - ./config:/app/Config
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.immichframe.rule=Host(`frame.gadgetusaf.com`)"
      - "traefik.http.routers.immichframe.entrypoints=websecure"
      - "traefik.http.routers.immichframe.tls.certresolver=le"
      - "traefik.http.services.immichframe.loadbalancer.server.port=8080"
```

Ensure Traefik forwards proto headers (it does by default for the `websecure`
entrypoint with TLS).

## nginx (snippet)

```nginx
server {
    listen 443 ssl;
    server_name frame.gadgetusaf.com;
    # ssl_certificate / ssl_certificate_key ...

    location / {
        proxy_pass http://immichframe:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```
