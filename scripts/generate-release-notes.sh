#!/usr/bin/env bash
set -euo pipefail

tag="${1:?version tag is required}"
output="${2:?output path is required}"
repository="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"

previous_tag="$(git describe --tags --abbrev=0 "${tag}^" 2>/dev/null || true)"
range="$tag"
if [[ -n "$previous_tag" ]]; then
  range="$previous_tag..$tag"
fi

generated_notes="$(
  if [[ -n "$previous_tag" ]]; then
    gh api \
      --method POST \
      "repos/$repository/releases/generate-notes" \
      -f tag_name="$tag" \
      -f previous_tag_name="$previous_tag" \
      --jq .body
    exit
  fi

  gh api \
    --method POST \
    "repos/$repository/releases/generate-notes" \
    -f tag_name="$tag" \
    --jq .body
)"

{
  echo "## What's Changed"
  git log "$range" --format='- %s (`%h`)'
  echo

  if grep -q '^## New Contributors$' <<<"$generated_notes"; then
    sed -n '/^## New Contributors$/,$p' <<<"$generated_notes"
  else
    echo "## New Contributors"
    echo "- No new contributors in this release."
  fi

  echo
  grep '^\*\*Full Changelog\*\*' <<<"$generated_notes" || true
} >"$output"
