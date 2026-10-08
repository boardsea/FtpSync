#!/bin/bash
# Builds FtpSync (Linux: mono-mcs + mingw-w64) and packs dist/FtpSync-<ver>-{x64,x86}.zip
set -e
cd "$(dirname "$0")"
VER=$(grep -o 'Version = "[0-9.]*"' managed/Plugin.cs | grep -o '[0-9.]*')
OUT=build; rm -rf $OUT; mkdir -p $OUT dist

echo "== tests"
mcs -out:$OUT/tests.exe -r:System.Xml.dll -r:System.Security.dll -r:lib/Renci.SshNet.dll managed/Core/*.cs tests/Tests.cs
cp lib/Renci.SshNet.dll $OUT/
mono $OUT/tests.exe
rm $OUT/tests.exe

echo "== managed"
# Compile against the .NET Framework 4.8 reference assemblies (NOT mono's own class library:
# mono has members such as String.TrimEnd(char) that do not exist on Windows).
mcs -nostdlib -noconfig -lib:/usr/lib/mono/4.8-api \
  -r:mscorlib.dll,System.dll,System.Core.dll,System.Xml.dll,System.Security.dll,System.Windows.Forms.dll,System.Drawing.dll \
  -r:lib/Renci.SshNet.dll -codepage:utf8 -target:library -optimize+ -out:$OUT/FtpSync.Managed.dll \
  managed/*.cs managed/Core/*.cs managed/UI/*.cs

for arch in x64:x86_64 x86:i686; do
  name=${arch%%:*}; tc=${arch##*:}
  echo "== native $name"
  ${tc}-w64-mingw32-g++ -shared -O2 -static -static-libgcc -static-libstdc++ -o $OUT/FtpSync-$name.dll native/FtpSync.cpp
  pkg=$OUT/pkg-$name/FtpSync; mkdir -p $pkg
  cp $OUT/FtpSync-$name.dll $pkg/FtpSync.dll
  cp $OUT/FtpSync.Managed.dll $OUT/Renci.SshNet.dll $pkg/
  cp -r lang $pkg/lang
  cp README.md $pkg/README.md; cp lib/SSH.NET-LICENSE.txt $pkg/ 2>/dev/null || true
  (cd $OUT/pkg-$name && zip -qr ../../dist/FtpSync-$VER-$name.zip FtpSync)
done
ls -la dist/FtpSync-*
