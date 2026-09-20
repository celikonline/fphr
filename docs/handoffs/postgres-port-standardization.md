# Postgres/Docker port standardization (senseik-032) — already merged, verified only

## Finding

The work senseik-032 asked for — reapplying the `chore/postgres-default-port-15432` port
pattern (standard port + 10000: Postgres 15432, Redis 16379, MinIO 19000/19001, Mailpit
11025/18025, RabbitMQ 15672/25672, Seq 15341, API 18080, Web 13000, Jaeger 26686, OTLP
gRPC/HTTP 14317/14318, Prometheus 19090, Loki 13100, Grafana 13001) against the current
`dev` branch — is **already fully present on `dev`**. No reapplication was needed.

The card's `source` note ("133 commit geride kaldığı için doğrudan merge edilmedi") was
stale: `chore/postgres-default-port-15432` was merged via PR #70, then a follow-up branch
`a/docker-ports-standardization` (commit `045579b`, "port kalıbı — standart port + 10000
offset tüm docker-compose'a uygulandı") merged via **PR #71** into `dev`. Both merges are
ancestors of the current `dev` tip (`0d7261f`, PR #73):

```
git branch --contains 045579b   # includes: dev
git log dev --oneline --merges | grep -i port
# fe459e6 Merge pull request #71 from algosense-yazilim/a/docker-ports-standardization
# 47b058a Merge pull request #70 from algosense-yazilim/chore/postgres-default-port-15432
```

The later `5a96eb1 fix(worker,documents,docker): repair docker-compose full-stack run`
commit landed **after** `045579b` in `dev`'s history, on top of the already-standardized
ports — so there is no conflict to reconcile; the repair fix is already compatible with
the new port scheme in the current `docker-compose.yml` / `infra/docker-compose.yml`.

## Verification performed (this session)

1. **`.env.example`** — confirmed every variable listed in the card's acceptance criteria
   is present with the `+10000` default (`POSTGRES_PORT=15432`, `REDIS_PORT=16379`,
   `MINIO_PORT=19000`, `MINIO_CONSOLE_PORT=19001`, `MAILPIT_SMTP_PORT=11025`,
   `MAILPIT_UI_PORT=18025`, `RABBITMQ_PORT=15672`, `RABBITMQ_UI_PORT=25672`,
   `SEQ_PORT=15341`, `JAEGER_UI_PORT=26686`, `OTLP_GRPC_PORT=14317`,
   `OTLP_HTTP_PORT=14318`, `PROMETHEUS_PORT=19090`, `LOKI_PORT=13100`,
   `GRAFANA_PORT=13001`, `API_PORT=18080`, `WEB_PORT=13000`).
2. **`docker-compose.yml`** (root) and **`infra/docker-compose.yml`** — every `ports:`
   mapping uses `${VAR:-<+10000 default>}:<container-internal port>`; container-internal
   ports are unchanged (only the host side shifted), matching the documented rationale.
3. **`docs/ci-cd.md`** / **`docs/observability.md`** — URLs already reference the new
   host ports (API `18080`, Web `13000`, Grafana `13001`, Prometheus `19090`, Jaeger
   `26686`, Loki `13100`, OTLP gRPC `14317`).
4. **MinIO presigned URL public endpoint** — confirmed the signing path:
   - `docker-compose.yml` sets `MinIO__PublicEndpoint: localhost:${MINIO_PORT:-19000}`
     on the `api` service (container-internal `MinIO__Endpoint` stays `minio:9000`).
   - `src/Shared/Sense.Shared.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
     registers a keyed `IMinioClient` (`MinioAdapter.PublicClientKey = "minio-public"`)
     whose endpoint is `options.PublicEndpoint` (falling back to `options.Endpoint` when
     unset).
   - `src/Shared/Sense.Shared.Infrastructure/Storage/MinioAdapter.cs` uses that keyed
     `_publicClient` (not the internal `_client`) to generate both upload
     (`PresignedPutObjectAsync`) and download (`PresignedGetObjectAsync`) URLs.
   - Net effect: presigned URLs handed to the browser/mobile client are signed against
     `localhost:19000`, which is reachable from outside the compose network — correct.
5. **`docker compose config --quiet`** on both `docker-compose.yml` and
   `infra/docker-compose.yml` — both pass (valid syntax, no unresolved interpolation
   errors).
6. **Live verification** (this host has a usable Docker Desktop): the full stack was
   already running from an earlier session —
   `senseik-api` (18080), `senseik-web` (13000), `senseik-redis` (16379),
   `senseik-mailpit` (11025/18025), `senseik-minio` (19000/19001) were all `Up ~5h
   (healthy)`. `senseik-postgres` was recreated by `docker compose -f
   infra/docker-compose.yml up -d postgres redis minio` (config drift vs. the running
   container, e.g. healthcheck/image), came back `Up ... (healthy)` on `15432`, and
   `pg_isready` succeeded — data intact via the named `senseik_pg` volume. This is a live,
   end-to-end confirmation that the new port scheme works, not just a syntax check.

## Outcome

No code/doc changes were made — the working tree has no diff against `dev`. Branch
`a/postgres-default-port-15432-reapply` was created per the task instructions but carries
only this handoff commit.

## Follow-up for the board

`docs/agent-board/cards/senseik-032-postgres-port-standardization.json` (still untracked
in the main checkout, not on any branch) should be marked done/closed rather than
`backlog` — the acceptance criteria are already satisfied on `dev`. That file lives
outside this worktree's tracked tree, so it wasn't edited here; flagging it for whoever
owns the board update.
