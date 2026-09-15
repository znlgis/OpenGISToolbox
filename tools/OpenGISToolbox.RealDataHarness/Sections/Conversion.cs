using OpenGISToolbox.TestKit;

namespace OpenGISToolbox.RealDataHarness.Sections;

/// <summary>Every registered format-conversion tool against the real layers.</summary>
public static class Conversion
{
    public static async Task Run()
    {
        Program.Checks.BeginSection("2. Format conversion sweep");

        var poly = Program.Catalog.Polygon;
        var pt = Program.Catalog.Point;
        var subject = poly ?? pt ?? Program.Catalog.Layers.FirstOrDefault();
        if (subject == null)
        {
            Program.Checks.Skip("conversion sweep", "no usable layer");
            return;
        }

        var dir = GdalEnv.Dir("harness", "conversion");

        // SHP → GeoJSON (exact expectations)
        {
            var outPath = Path.Combine(dir, $"{subject.Name}.geojson");
            await Program.Checks.RunAsync("shp-to-geojson", async () =>
            {
                await Program.RunRegisteredAsync("shp-to-geojson",
                    new() { ["input"] = subject.ShpPath, ["output"] = outPath }, "shp-to-geojson");
                var l = GdalEnv.ReadLayer(outPath);
                Program.Require((int)l.GetFeatureCount() == subject.ExpectedCount,
                    "shp-to-geojson count", $"got {l.GetFeatureCount()}, want {subject.ExpectedCount}");
                var gj = GeoJsonFile.Read(outPath);
                Program.Require(gj.FeatureCount == subject.ExpectedCount,
                    "shp-to-geojson independent JSON parse", $"got {gj.FeatureCount}");
            });
        }

        // GeoJSON → SHP from the produced file (round trip back to SHP)
        {
            var rtPath = Path.Combine(dir, $"{subject.Name}_rt.shp");
            await Program.Checks.RunAsync("geojson-to-shp round trip", async () =>
            {
                await Program.RunRegisteredAsync("geojson-to-shp",
                    new() { ["input"] = Path.Combine(dir, $"{subject.Name}.geojson"), ["output"] = rtPath },
                    "geojson-to-shp");
                var shp = ShpFile.Read(rtPath);
                var dbf = DbfFile.Read(Path.ChangeExtension(rtPath, ".dbf"));
                Program.Require(shp.Records == subject.ExpectedCount && dbf.Records == subject.ExpectedCount,
                    "geojson-to-shp round trip count", $"shp={shp.Records} dbf={dbf.Records}");
                Program.Require(shp.GeometryKind == subject.Kind,
                    "geojson-to-shp round trip geometry kind", $"got {shp.GeometryKind} want {subject.Kind}");
            });
        }

        // SHP → GPKG → SHP
        {
            var gpkg = Path.Combine(dir, $"{subject.Name}.gpkg");
            var back = Path.Combine(dir, $"{subject.Name}_gpkg.shp");
            await Program.Checks.RunAsync("gpkg round trip", async () =>
            {
                await Program.RunRegisteredAsync("shp-to-gpkg",
                    new() { ["input"] = subject.ShpPath, ["output"] = gpkg }, "shp-to-gpkg");
                var l = GdalEnv.ReadLayer(gpkg);
                Program.Require((int)l.GetFeatureCount() == subject.ExpectedCount,
                    "shp-to-gpkg count", $"got {l.GetFeatureCount()}");
                await Program.RunRegisteredAsync("gpkg-to-shp",
                    new() { ["input"] = gpkg, ["output"] = back }, "gpkg-to-shp");
                var shp = ShpFile.Read(back);
                Program.Require(shp.Records == subject.ExpectedCount, "gpkg-to-shp count",
                    $"got {shp.Records}");
            });
        }

        // SHP → KML → SHP. KML has known lossiness (null geometries dropped), so a
        // count difference is a Warn, not a Fail.
        {
            var kml = Path.Combine(dir, $"{subject.Name}.kml");
            var backKml = Path.Combine(dir, $"{subject.Name}_kml.shp");
            await Program.Checks.RunAsync("kml round trip", async () =>
            {
                await Program.RunRegisteredAsync("shp-to-kml",
                    new() { ["input"] = subject.ShpPath, ["output"] = kml }, "shp-to-kml");
                Program.Require(new FileInfo(kml).Length > 1024, "shp-to-kml produced file", $"size {new FileInfo(kml).Length}");
                var kmlLayer = GdalEnv.ReadLayer(kml);
                var kmlCount = (int)kmlLayer.GetFeatureCount();
                Program.RequireWithWarn(kmlCount == subject.ExpectedCount, "shp-to-kml count",
                    $"KML has {kmlCount} vs source {subject.ExpectedCount} (KML drops empty/complex parts — known format behaviour)");
                await Program.RunRegisteredAsync("kml-to-shp",
                    new() { ["input"] = kml, ["output"] = backKml }, "kml-to-shp");
                var shp = ShpFile.Read(backKml);
                Program.Require(shp.Records == kmlCount, "kml-to-shp fidelity",
                    $"shp={shp.Records} vs kml={kmlCount}");
            });
        }

        // GeoJSON → KML / GeoJSON → GPKG direct from the source sibling (when present)
        if (subject.GeoJsonPath != null)
        {
            await Program.Checks.RunAsync("geojson-to-gpkg", async () =>
            {
                var outPath = Path.Combine(dir, "from_geojson.gpkg");
                await Program.RunRegisteredAsync("geojson-to-gpkg",
                    new() { ["input"] = subject.GeoJsonPath, ["output"] = outPath }, "geojson-to-gpkg");
                var l = GdalEnv.ReadLayer(outPath);
                Program.Require((int)l.GetFeatureCount() == subject.ExpectedCount,
                    "geojson-to-gpkg count", $"got {l.GetFeatureCount()}");
            });

            await Program.Checks.RunAsync("geojson-to-kml", async () =>
            {
                var outPath = Path.Combine(dir, "from_geojson.kml");
                await Program.RunRegisteredAsync("geojson-to-kml",
                    new() { ["input"] = subject.GeoJsonPath, ["output"] = outPath }, "geojson-to-kml");
                Program.Require(File.Exists(outPath) && new FileInfo(outPath).Length > 1024,
                    "geojson-to-kml produced file", "missing/too small");
            });
        }

        // DXF: vector→DXF is linework-only; attribute/feature-count fidelity is not
        // guaranteed by the format, so exact expectations apply only to file existence
        // and DXF internal self-consistency (warn otherwise).
        {
            var dxf = Path.Combine(dir, $"{subject.Name}.dxf");
            var dxfBack = Path.Combine(dir, $"{subject.Name}_dxf.shp");
            await Program.Checks.RunAsync("dxf round trip", async () =>
            {
                await Program.RunRegisteredAsync("shp-to-dxf",
                    new() { ["input"] = subject.ShpPath, ["output"] = dxf }, "shp-to-dxf");
                Program.Require(File.Exists(dxf) && new FileInfo(dxf).Length > 0, "shp-to-dxf produced file", "missing");
                if (!File.Exists(dxf)) return;
                var dxfLayer = GdalEnv.ReadLayer(dxf);
                var dxfCount = (int)dxfLayer.GetFeatureCount();
                Program.RequireWithWarn(dxfCount > 0, "shp-to-dxf contains entities", "no features read back");
                await Program.RunRegisteredAsync("dxf-to-shp",
                    new() { ["input"] = dxf, ["output"] = dxfBack }, "dxf-to-shp");
                var shp = ShpFile.Read(dxfBack);
                Program.RequireWithWarn(shp.Records == dxfCount, "dxf→shp fidelity",
                    $"shp={shp.Records} vs dxf={dxfCount} (DXF linework conversion — inherent count drift)");
            });
        }

        // FileGDB: capability probe first; skip cleanly when the bundled runtime
        // cannot create a .gdb.
        {
            var probeDir = GdalEnv.Dir("harness", "filegdb_probe");
            var driver = GdalEnv.ProbeFileGdbWrite(probeDir);
            if (driver == null)
            {
                Program.Checks.Skip("filegdb round trip", "no writable FileGDB/OpenFileGDB driver in bundled runtime");
            }
            else
            {
                var gdb = Path.Combine(probeDir, "layers.gdb");
                var gdbBack = Path.Combine(dir, "from_gdb.shp");
                await Program.Checks.RunAsync("filegdb round trip", async () =>
                {
                    await Program.RunRegisteredAsync("shp-to-filegdb",
                        new() { ["input"] = subject.ShpPath, ["output"] = gdb }, "shp-to-filegdb");
                    await Program.RunRegisteredAsync("filegdb-to-shp",
                        new() { ["input"] = gdb, ["output"] = gdbBack }, "filegdb-to-shp");
                    var shp = ShpFile.Read(gdbBack);
                    Program.RequireWithWarn(shp.Records == subject.ExpectedCount, "filegdb round trip count",
                        $"got {shp.Records}, want {subject.ExpectedCount}");
                });
            }
        }
    }
}
