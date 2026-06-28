# DuckDNS + Caddy deployment

This deployment exposes the application only through Caddy on ports 80 and
443. The Next.js frontend remains on the private Docker network, and Caddy
obtains and renews its TLS certificate automatically.

## 1. Register the DuckDNS hostname

1. Sign in to DuckDNS.
2. Create `smautomate.duckdns.org` (or another available hostname).
3. Set its IPv4 address to `67.202.24.212`.

If a different hostname is used, create a `.env` file beside
`docker-compose.yml` on the server:

```dotenv
APP_DOMAIN=your-name.duckdns.org
```

Do not commit the DuckDNS token. A token is only needed to update DuckDNS; it
is not needed by Caddy when the server has a static public IP.

## 2. Open the required inbound ports

Allow TCP 80 and TCP 443 in both the cloud firewall/security group and the
server firewall. UDP 443 is optional and enables HTTP/3.

The public firewall should not expose ports 3000, 5001, 5002, 5003, or 5432.
Docker Compose also binds the API and database maintenance ports to localhost
only.

## 3. Deploy

From the repository root on the server:

```bash
docker compose config
docker compose pull caddy
docker compose up -d --build --remove-orphans
docker compose logs -f caddy
```

Caddy can obtain a certificate only after the DuckDNS record resolves to the
server and ports 80/443 reach this Compose stack.

## 4. Verify

```bash
curl -I http://smautomate.duckdns.org
curl -I https://smautomate.duckdns.org
docker compose logs --tail=100 caddy
```

The HTTP request should redirect to HTTPS, the HTTPS response should have a
valid public certificate, and the browser URL should not include `:3000`.

## Meta webhook URL

Once the webhook endpoint is implemented, use a callback URL under the same
origin, for example:

```text
https://smautomate.duckdns.org/api/smapi/webhooks/meta
```

The current codebase does not yet contain a Meta webhook verification route,
so this URL will not verify until the GET challenge/verify-token handler is
added.
