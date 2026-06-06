#!/usr/bin/env bash
# Release helper for com.kidzdev.addressables-toolkit
# Usage: ./release.sh <version>     e.g.  ./release.sh 1.1.0
#
# Bumps the "version" field in package.json, then commits, tags, and pushes.

set -euo pipefail

usage() {
    echo "Usage: ./release.sh <version>"
    echo "Example: ./release.sh 1.1.0"
}

# 1) Require a version argument.
VERSION="${1:-}"
if [ -z "$VERSION" ]; then
    usage
    exit 1
fi

# 4) Validate the version looks like X.Y.Z before doing anything.
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "ERROR: Version '$VERSION' is not valid. Expected X.Y.Z (e.g. 1.1.0)." >&2
    exit 1
fi

# Always operate from the repo root (this script's folder).
cd "$(dirname "$0")"

if [ ! -f package.json ]; then
    echo "ERROR: package.json not found." >&2
    exit 1
fi

# 2) Targeted replace of only the first "version" value — preserve the rest of the file.
perl -0pi -e 'BEGIN { $v = shift } s/("version"\s*:\s*")[^"]*(")/${1}$v${2}/' "$VERSION" package.json
echo "Set version to $VERSION in package.json"

TAG="v$VERSION"

# 3) Run git steps in order, stopping on any git error (commit is allowed to be a no-op).
git add -A
git commit -m "Release $TAG" || echo "Nothing to commit, continuing."
git tag "$TAG"
git push origin main
git push origin "$TAG"

# 4) Confirm the tag landed on the remote.
echo ""
echo "Released version $VERSION (tag $TAG)."
REMOTE_TAG="$(git ls-remote --tags origin "$TAG")"
if [ -z "$REMOTE_TAG" ]; then
    echo "ERROR: Tag $TAG was not found on origin after push." >&2
    exit 1
fi
echo "Confirmed: $TAG is on origin."
echo "$REMOTE_TAG"
