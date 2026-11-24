#!/usr/bin/env bash
# Pulls the latest images and (re)starts the production stack. Runs migrations explicitly
# before swapping traffic over, per ADR 0007 (migrations are never automatic on startup).
#
# Run from the repository root on the server, with infra/env/prod.env already in place.
# CI calls this over SSH after a successful build; it can also be run by hand.

set -euo pipefail

cd "$(dirname "$0")/../.."

COMPOSE="docker compose --env-file infra/env/prod.env -f infra/docker/docker-compose.prod.yml"

echo "== Pulling images =="
$COMPOSE pull api worker

echo "== Starting dependencies =="
$COMPOSE up -d db redis rabbitmq
$COMPOSE up -d --wait db redis rabbitmq

echo "== Applying database migrations =="
$COMPOSE run --rm api --migrate

echo "== Rolling out api, worker and nginx =="
$COMPOSE up -d api worker nginx certbot

echo "== Pruning old images =="
docker image prune -f

echo "== Current status =="
$COMPOSE ps
