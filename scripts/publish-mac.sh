#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_root="${1:-${repo_root}/publish/mac-arm64}"
app="${output_root}/ASPAssistant.app"
if [[ -e "${app}" ]]; then
  echo "Output already exists: ${app}" >&2
  exit 1
fi

mkdir -p "${app}/Contents/MacOS" "${app}/Contents/Resources"
dotnet publish "${repo_root}/src/ASPAssistant.Mac/ASPAssistant.Mac.csproj" \
  -c Release -f net9.0 -r osx-arm64 --self-contained false \
  -p:UseAppHost=true -o "${app}/Contents/MacOS"
swiftc "${repo_root}/src/ASPAssistant.Mac/Native/window-list.swift" \
  -o "${app}/Contents/MacOS/aspa-window-list"
swiftc "${repo_root}/src/ASPAssistant.Mac/Native/passport-login.swift" \
  -o "${app}/Contents/MacOS/aspa-passport-login"
clang -arch arm64 "${repo_root}/src/ASPAssistant.Mac/Native/app-launcher.c" \
  -o "${app}/Contents/MacOS/ASPAssistant"
clang -arch arm64 -dynamiclib -fobjc-arc -framework AppKit -framework CoreGraphics \
  "${repo_root}/src/ASPAssistant.Mac/Native/window-position.m" \
  -o "${app}/Contents/MacOS/libaspa-window.dylib"

cat > "${app}/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleDevelopmentRegion</key><string>zh_CN</string>
  <key>CFBundleExecutable</key><string>ASPAssistant</string>
  <key>CFBundleIdentifier</key><string>cn.moonfair.aspassistant.mac</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>ASP Assistant</string>
  <key>CFBundleDisplayName</key><string>ASP Assistant</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>0.1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSScreenCaptureUsageDescription</key><string>需要读取游戏窗口以识别卫戍协议画面。</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
</dict></plist>
PLIST

iconset="${output_root}/AppIcon.iconset"
mkdir -p "${iconset}"
source_icon="${repo_root}/src/ASPAssistant.App/Resources/AppIcon.png"
for size in 16 32 64 128 256 512 1024; do
  sips -z "${size}" "${size}" "${source_icon}" \
    --out "${iconset}/icon_${size}x${size}.png" >/dev/null
done
cp "${iconset}/icon_32x32.png" "${iconset}/icon_16x16@2x.png"
cp "${iconset}/icon_64x64.png" "${iconset}/icon_32x32@2x.png"
cp "${iconset}/icon_256x256.png" "${iconset}/icon_128x128@2x.png"
cp "${iconset}/icon_512x512.png" "${iconset}/icon_256x256@2x.png"
cp "${iconset}/icon_1024x1024.png" "${iconset}/icon_512x512@2x.png"
iconutil -c icns "${iconset}" -o "${app}/Contents/Resources/AppIcon.icns"
rm -rf "${iconset}"

codesign --force --deep --sign "${CODESIGN_IDENTITY:--}" "${app}"
echo "Built ${app}"
