#!/usr/bin/env bash

declare -a EXIT_TRAPS

function onExit() {
    for i in "${!EXIT_TRAPS[@]}"; do
        ${EXIT_TRAPS[$i]};
    done
}

trap onExit INT TERM EXIT

function deleteSecret() {
    rm -rf "${SPATIAL_OAUTH_DIR}"
}

function getSecret() {
    if [[ "${BUILDKITE:-}" ]]; then
        export SPATIAL_OAUTH_DIR=$(mktemp -d)
        local SPATIAL_OAUTH_FILE="${SPATIAL_OAUTH_DIR}/oauth2_refresh_token"

        imp-ci secrets read \
            --environment="production" \
            --secret-type="spatialos-service-account" \
            --buildkite-org="improbable" \
            --secret-name="online-services-toolbelt" \
            --field="token" \
            --write-to="${SPATIAL_OAUTH_FILE}"

        EXIT_TRAPS+=(deleteSecret)
    else
        # TODO: Support Linux/MacOS?
        export SPATIAL_OAUTH_DIR="${APPDATA}/../Local/.improbable/oauth2/"
    fi
}

function startDockerCompose() {
    GIT_HASH="$(git rev-parse HEAD | cut -c1-8)"
    TIMESTAMP=$(date +"%y%m%d%H%M%S%N")

    # Create unique name given the hash and timestamp of the build for docker container/network.
    # This means that builds that run in parallel on the same machine cannot clash _unless_ they run with the same hash at the same time (sufficiently unlikely!).
    export DOCKER_COMPOSE_NAME="compose_${GIT_HASH}_${TIMESTAMP}"

    export UNIQUE_BUILD_PATH="${BUILDKITE_BUILD_ID}/${BUILDKITE_JOB_ID}"
    export DOCKER_COMPOSE_FILE="${1}"
    DOCKER_POSTGRES_NAME="${DOCKER_COMPOSE_NAME}_database_1"
    export POSTGRES_HOST="${DOCKER_POSTGRES_NAME}"
    export POSTGRES_DATABASE="items"
    export POSTGRES_USER="postgres"
    export POSTGRES_PASSWORD="DO_NOT_USE_IN_PRODUCTION"
    export PACKAGES_DIR="/tmp/${UNIQUE_BUILD_PATH}-nupkgs"
    export LOGS_DIR="/tmp/${UNIQUE_BUILD_PATH}-logs"

    echo "Creating ${PACKAGES_DIR}"
    mkdir -p "${PACKAGES_DIR}"

    echo "Creating ${LOGS_DIR}"
    mkdir -p "${LOGS_DIR}"

    EXIT_TRAPS+=(cleanUpDocker)

    # Fetch the secret and mount it in a directory pointed to by $SPATIAL_OAUTH_DIR.
    getSecret

    dc up --detach --build
}

function cleanUpDocker() {
    dc down
}

# Just for convenience
function dc() {
    docker-compose --file "${DOCKER_COMPOSE_FILE}" --project-name ${DOCKER_COMPOSE_NAME} "$@"
}
