#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 4 ]]; then
  echo "Usage: $0 <publish_dir> <version> <runtime> <output_dir>" >&2
  exit 1
fi

publish_dir="$1"
version="$2"
runtime="$3"
output_dir="$4"

package_root="$(mktemp -d)"
app_root="$package_root/Applications/BrassLedger"
bin_root="$package_root/usr/local/bin"

mkdir -p "$app_root" "$bin_root" "$output_dir"
cp -R "$publish_dir"/. "$app_root"/

cat > "$bin_root/brassledger" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
exec "/Applications/BrassLedger/BrassLedger.Web" "$@"
EOF
chmod 755 "$bin_root/brassledger"

pkgbuild \
  --root "$package_root" \
  --identifier "org.brassledger.app.$runtime" \
  --version "$version" \
  --install-location "/" \
  "$output_dir/BrassLedger-${version}-${runtime}.pkg"

rm -rf "$package_root"
