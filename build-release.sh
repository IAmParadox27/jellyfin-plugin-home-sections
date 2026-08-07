#!/usr/bin/env bash
# Build Home Screen Sections plugin zip(s) for manual install on Jellyfin.
#
# Usage:
#   ./build-release.sh [plugin_version] [jellyfin_versions] [output_dir]
#
# Examples:
#   ./build-release.sh 2.5.13.0 10.11.11
#   ./build-release.sh 2.5.13.0 10.10.7,10.11.5,10.11.11
#   ./build-release.sh 2.5.13.0 10.11.11 ./dist

set -euo pipefail

PLUGIN_VERSION="${1:-2.5.13.0}"
JF_VERSIONS_RAW="${2:-10.11.11}"
OUTPUT_DIR="${3:-}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ -z "$OUTPUT_DIR" ]]; then
  OUTPUT_DIR="${REPO_ROOT}/dist"
fi

if [[ "$PLUGIN_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  PLUGIN_VERSION="${PLUGIN_VERSION}.0"
fi

PROJECT="${REPO_ROOT}/src/Jellyfin.Plugin.HomeScreenSections/Jellyfin.Plugin.HomeScreenSections.csproj"
ASSEMBLY_INFO="${REPO_ROOT}/src/Jellyfin.Plugin.HomeScreenSections/Properties/AssemblyInfo.cs"

if [[ ! -f "$PROJECT" ]]; then
  echo "Project not found: $PROJECT" >&2
  exit 1
fi

IFS=',' read -ra JF_VERSIONS <<< "$JF_VERSIONS_RAW"

ASSEMBLY_BACKUP="$(mktemp)"
CSPROJ_BACKUP="$(mktemp)"
cp "$ASSEMBLY_INFO" "$ASSEMBLY_BACKUP"
cp "$PROJECT" "$CSPROJ_BACKUP"

cleanup() {
  cp "$ASSEMBLY_BACKUP" "$ASSEMBLY_INFO"
  cp "$CSPROJ_BACKUP" "$PROJECT"
  rm -f "$ASSEMBLY_BACKUP" "$CSPROJ_BACKUP"
}
trap cleanup EXIT

# Stamp versions for this build
if [[ "$(uname -s)" == "Darwin" ]]; then
  sed -i '' "s/AssemblyVersion(\"[^\"]*\")/AssemblyVersion(\"${PLUGIN_VERSION}\")/" "$ASSEMBLY_INFO"
  sed -i '' "s#<Version>[^<]*</Version>#<Version>${PLUGIN_VERSION}</Version>#" "$PROJECT"
else
  sed -i "s/AssemblyVersion(\"[^\"]*\")/AssemblyVersion(\"${PLUGIN_VERSION}\")/" "$ASSEMBLY_INFO"
  sed -i "s#<Version>[^<]*</Version>#<Version>${PLUGIN_VERSION}</Version>#" "$PROJECT"
fi

mkdir -p "$OUTPUT_DIR"
echo "Plugin version: ${PLUGIN_VERSION}"

for JF in "${JF_VERSIONS[@]}"; do
  JF="$(echo "$JF" | xargs)"
  [[ -z "$JF" ]] && continue

  echo ""
  echo "=== Building for Jellyfin ${JF} ==="

  if [[ "$JF" == 10.10.* ]]; then
    TFM="net8.0"
  else
    TFM="net9.0"
  fi

  dotnet restore "$PROJECT" -p:JellyfinVersion="$JF"
  dotnet build "$PROJECT" -c Release --no-restore -p:JellyfinVersion="$JF"

  OUT_DIR="${REPO_ROOT}/src/Jellyfin.Plugin.HomeScreenSections/bin/Release/${TFM}"
  STAGE="$(mktemp -d)"

  for file in \
    Jellyfin.Plugin.HomeScreenSections.dll \
    Jellyfin.Plugin.HomeScreenSections.deps.json \
    logo.png
  do
    if [[ ! -f "${OUT_DIR}/${file}" ]]; then
      echo "Missing build output: ${OUT_DIR}/${file}" >&2
      rm -rf "$STAGE"
      exit 1
    fi
    cp "${OUT_DIR}/${file}" "$STAGE/"
  done

  if [[ -f "${OUT_DIR}/Jellyfin.Plugin.HomeScreenSections.pdb" ]]; then
    cp "${OUT_DIR}/Jellyfin.Plugin.HomeScreenSections.pdb" "$STAGE/"
  fi

  ZIP_PATH="${OUTPUT_DIR}/Release-${JF}.zip"
  rm -f "$ZIP_PATH"
  (
    cd "$STAGE"
    if command -v zip >/dev/null 2>&1; then
      zip -9 "$ZIP_PATH" ./*
    else
      # Fallback when zip is unavailable (e.g. some Windows shells)
      powershell.exe -NoProfile -Command "Compress-Archive -Path (Join-Path '$STAGE' '*') -DestinationPath '$ZIP_PATH' -Force"
    fi
  )
  rm -rf "$STAGE"

  echo "Created ${ZIP_PATH}"
done

echo ""
echo "Done. Install on Jellyfin:"
echo "  1. Stop Jellyfin (or be ready to restart)"
echo "  2. Unzip Release-<your-version>.zip into:"
echo "       <Jellyfin data>/plugins/HomeScreenSections/"
echo "  3. Restart Jellyfin"
echo ""
echo "Requires File Transformation + Plugin Pages plugins (see README)."
