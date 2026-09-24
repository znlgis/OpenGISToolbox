#!/usr/bin/env bash
set -u
# Why per-class processes: sharing one testhost across all classes triggers a
# native GDAL access violation (0xC0000005) on machines with OSGeo4W, and the
# bundled MaxRev runtime must be the only gdal.dll on the search path.
#
# Usage:
#   bash run-tests.sh                 # xunit suites
#   bash run-tests.sh --with-harness  # + console real-data harness (exit 1 on FAIL)
#
# Environment knobs (all optional, everything data-agnostic):
#   OGT_REAL_DATA_DIR   real .shp/.dbf directory (else Natural Earth 1:50m download cache)
#   OGT_REAL_DATA_CACHE cache dir for the Natural Earth anchor set
#   OGT_TEST_PG_HOST/PORT/DB/USER/PASSWORD   PostGIS endpoint (defaults to local container)
#   OGT_SKIP_NETWORK=1  skip live geocode/tile checks
CLEANPATH=$(echo "$PATH" | tr ':' '\n' | grep -viE "osgeo4w" | paste -sd:)
classes=(
  SmokeTests
  ConversionTests
  GeometryToolTests
  RasterTests
  MiscToolTests
  RealDataTests
  RealDataPipelineTests
  DxfFilegdbToolTests
  PostgisRoundTripTests
  NetworkToolTests
  Sprint1ToolTests
  Sprint2ToolTests
)
PROJECT=tests/OpenGISToolbox.Tests/OpenGISToolbox.Tests.csproj
FAILED=0
for cls in "${classes[@]}"; do
  echo "===== $cls ====="
  env PATH="$CLEANPATH" GDAL_DRIVER_PATH= GDAL_DATA= PROJ_LIB= PROJ_DATA= \
    dotnet test "$PROJECT" --nologo -v q --filter "FullyQualifiedName~$cls" \
    >/tmp/ogt_$cls.log 2>&1
  code=$?
  tail -2 /tmp/ogt_$cls.log | grep -aE "已通过|失败|中止|skipped|Passed|Failed" || tail -3 /tmp/ogt_$cls.log
  if [ $code -ne 0 ]; then
    echo "  !! $cls failed (exit $code) — full log: /tmp/ogt_$cls.log"
    FAILED=1
  fi
done

if [ "${1:-}" = "--with-harness" ]; then
  echo "===== RealDataHarness ====="
  env PATH="$CLEANPATH" GDAL_DRIVER_PATH= GDAL_DATA= PROJ_LIB= PROJ_DATA= \
    dotnet run --project tools/OpenGISToolbox.RealDataHarness -c Debug --no-build
  [ $? -ne 0 ] && FAILED=1
fi

exit $FAILED
