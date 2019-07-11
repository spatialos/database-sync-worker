FROM microsoft/dotnet:2.2-sdk as build
ARG TOOLBELT_VERSION="20190516.083918.6882deba56"
WORKDIR /build

# Mount a directory which contains the oauth2 token
VOLUME /var/spatial_oauth

# Copy spatial CLI & proxy script into container
WORKDIR /build/tools/
ADD "https://console.improbable.io/toolbelt/download/${TOOLBELT_VERSION}/linux" ./real_spatial
RUN ["chmod", "+x", "./real_spatial"]
COPY ci/docker/spatial ./spatial
ENV PATH "$PATH:/build/tools/"

# Copy database worker across
WORKDIR /build/
COPY ./ ./worker/

WORKDIR /build/worker
ENTRYPOINT ["tail", "-f", "/dev/null"]
