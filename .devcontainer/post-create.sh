#!/usr/bin/env bash
set -euo pipefail

corepack enable
corepack prepare --activate

dotnet restore
pnpm install --frozen-lockfile
pnpm test:e2e:install

# Best-effort steps: a failure here must not leave the workspace without
# dependencies, so they run last and only warn.

curl -sSL https://aspire.dev/install.sh | bash \
  || echo "WARNING: Aspire CLI install failed"

# `dotnet dev-certs https --trust` can only half-trust on Linux
# (PartiallyFailedToTrustTheCertificate), so export the certificate and put it
# in the system store directly.
(
  dotnet dev-certs https
  dotnet dev-certs https --export-path /tmp/aspnetcore-dev.crt --format Pem --no-password
  sudo cp /tmp/aspnetcore-dev.crt /usr/local/share/ca-certificates/aspnetcore-dev.crt
  sudo update-ca-certificates
  rm -f /tmp/aspnetcore-dev.crt
) || echo "WARNING: could not trust the ASP.NET dev certificate; use --launch-profile http"
