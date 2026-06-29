#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

RIDS=("win-x64" "osx-x64" "linux-x64" "linux-arm64")

for rid in "${RIDS[@]}"; do
    echo "=== Building $rid ==="
    dotnet publish -c Release -r "$rid" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -o "dist/$rid"
    echo ""
done

echo "=== All builds complete ==="
ls -lh dist/*/BlazorApp1* 2>/dev/null || true
