"""Check that the selected CI run produced the requested tool before promotion."""

import os
import xml.etree.ElementTree as ET
from zipfile import ZipFile

package_id = "CodeSemantic4Llm.Mcp.Server"
version = os.environ["VERSION"]
with ZipFile(f"artifacts/{package_id}.{version}.nupkg") as package:
    manifest = ET.fromstring(package.read(f"{package_id}.nuspec"))

metadata = manifest.find("{*}metadata")
if metadata is None or metadata.findtext("{*}id") != package_id or metadata.findtext("{*}version") != version:
    raise SystemExit("Package identity does not match the requested release.")

repository = metadata.find("{*}repository")
if repository is None or repository.get("commit") != os.environ["COMMIT"]:
    raise SystemExit("Package source commit does not match the selected CI run.")
