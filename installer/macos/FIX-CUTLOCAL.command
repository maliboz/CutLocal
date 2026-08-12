#!/bin/bash

set -euo pipefail

SCRIPT_DIRECTORY="$(cd "$(dirname "$0")" && pwd)"
EXTRACTED_APP="$SCRIPT_DIRECTORY/CutLocal.app"
INSTALLED_APP="/Applications/CutLocal.app"

if [[ -d "$EXTRACTED_APP" ]]; then
    APP_PATH="$EXTRACTED_APP"
elif [[ -d "$INSTALLED_APP" ]]; then
    APP_PATH="$INSTALLED_APP"
else
    echo "HATA: CutLocal.app bulunamadı."
    echo ""
    echo "FIX-CUTLOCAL.command dosyasını CutLocal.app ile aynı klasörde tutun"
    echo "veya CutLocal.app dosyasını Applications klasörüne taşıyın."
    echo ""
    read -r -p "Kapatmak için Enter'a basın..."
    exit 1
fi

PLIST_PATH="$APP_PATH/Contents/Info.plist"
EXECUTABLE_PATH="$APP_PATH/Contents/MacOS/CutLocal"
if [[ ! -f "$PLIST_PATH" || ! -f "$EXECUTABLE_PATH" ]]; then
    echo "HATA: Seçilen klasör geçerli bir CutLocal paketi değil."
    read -r -p "Kapatmak için Enter'a basın..."
    exit 1
fi

BUNDLE_IDENTIFIER="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$PLIST_PATH")"
if [[ "$BUNDLE_IDENTIFIER" != "app.cutlocal.desktop" ]]; then
    echo "HATA: Beklenmeyen paket kimliği: $BUNDLE_IDENTIFIER"
    read -r -p "Kapatmak için Enter'a basın..."
    exit 1
fi

echo "CutLocal macOS paketi hazırlanıyor..."
echo "Paket: $APP_PATH"
echo ""

# Operates only on the validated CutLocal bundle selected above.
/usr/bin/xattr -dr com.apple.quarantine "$APP_PATH" 2>/dev/null || true
/bin/chmod 755 "$EXECUTABLE_PATH"

while IFS= read -r -d '' binary_path; do
    if /usr/bin/file -b "$binary_path" | /usr/bin/grep -q 'Mach-O'; then
        /usr/bin/codesign --force --sign - "$binary_path"
    fi
done < <(/usr/bin/find "$APP_PATH/Contents/MacOS" -type f -print0)

/usr/bin/codesign --force --sign - "$APP_PATH"
/usr/bin/codesign --verify --deep --strict --verbose=2 "$APP_PATH"

echo ""
echo "TAMAM: Yerel imza ve paket doğrulaması başarılı."
echo "CutLocal açılıyor..."
/usr/bin/open "$APP_PATH"

echo ""
read -r -p "Bu pencereyi kapatmak için Enter'a basın..."
