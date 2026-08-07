#!/usr/bin/env bash
# Rebuild manifest.json from GitHub Releases on this repo.
# Usage: ./scripts/generate-manifest.sh [owner/repo]
set -euo pipefail

REPO="${1:-${GITHUB_REPOSITORY:-Robert-Hansen/jellyfin-plugin-home-sections}}"
OUT="${2:-manifest.json}"
PLUGIN_GUID="b8298e01-2697-407a-b44d-aa8dc795e850"
FILE_TRANSFORMATION_GUID="5e87cc92-571a-4d8d-8d98-d2d4147f9f90"
PLUGIN_NAME="Home Screen Sections (Fork)"
OWNER_NAME="${REPO%%/*}"
IMAGE_URL="https://raw.githubusercontent.com/${REPO}/main/src/logo.png"

WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

echo "Scanning releases for ${REPO}..."
mapfile -t RELEASES < <(gh release list --repo "$REPO" --limit 50 --json tagName,isDraft,isPrerelease,createdAt --jq '.[] | select(.isDraft==false) | .tagName')

if [[ ${#RELEASES[@]} -eq 0 ]]; then
  echo "No releases found." >&2
  exit 1
fi

VERSIONS_JSON="[]"

for TAG in "${RELEASES[@]}"; do
  echo "Processing release ${TAG}"
  ASSETS_JSON="$(gh release view "$TAG" --repo "$REPO" --json assets,createdAt,body,isPrerelease)"
  CREATED="$(echo "$ASSETS_JSON" | jq -r '.createdAt' | sed 's/Z$//')"
  BODY="$(echo "$ASSETS_JSON" | jq -r '.body // empty' | tr '\r' '\n' | head -c 2000)"
  if [[ -z "$BODY" || "$BODY" == "null" ]]; then
    BODY="Fork build ${TAG}"
  fi

  while IFS= read -r ASSET; do
    NAME="$(echo "$ASSET" | jq -r '.name')"
    URL="$(echo "$ASSET" | jq -r '.url')"
    [[ "$NAME" == Release-*.zip ]] || continue

    JF_VER="${NAME#Release-}"
    JF_VER="${JF_VER%.zip}"
    TARGET_ABI="${JF_VER}.0"

    ZIP_PATH="${WORKDIR}/${NAME}"
    echo "  downloading ${NAME}"
    gh release download "$TAG" --repo "$REPO" -p "$NAME" -D "$WORKDIR" --clobber
    CHECKSUM="$(md5sum "$ZIP_PATH" | awk '{print toupper($1)}')"

    # Escape changelog for JSON
    CHANGELOG_JSON="$(printf '%s' "$BODY" | jq -Rs .)"

    ENTRY="$(jq -n \
      --arg version "$TAG" \
      --argjson changelog "$CHANGELOG_JSON" \
      --arg targetAbi "$TARGET_ABI" \
      --arg sourceUrl "https://github.com/${REPO}/releases/download/${TAG}/${NAME}" \
      --arg checksum "$CHECKSUM" \
      --arg timestamp "$CREATED" \
      --arg dep "$FILE_TRANSFORMATION_GUID" \
      '{
        version: $version,
        changelog: $changelog,
        targetAbi: $targetAbi,
        sourceUrl: $sourceUrl,
        checksum: $checksum,
        timestamp: $timestamp,
        dependencies: [$dep]
      }')"

    VERSIONS_JSON="$(echo "$VERSIONS_JSON" | jq --argjson e "$ENTRY" '. + [$e]')"
    echo "  ${NAME} -> ${CHECKSUM} (targetAbi ${TARGET_ABI})"
  done < <(echo "$ASSETS_JSON" | jq -c '.assets[]')
done

# Prefer newer tags first (already roughly so from gh list); keep stable order by version then abi
VERSIONS_JSON="$(echo "$VERSIONS_JSON" | jq 'sort_by(.version, .targetAbi) | reverse')"

jq -n \
  --arg guid "$PLUGIN_GUID" \
  --arg name "$PLUGIN_NAME" \
  --arg owner "$OWNER_NAME" \
  --arg imageUrl "$IMAGE_URL" \
  --argjson versions "$VERSIONS_JSON" \
  '[
    {
      guid: $guid,
      name: $name,
      overview: "Fork build for testing (includes Danish localization).",
      description: "Adds support for server provided home screen sections.\n\nThis is a personal fork build used for testing translations and changes. Install File Transformation and Plugin Pages as well.",
      owner: $owner,
      category: "General",
      imageUrl: $imageUrl,
      versions: $versions
    }
  ]' > "$OUT"

echo "Wrote $(realpath "$OUT")"
jq '.[0] | {name, guid, versionCount:(.versions|length), versions:[.versions[]|{version,targetAbi,checksum}]}' "$OUT"
