#!/usr/bin/env bash
set -euo pipefail

index="https://nuget.pkg.github.com/$GITHUB_REPOSITORY_OWNER/index.json"
auth=(--user "$GITHUB_ACTOR:$GH_TOKEN")
base=$(curl -fsS "${auth[@]}" "$index" | jq -er '.resources[] | select(."@type" == "PackageBaseAddress/3.0.0") | ."@id"')
[[ "$base" == https://nuget.pkg.github.com/* ]] || { echo "::error::Unexpected package endpoint."; exit 1; }
id=codesemantic4llm.mcp.server
mkdir -p artifacts
curl -fsSL "${auth[@]}" "${base%/}/$id/$VERSION/$id.$VERSION.nupkg" \
  -o "artifacts/CodeSemantic4Llm.Mcp.Server.$VERSION.nupkg"
