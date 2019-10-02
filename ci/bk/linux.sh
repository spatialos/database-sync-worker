#!/usr/bin/env bash

set -euo pipefail
[[ -n "${DEBUG:-}" ]] && set -x
cd "$(dirname "$0")/../../"

docker ps

docker-compose \
    --file "$1" \
    --project-name ${DOCKER_COMPOSE_NAME} \
    up \
    --build \
    --abort-on-container-exit
