FROM mcr.microsoft.com/dotnet/core/sdk:3.0 as build

RUN curl -LSs -o /usr/local/bin/gosu -SL "https://github.com/tianon/gosu/releases/download/1.4/gosu-$(dpkg --print-architecture)" \
    && chmod +x /usr/local/bin/gosu

ARG TOOLBELT_VERSION="20190924.093427.d1d4ab159e"

# Mount a directory which contains the oauth2 token
VOLUME /var/spatial_oauth

# Copy spatial CLI into container
WORKDIR /build/tools/
ADD "https://console.improbable.io/toolbelt/download/${TOOLBELT_VERSION}/linux" ./spatial
RUN ["chmod", "+x", "./spatial"]
ENV PATH "$PATH:/build/tools/"

COPY ci/docker/entrypoint.sh ./entrypoint.sh
ENTRYPOINT ["./entrypoint.sh"]
