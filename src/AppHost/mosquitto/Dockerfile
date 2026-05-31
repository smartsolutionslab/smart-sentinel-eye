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

# --- Stage 1: build Mosquitto (glibc) so we get the broker binary,
#     libmosquitto, and the plugin headers (mosquitto_broker.h is not in
#     any Debian -dev package). ---
FROM debian:bookworm-slim AS mosquitto_builder
ARG MOSQUITTO_VERSION
RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential wget ca-certificates libssl-dev libcjson-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /build
RUN wget -q "https://mosquitto.org/files/source/mosquitto-${MOSQUITTO_VERSION}.tar.gz" \
    && tar xzf "mosquitto-${MOSQUITTO_VERSION}.tar.gz"
WORKDIR /build/mosquitto-${MOSQUITTO_VERSION}
RUN make WITH_WEBSOCKETS=no WITH_DOCS=no WITH_CJSON=yes WITH_TLS=yes WITH_SRV=no -j"$(nproc)" \
    && make install WITH_WEBSOCKETS=no WITH_DOCS=no

# --- Stage 2: build the Go auth plugin as a c-shared .so against those
#     headers. ---
FROM golang:1.23-bookworm AS plugin_builder
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
FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends \
        libssl3 libcjson1 ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /mosquitto/config /mosquitto/data
COPY --from=mosquitto_builder /usr/local/sbin/mosquitto /usr/local/sbin/mosquitto
COPY --from=mosquitto_builder /usr/local/lib/libmosquitto.so.1 /usr/local/lib/
COPY --from=plugin_builder /sse-jwt-auth.so /mosquitto/sse-jwt-auth.so
RUN ldconfig
EXPOSE 1883
CMD ["/usr/local/sbin/mosquitto", "-c", "/mosquitto/config/mosquitto.conf"]
