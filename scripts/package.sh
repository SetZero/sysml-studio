#!/usr/bin/env bash
# Builds the downloadable package of SysML Studio for one platform.
#
#   scripts/package.sh <rid> [version] [outdir]
#
#   rid      win-x64, win-arm64, linux-x64, linux-arm64, osx-x64 or osx-arm64
#   version  defaults to the VersionPrefix in Directory.Build.props
#   outdir   defaults to artifacts/packages
#
# The application is published self-contained as one compressed file, so
# nothing has to be installed first. The file names carry no version: the
# website links to releases/latest/download/<name>, which must not move.
#
#   win-*    SysML-Studio-windows-<arch>.msi (needs WiX 5) and .zip
#   linux-*  sysml-studio_<debarch>.deb, sysml-studio-<rpmarch>.rpm (needs
#            rpmbuild) and SysML-Studio-linux-<arch>.tar.gz
#   osx-*    SysML-Studio-macos-<arch>.pkg and a .zip of SysML Studio.app
#
# Every one carries the sample models. On CI a missing tool is an error;
# elsewhere that installer is left out.
set -euo pipefail

cd "$(dirname "$0")/.."
rid="${1:?usage: scripts/package.sh <rid> [version] [outdir]}"
version="${2:-$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' Directory.Build.props)}"
out="${3:-artifacts/packages}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"

os="${rid%-*}"
arch="${rid#*-}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "==> publish $rid $version"
dotnet publish src/SysmlStudio.App -c Release -r "$rid" --self-contained --nologo \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=none \
    -p:GenerateDocumentationFile=false \
    -p:Version="$version" \
    -o "$work/app"
# Symbols some native packages ship beside their libraries.
find "$work/app" \( -name '*.pdb' -o -name '*.dbg' -o -name '*.xml' \) -delete

zip_dir() { # zip_dir <parent> <entry> <archive>
    if command -v zip >/dev/null; then
        (cd "$1" && zip -qr -9 "$3" "$2")
    elif command -v 7z >/dev/null; then
        local archive="$3"
        command -v cygpath >/dev/null && archive="$(cygpath -w "$3")"
        (cd "$1" && 7z a -tzip -mx=9 -bso0 -bsp0 "$archive" "$2")
    else
        powershell -NoProfile -Command "Compress-Archive -Path '$1/$2' -DestinationPath '$3' -Force"
    fi
}

case "$os" in
win)
    mkdir -p "$work/pkg"
    mv "$work/app" "$work/pkg/SysML Studio"
    mv "$work/pkg/SysML Studio/SysmlStudio.App.exe" "$work/pkg/SysML Studio/SysML Studio.exe"
    cp LICENSE "$work/pkg/SysML Studio/"
    name="SysML-Studio-windows-$arch.zip"
    rm -f "$out/$name"
    zip_dir "$work/pkg" "SysML Studio" "$out/$name"
    echo "$out/$name"

    # The installer: Program Files, the Start menu, Apps & features.
    if command -v wix >/dev/null; then
        appdir="$work/pkg/SysML Studio"
        command -v cygpath >/dev/null && appdir="$(cygpath -w "$appdir")"
        name="SysML-Studio-windows-$arch.msi"
        # An MSI version is numbers only: 0.2.0-dev.7 installs as 0.2.0.
        wix build installer/windows/SysmlStudio.wxs -arch "$arch" \
            -d Version="${version%%-*}" -d AppDir="$appdir" \
            -ext WixToolset.UI.wixext -o "$out/$name"
        rm -f "$out/${name%.msi}.wixpdb"
        echo "$out/$name"
    elif [ -n "${CI:-}" ]; then
        echo "wix is not installed: cannot build the .msi" >&2
        exit 1
    else
        echo "wix is not installed: no .msi (dotnet tool install --global wix --version 5.0.2)" >&2
    fi
    ;;

linux)
    icon=src/SysmlStudio.App/Assets/Icon/sysml-studio.png
    desktop="[Desktop Entry]
Type=Application
Name=SysML Studio
Comment=Draw and edit SysML v2 models
Exec=sysml-studio %F
Icon=sysml-studio
Terminal=false
Categories=Development;Engineering;
Keywords=SysML;MBSE;model;diagram;"

    # The tarball: run ./sysml-studio/sysml-studio from wherever it is unpacked.
    mkdir -p "$work/tar"
    cp -r "$work/app" "$work/tar/sysml-studio"
    mv "$work/tar/sysml-studio/SysmlStudio.App" "$work/tar/sysml-studio/sysml-studio"
    cp "$icon" LICENSE "$work/tar/sysml-studio/"
    printf '%s\n' "$desktop" > "$work/tar/sysml-studio/sysml-studio.desktop"
    name="SysML-Studio-linux-$arch.tar.gz"
    tar -C "$work/tar" -czf "$out/$name" sysml-studio
    echo "$out/$name"

    # The Debian package: installed under /opt, on the PATH and in the menu.
    case "$arch" in x64) debarch=amd64 ;; arm64) debarch=arm64 ;; *) echo "no .deb for $arch" >&2; exit 1 ;; esac
    deb="$work/deb"
    mkdir -p "$deb/DEBIAN" "$deb/opt" "$deb/usr/bin" "$deb/usr/share/applications" \
             "$deb/usr/share/icons/hicolor/512x512/apps" "$deb/usr/share/doc/sysml-studio"
    cp -r "$work/app" "$deb/opt/sysml-studio"
    mv "$deb/opt/sysml-studio/SysmlStudio.App" "$deb/opt/sysml-studio/sysml-studio"
    # A launcher, not a link: the program finds its samples beside itself.
    cat > "$deb/usr/bin/sysml-studio" <<'EOF'
#!/bin/sh
exec /opt/sysml-studio/sysml-studio "$@"
EOF
    printf '%s\n' "$desktop" > "$deb/usr/share/applications/sysml-studio.desktop"
    cp "$icon" "$deb/usr/share/icons/hicolor/512x512/apps/sysml-studio.png"
    cp LICENSE "$deb/usr/share/doc/sysml-studio/copyright"
    cat > "$deb/DEBIAN/control" <<EOF
Package: sysml-studio
Version: $version
Architecture: $debarch
Maintainer: SetZero <SetZero@users.noreply.github.com>
Homepage: https://setzero.github.io/sysml-studio/
Section: devel
Priority: optional
Depends: libc6, libfontconfig1, libx11-6, libice6, libsm6
Installed-Size: $(du -sk "$deb" | cut -f1)
Description: Graphical viewer and editor for SysML v2 models
 Open a folder of .sysml files, browse the model as a tree, read it as
 diagrams and change it. Every edit is a minimal patch over the text.
EOF
    chmod -R u=rwX,go=rX "$deb"
    chmod 755 "$deb/opt/sysml-studio/sysml-studio" "$deb/usr/bin/sysml-studio"
    name="sysml-studio_$debarch.deb"
    dpkg-deb --root-owner-group --build "$deb" "$out/$name" >/dev/null
    echo "$out/$name"

    # The RPM package, for Fedora, openSUSE and the like: the same files.
    case "$arch" in x64) rpmarch=x86_64 ;; arm64) rpmarch=aarch64 ;; esac
    if command -v rpmbuild >/dev/null; then
        rm -rf "$deb/DEBIAN"
        mkdir -p "$work/rpm/SPECS"
        cat > "$work/rpm/SPECS/sysml-studio.spec" <<EOF
Name: sysml-studio
Version: ${version//-/\~}
Release: 1
Summary: Graphical viewer and editor for SysML v2 models
License: MIT
URL: https://setzero.github.io/sysml-studio/
Requires: fontconfig, libX11, libICE, libSM
AutoReqProv: no
# The program is one bundle: stripping or splitting it would break it.
%global __os_install_post %{nil}
%global debug_package %{nil}
%define _build_id_links none

%description
Open a folder of .sysml files, browse the model as a tree, read it as
diagrams and change it. Every edit is a minimal patch over the text.

%install
cp -a "$deb/." %{buildroot}/

%files
/opt/sysml-studio
/usr/bin/sysml-studio
/usr/share/applications/sysml-studio.desktop
/usr/share/icons/hicolor/512x512/apps/sysml-studio.png
%license /usr/share/doc/sysml-studio/copyright
EOF
        rpmbuild -bb --quiet --target "$rpmarch" --define "_topdir $work/rpm" "$work/rpm/SPECS/sysml-studio.spec"
        name="sysml-studio-$rpmarch.rpm"
        cp "$work/rpm/RPMS/$rpmarch/"*.rpm "$out/$name"
        echo "$out/$name"
    elif [ -n "${CI:-}" ]; then
        echo "rpmbuild is not installed: cannot build the .rpm" >&2
        exit 1
    fi
    ;;

osx)
    bundle="$work/pkg/SysML Studio.app"
    mkdir -p "$bundle/Contents/Resources"
    mv "$work/app" "$bundle/Contents/MacOS"
    cp src/SysmlStudio.App/Assets/Icon/sysml-studio.icns "$bundle/Contents/Resources/"
    cat > "$bundle/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>SysML Studio</string>
    <key>CFBundleDisplayName</key><string>SysML Studio</string>
    <key>CFBundleIdentifier</key><string>io.github.setzero.sysml-studio</string>
    <key>CFBundleVersion</key><string>$version</string>
    <key>CFBundleShortVersionString</key><string>$version</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleExecutable</key><string>SysmlStudio.App</string>
    <key>CFBundleIconFile</key><string>sysml-studio.icns</string>
    <key>LSMinimumSystemVersion</key><string>12.0</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF
    # Not notarized: an ad-hoc signature lets it run once the user allows it.
    if command -v codesign >/dev/null; then
        codesign --force --deep --sign - "$bundle"
    fi
    name="SysML-Studio-macos-$arch.zip"
    rm -f "$out/$name"
    if command -v ditto >/dev/null; then
        ditto -c -k --sequesterRsrc --keepParent "$bundle" "$out/$name"
    else
        zip_dir "$work/pkg" "SysML Studio.app" "$out/$name"
    fi
    echo "$out/$name"

    # The installer: puts SysML Studio.app in /Applications, after the licence.
    if command -v pkgbuild >/dev/null; then
        pkgbuild --analyze --root "$work/pkg" "$work/components.plist" >/dev/null
        # Install where it says, not over a copy of the app found elsewhere.
        plutil -replace 0.BundleIsRelocatable -bool NO "$work/components.plist"
        pkgbuild --quiet --root "$work/pkg" --component-plist "$work/components.plist" \
            --identifier io.github.setzero.sysml-studio --version "$version" \
            --install-location /Applications "$work/sysml-studio.pkg"
        mkdir -p "$work/resources"
        cp LICENSE "$work/resources/LICENSE.txt"
        productbuild --synthesize --package "$work/sysml-studio.pkg" "$work/distribution.xml"
        sed -i '' 's|<installer-gui-script minSpecVersion="[0-9]*">|&\
    <title>SysML Studio</title>\
    <license file="LICENSE.txt"/>|' "$work/distribution.xml"
        name="SysML-Studio-macos-$arch.pkg"
        productbuild --quiet --distribution "$work/distribution.xml" --resources "$work/resources" \
            --package-path "$work" "$out/$name"
        echo "$out/$name"
    fi
    ;;

*)
    echo "unknown runtime $rid" >&2
    exit 1
    ;;
esac
