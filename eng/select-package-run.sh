#!/usr/bin/env bash
set -euo pipefail

branch=main
filter=()
if [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+-beta-([a-f0-9]{8})$ ]]; then
  branch=develop
  commit=$(gh api "repos/$GITHUB_REPOSITORY/commits/${BASH_REMATCH[1]}" --jq .sha)
  filter=(--commit "$commit")
elif [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "::error::Expected a stable version or a beta version produced by CI."
  exit 1
fi

if [[ -z "$RUN_ID" ]]; then
  RUN_ID=$(gh run list --repo "$GITHUB_REPOSITORY" --workflow ci.yml --branch "$branch" \
    "${filter[@]}" --event push --status success --limit 1 --json databaseId --jq '.[0].databaseId')
fi
if [[ ! "$RUN_ID" =~ ^[1-9][0-9]*$ ]]; then
  echo "::error::No valid CI run selected. Supply a successful push run ID."
  exit 1
fi

run=$(gh api "repos/$GITHUB_REPOSITORY/actions/runs/$RUN_ID")
jq -e --arg branch "$branch" --arg expected "${commit:-}" '
  .path == ".github/workflows/ci.yml" and .event == "push" and .conclusion == "success"
  and .head_branch == $branch and ($expected == "" or .head_sha == $expected)
' <<< "$run" >/dev/null || { echo "::error::Run $RUN_ID does not match the required CI push."; exit 1; }

artifacts=$(gh api --paginate --slurp "repos/$GITHUB_REPOSITORY/actions/runs/$RUN_ID/artifacts")
source=registry
if jq -e 'any(.[].artifacts[]; .name == "codesemantic4llm-dotnet-tool" and (.expired | not))' <<< "$artifacts" >/dev/null; then
  source=artifact
fi
echo "run_id=$RUN_ID" >> "$GITHUB_OUTPUT"
echo "commit=$(jq -r .head_sha <<< "$run")" >> "$GITHUB_OUTPUT"
echo "source=$source" >> "$GITHUB_OUTPUT"
