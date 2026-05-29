# mosquitto + mosquitto-go-auth, built on the upstream Eclipse Mosquitto
# 2.0 image so the broker config and dev volumes from spec 006 keep
# working. Spec 008 PR E (ADR-0100): the plugin layer adds JWT-based
# auth + ACL evaluation so devices authenticate with Keycloak-minted
# service-account tokens instead of static passwords.
#
# Two-stage build keeps the published image small:
#   1. golang:1.23-alpine compiles the plugin (.so) from upstream.
#   2. eclipse-mosquitto:2.0 receives the .so + a config drop-in.
#
# Pinned commit: stays reproducible across CI runs; bump together
# with security advisories.

FROM golang:1.23-alpine AS plugin-builder

ARG MOSQUITTO_GO_AUTH_VERSION=2.1.0

RUN apk add --no-cache git build-base mosquitto-dev \
    && git clone --depth 1 --branch ${MOSQUITTO_GO_AUTH_VERSION} \
        https://github.com/iegomez/mosquitto-go-auth.git /src \
    && cd /src \
    && make

FROM eclipse-mosquitto:2.0

COPY --from=plugin-builder /src/go-auth.so /mosquitto/go-auth.so
