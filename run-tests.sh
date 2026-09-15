#!/usr/bin/env bash
set -u
CLEANPATH=$(echo "$PATH" | tr ':' '\n' | grep -vi "osgeo4w" | paste -sd:)
classes=(SmokeTests ConversionTests GeometryToolTests RasterTests MiscToolTests RealDataTests)
TOTAL=0; PASS=0; FAIL=0
for cls in "${classes[@]}"; do
  echo "===== $cls ====="
  env PATH="$CLEANPATH" GDAL_DRIVER_PATH= GDAL_DATA= PROJ_LIB= PROJ_DATA= \
    dotnet test tests/OpenGISToolbox.Tests/OpenGISToolbox.Tests.csproj --filter "FullyQualifiedName~$cls" \
    2>&1 | grep -aE "已通过!|失败!|测试运行已中止" | tail -1
done
