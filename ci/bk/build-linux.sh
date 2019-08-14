#!/usr/bin/env bash

set -euo pipefail
[[ -n "${DEBUG:-}" ]] && set -x

cd "$(dirname "$0")/../../"

source "ci/pinned-tools.sh"

startDockerCompose ./ci/docker/docker-compose.yml

dc exec -T dotnet gosu user /bin/bash -c "./scripts/build-nuget-packages.sh"
dc exec -T dotnet gosu user /bin/bash -c "dotnet build ./Workers.sln -p:Platform=x64"
dc exec -T dotnet gosu user /bin/bash -c "./scripts/publish-windows-workers.sh"
dc exec -T dotnet gosu user /bin/bash -c "./scripts/publish-osx-workers.sh"
dc exec -T dotnet gosu user /bin/bash -c "./scripts/publish-linux-workers.sh"
