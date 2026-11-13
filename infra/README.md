# Infra

Everything needed to build, ship and run the backend outside local development.

| Path | Purpose |
|---|---|
| `docker/docker-compose.dev.yml` | Local dependency stack only (db, redis, rabbitmq). API/Worker run on the host. |
| `docker/docker-compose.prod.yml` | Full production stack: api, worker, db, redis, rabbitmq, nginx, certbot. |
| `env/dev.env.example` | Template for the local dependency stack's secrets. |
| `env/prod.env.example` | Template for the production stack's secrets. |
| `nginx/nginx.conf` | Base Nginx config (logging, includes `conf.d/`). |
| `nginx/conf.d/bandup.conf.template` | Reverse-proxy server block; copy to `bandup.conf` and fill in the domain. |
| `scripts/setup-server.sh` | One-time VPS setup: sudo user, key-only SSH, Docker, UFW. |
| `scripts/deploy.sh` | Pulls images, migrates, rolls out the stack. Used by CD and by hand. |

## One-time server setup

From your machine, with root access to a fresh Ubuntu/Debian VPS:

```bash
scp infra/scripts/setup-server.sh root@<server-ip>:/root/
ssh root@<server-ip> './setup-server.sh deploy ~deploy/.ssh/id_ed25519.pub'
```

Pass the deploy user's **public** key, generated beforehand with `ssh-keygen -t ed25519`. The
script creates a sudo user with key-only SSH, installs Docker + Compose, sets the timezone to
UTC, and opens only SSH/80/443 through UFW. It is safe to re-run.

Then, as the deploy user:

```bash
git clone git@github.com:Ilya-Jahed/bandup-backend.git /srv/bandup
cd /srv/bandup
cp infra/env/prod.env.example infra/env/prod.env   # fill in every CHANGE_ME
cp infra/nginx/conf.d/bandup.conf.template infra/nginx/conf.d/bandup.conf   # fill in ${DOMAIN}
```

## First HTTPS certificate

Nginx's HTTPS server block needs a certificate before it can start, so the first issuance is
a one-off, before the stack is up:

```bash
docker compose -f infra/docker/docker-compose.prod.yml run --rm \
  -p 80:80 certbot certonly --standalone -d <your-domain>
```

After that, the `certbot` service in the compose file renews automatically every 12 hours, and
Nginx already points at the right certificate path.

## Deploying

CI builds and tests every push and pull request to `main` ([`.github/workflows/ci.yml`](../.github/workflows/ci.yml)).
CD ([`.github/workflows/cd.yml`](../.github/workflows/cd.yml)) runs only after CI succeeds on `main`: it builds the
api/worker images, pushes them to GHCR, then SSHes into the server and runs `deploy.sh`.

Required repository secrets (Settings → Secrets and variables → Actions), plus a `production`
environment if you want a manual approval gate before deploy:

| Secret | Value |
|---|---|
| `DEPLOY_HOST` | server IP or hostname |
| `DEPLOY_USER` | the deploy user created by `setup-server.sh` |
| `DEPLOY_SSH_KEY` | the deploy user's **private** key |
| `DEPLOY_PATH` | `/srv/bandup` (or wherever the repo was cloned) |

Required branch protection (Settings → Branches → `main`): require the CI `build-and-test`
check to pass and require a pull-request review before merging. This is a one-time manual
setting; GitHub does not let a workflow file configure it.

To deploy by hand instead of through CD:

```bash
ssh deploy@<server-ip>
cd /srv/bandup
./infra/scripts/deploy.sh
```

## Rollback

`deploy.sh` always migrates the database **before** switching the api/worker containers to the
new image, and a failed `docker compose pull` or `--migrate` run stops the script before
anything is touched — the previous containers keep running untouched. To roll back on purpose,
re-run CD for an older green commit (via *Actions → CD → Run workflow*, or by re-pushing that
commit), or run `deploy.sh` by hand with `API_IMAGE`/`WORKER_IMAGE` pinned to an older tag:

```bash
API_IMAGE=ghcr.io/OWNER/bandup-backend-api:<old-sha> \
WORKER_IMAGE=ghcr.io/OWNER/bandup-backend-worker:<old-sha> \
  ./infra/scripts/deploy.sh
```

## What is deliberately not here

A Kubernetes setup, blue/green or canary deploys, and a secrets manager — the single-VPS
Docker Compose stack above is the right size for this project. Revisit only if real load or a
second environment demands it.
