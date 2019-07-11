#!/usr/bin/env bash

set -euo pipefail
[[ -n "${DEBUG:-}" ]] && set -x
cd "$(dirname "$0")/../../"

source "ci/pinned-tools.sh"

startDockerCompose ./ci/docker/docker-compose.yml

dc exec -T dotnet /bin/bash -c "./ci/test.sh"
dc exec -T dotnet /bin/bash -c "./scripts/init-database.sh && ./scripts/create-snapshot.sh"
