#!/usr/bin/env bash

set -euo pipefail
[[ -n "${DEBUG:-}" ]] && set -x
cd "$(dirname "$0")/../../"

docker ps

pushd ci/docker
    docker-compose \
        --project-name ${DOCKER_COMPOSE_NAME} \
        up \
        --build \
        --abort-on-container-exit
popd
