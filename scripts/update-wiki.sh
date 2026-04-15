#!/usr/bin/env bash
# update-wiki.sh — Sync wiki/ directory to GitHub wiki repo
# Usage: ./scripts/update-wiki.sh [commit message]
#
# Requires: gh CLI authenticated, git

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
WIKI_SRC="$PROJECT_ROOT/wiki"
WIKI_TMP="$(mktemp -d)"
REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner 2>/dev/null || echo "ArMaTeC/Redball")
WIKI_REPO="https://$(gh auth token)@github.com/${REPO}.wiki.git"
COMMIT_MSG="${1:-Update wiki documentation}"

cleanup() {
  rm -rf "$WIKI_TMP"
}
trap cleanup EXIT

echo "[WIKI] Cloning wiki repo: https://github.com/${REPO}/wiki"
if ! git clone --quiet "$WIKI_REPO" "$WIKI_TMP" 2>/dev/null; then
  echo "[WIKI] ERROR: Could not clone wiki. Ensure the wiki has been initialised"
  echo "[WIKI] (Visit https://github.com/${REPO}/wiki and create the first page manually)"
  exit 1
fi

echo "[WIKI] Syncing wiki/ files..."
# Copy all .md files from wiki/ into the cloned wiki repo
cp "$WIKI_SRC"/*.md "$WIKI_TMP/"

cd "$WIKI_TMP"

git config user.email "$(git -C "$PROJECT_ROOT" config user.email 2>/dev/null || echo 'build@redball')"
git config user.name  "$(git -C "$PROJECT_ROOT" config user.name  2>/dev/null || echo 'Redball Build')"

if git diff --quiet && git diff --cached --quiet; then
  echo "[WIKI] No changes — wiki is already up to date"
  exit 0
fi

git add -A
git commit -m "$COMMIT_MSG"
git push origin master 2>/dev/null || git push origin main

echo "[WIKI] Wiki updated: https://github.com/${REPO}/wiki"
