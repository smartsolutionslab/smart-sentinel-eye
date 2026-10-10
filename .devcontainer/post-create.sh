#!/usr/bin/env bash
set -euo pipefail

corepack enable
corepack prepare --activate

curl -sSL https://aspire.dev/install.sh | bash

dotnet restore
pnpm install --frozen-lockfile
pnpm test:e2e:install
