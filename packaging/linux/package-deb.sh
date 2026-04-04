#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 3 ]]; then
  echo "Usage: $0 <publish_dir> <version> <output_dir>" >&2
  exit 1
fi

publish_dir="$1"
version="$2"
output_dir="$3"

package_root="$(mktemp -d)"
install_root="$package_root/opt/brassledger"
bin_root="$package_root/usr/local/bin"
debian_root="$package_root/DEBIAN"

mkdir -p "$install_root" "$bin_root" "$debian_root" "$output_dir"
cp -R "$publish_dir"/. "$install_root"/

cat > "$bin_root/brassledger" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
exec /opt/brassledger/BrassLedger.Web "$@"
EOF
chmod 755 "$bin_root/brassledger"

installed_size_kb="$(du -sk "$package_root" | awk '{print $1}')"

cat > "$debian_root/control" <<EOF
Package: brassledger
Version: $version
Section: utils
Priority: optional
Architecture: amd64
Maintainer: BrassLedger Contributors <opensource@brassledger.local>
Depends: libc6, libgcc-s1, libicu72 | libicu71 | libicu70 | libicu69, libssl3 | libssl1.1, zlib1g
Description: BrassLedger accounting platform
 Cross-platform accounting application packaged as a self-contained .NET runtime.
Installed-Size: $installed_size_kb
EOF

chmod 755 "$package_root" "$install_root" "$bin_root"
dpkg-deb --build "$package_root" "$output_dir/brassledger_${version}_amd64.deb"
rm -rf "$package_root"
