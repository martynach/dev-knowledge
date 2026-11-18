#!/usr/bin/env bash
# Cloud mutation script; invoked only by approved deployment environments.
set -euo pipefail
dir="$1"; target="$2"
test "${COST_SAFETY_APPROVED:-false}" = true || { echo 'Cost approval missing.'; exit 1; }
[[ "$target" = dev || "$target" = prod ]] || exit 1
node scripts/verify-release.mjs "$dir" "$target"
commit=$(node -p "require(process.argv[1]).commit" "$dir/manifest.json")
if [[ "$target" = dev ]]; then
  registry="${IMAGE_REPOSITORY%%/*}"
  printf '%s' "$REGISTRY_TOKEN" | docker login "$registry" --username "$REGISTRY_USER" --password-stdin
  trap 'docker logout "$registry" >/dev/null 2>&1 || true' EXIT
  docker load -i "$dir/api.tar"
  docker tag devknowledge:validated "$IMAGE_REPOSITORY:$commit"
  docker push "$IMAGE_REPOSITORY:$commit"
  image=$(docker inspect --format='{{index .RepoDigests 0}}' "$IMAGE_REPOSITORY:$commit")
  IMAGE_DIGEST="$image" node scripts/create-promotion.mjs "$dir"
else
  image=$(node -p "require(process.argv[1]).image" "$dir/promotion.json")
fi
[[ "$image" = "$IMAGE_REPOSITORY"@sha256:* ]] || { echo 'Unexpected image repository or digest.'; exit 1; }
# Overlay environment-specific public OIDC config without rebuilding the validated JS.
RELEASE_SOURCE_REF=$(node -p "require(process.argv[1]).sourceRef" "$dir/manifest.json") node scripts/configure-frontend.mjs "$dir/frontend" "$commit"
az containerapp update --resource-group "$RESOURCE_GROUP" --name "$CONTAINER_APP" --image "$image" --set-env-vars "Release__Commit=$commit" >/dev/null
# Install the pinned deployment CLI locally, never an unpinned global latest.
npx --yes --package @azure/static-web-apps-cli@2.0.7 swa deploy "$dir/frontend" --deployment-token "$SWA_TOKEN" --env production
curl --fail --retry 12 --retry-delay 5 --retry-all-errors "$API_ORIGIN/health"
curl --fail --retry 6 --retry-delay 5 "$API_ORIGIN/api/articles?page=1"
curl --fail --retry 6 --retry-delay 5 "$WEB_ORIGIN/"
test "$(curl --fail --silent "$WEB_ORIGIN/release.json" | node -pe 'JSON.parse(require("fs").readFileSync(0,"utf8")).commit')" = "$commit"
echo "Deployed $target commit $commit using $image"
