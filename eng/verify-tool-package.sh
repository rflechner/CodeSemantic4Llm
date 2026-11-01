#!/usr/bin/env bash
# Verify that a packaged tool actually starts and can create/preview configuration.
set -euo pipefail

package_dir="${1:?Pass the directory containing the nupkg}"
package_version="${2:?Pass the package version}"
package_file="$package_dir/CodeSemantic4Llm.Mcp.Server.$package_version.nupkg"

if [[ ! -f "$package_file" ]]; then
  echo "Expected package not found: $package_file" >&2
  exit 1
fi

sandbox="$(mktemp -d)"
trap 'rm -rf "$sandbox"' EXIT

dotnet tool install CodeSemantic4Llm.Mcp.Server \
  --tool-path "$sandbox/tools" \
  --source "$(realpath "$package_dir")" \
  --version "$package_version"

tool="$sandbox/tools/codesemantic4llm"
"$tool" --help > "$sandbox/help.txt"
grep -q 'serve' "$sandbox/help.txt"
grep -q 'dashboard' "$sandbox/help.txt"

mkdir -p "$sandbox/repository"
printf 'namespace Widgets; public sealed class Widget { public void Run() {} }\n' \
  > "$sandbox/repository/Widget.cs"

"$tool" init "$sandbox/repository" --name "Tool smoke test"
test -f "$sandbox/repository/codesemantic4llm.yaml"

"$tool" preview "$sandbox/repository" --max-files 10 > "$sandbox/preview.txt"
grep -q 'Widget.cs' "$sandbox/preview.txt"

echo "Packaged codesemantic4llm $package_version passed install, help, init and preview."
