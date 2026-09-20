# Mosquitto + a custom Go JWT/JWKS auth plugin (spec 008 ADR-0100).
#
# Why build Mosquitto from source on Debian instead of starting FROM the
# Alpine eclipse-mosquitto image: a Go `-buildmode=c-shared` library
# can't be dlopen'd by a musl-linked Mosquitto (it fails with
# "runtime.tlsg: initial-exec TLS resolves to dynamic"). Keeping the
# broker and the plugin on the same glibc toolchain avoids that. The
# plugin links nothing from libmosquitto at build time
# (-unresolved-symbols=ignore-all); the broker provides those symbols
# when it loads the .so.

ARG MOSQUITTO_VERSION=2.0.18
# Checksum of the release tarball, moved together with the version above.
# Corroborated against Alpine aports' independently recorded sha512 for the same
# file (spec 195 §6) — a hash computed from one's own download proves only what
# arrived.
ARG MOSQUITTO_SHA256=d665fe7d0032881b1371a47f34169ee4edab67903b2cd2b4c083822823f4448a

# Base images below are pinned by digest (spec 195, issue #2296) so a floating
# tag cannot change what this build produces with no diff and no PR (#2265).
# This makes the image pinned, not reproducible: apt-get below still resolves
# against whatever deb.debian.org serves at build time — deliberately left
# unfrozen, see the follow-up filed for that decision (spec 195 §8.1).

# --- Stage 1: build Mosquitto (glibc) so we get the broker binary,
#     libmosquitto, and the plugin headers (mosquitto_broker.h is not in
#     any Debian -dev package). ---
FROM debian:bookworm-slim@sha256:3783cc01769c7b2b1b83a5c5ad96c815348e28ed7da68e2e3687004faa906251 AS mosquitto_builder
ARG MOSQUITTO_VERSION
ARG MOSQUITTO_SHA256
RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential wget ca-certificates libssl-dev libcjson-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /build
RUN wget -q "https://mosquitto.org/files/source/mosquitto-${MOSQUITTO_VERSION}.tar.gz" \
    && echo "${MOSQUITTO_SHA256}  mosquitto-${MOSQUITTO_VERSION}.tar.gz" | sha256sum -c - \
    && tar xzf "mosquitto-${MOSQUITTO_VERSION}.tar.gz"
WORKDIR /build/mosquitto-${MOSQUITTO_VERSION}
RUN make WITH_WEBSOCKETS=no WITH_DOCS=no WITH_CJSON=yes WITH_TLS=yes WITH_SRV=no -j"$(nproc)" \
    && make install WITH_WEBSOCKETS=no WITH_DOCS=no

# --- Stage 2: build the Go auth plugin as a c-shared .so against those
#     headers. ---
FROM golang:1.23-bookworm@sha256:167053a2bb901972bf2c1611f8f52c44d5fe7e762e5cab213708d82c421614db AS plugin_builder
ENV CGO_ENABLED=1 \
    CGO_CFLAGS="-fPIC" \
    CGO_LDFLAGS="-shared -Wl,-unresolved-symbols=ignore-all"
COPY --from=mosquitto_builder /usr/local/include/ /usr/local/include/
WORKDIR /plugin
COPY plugin/go.mod plugin/go.sum ./
RUN go mod download
COPY plugin/ ./
RUN go build -buildmode=c-shared -o /sse-jwt-auth.so .

# --- Stage 3: runtime. ---
FROM debian:bookworm-slim@sha256:3783cc01769c7b2b1b83a5c5ad96c815348e28ed7da68e2e3687004faa906251
# The `mosquitto` user has to exist. Started as root, the broker drops
# privileges to it by default; the official Alpine image ships that user but a
# from-source build does not, so it fell back to `nobody` and then could not
# write the root-owned /mosquitto/data — "Error: Permission denied." once per
# autosave_interval, forever, with persistence silently doing nothing.
# uid/gid 1883 matches the official image so an existing data volume stays
# readable if anyone switches between the two.
RUN apt-get update && apt-get install -y --no-install-recommends \
        libssl3 libcjson1 ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /mosquitto/config /mosquitto/data \
    && groupadd -g 1883 mosquitto \
    && useradd -u 1883 -g mosquitto -d /mosquitto -s /usr/sbin/nologin mosquitto \
    && chown -R mosquitto:mosquitto /mosquitto/data
COPY --from=mosquitto_builder /usr/local/sbin/mosquitto /usr/local/sbin/mosquitto
COPY --from=mosquitto_builder /usr/local/lib/libmosquitto.so.1 /usr/local/lib/
COPY --from=plugin_builder /sse-jwt-auth.so /mosquitto/sse-jwt-auth.so
RUN ldconfig
EXPOSE 1883
# chown on every start, not just at build: the data volume already exists on
# developer machines, created root-owned before the user above existed. Docker
# only seeds ownership from the image into an empty volume, so relying on the
# build-time chown alone would mean "works, but only after you drop the volume"
# — and the image chown still covers a genuinely fresh volume. `exec` keeps
# mosquitto as PID 1 so it still receives SIGTERM.
CMD ["sh", "-c", "chown -R mosquitto:mosquitto /mosquitto/data && exec /usr/local/sbin/mosquitto -c /mosquitto/config/mosquitto.conf"]
