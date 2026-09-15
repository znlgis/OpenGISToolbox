using OpenGISToolbox.TestKit;

namespace OpenGISToolbox.RealDataHarness.Sections;

/// <summary>Anchor integrity: file-header parsing vs GDAL reads vs GeoJSON sibling.</summary>
public static class CatalogIntegrity
{
    public static async Task Run()
    {
        Program.Checks.BeginSection("1. Real-data anchor integrity");

        foreach (var layer in Program.Catalog.Layers)
        {
            await Program.Checks.RunAsync($"{layer.Name}: three-way count agreement", () =>
            {
                var shpCount = layer.Shp.Records;
                var dbfCount = layer.Dbf.Records;
                var gjCount = layer.GeoJson?.FeatureCount;
                var gdalLayer = GdalEnv.ReadLayer(layer.ShpPath);
                var gdalCount = (int)gdalLayer.GetFeatureCount();

                Program.Require(shpCount == dbfCount, $"{layer.Name}: .shp chain == .dbf records",
                    $"shp={shpCount} dbf={dbfCount}");
                Program.Require(gdalCount == shpCount, $"{layer.Name}: GDAL count == header count",
                    $"gdal={gdalCount} header={shpCount}");
                if (gjCount != null)
                    Program.Require(gjCount == shpCount, $"{layer.Name}: source GeoJSON == converted SHP",
                        $"geojson={gjCount} shp={shpCount}");
                return Task.CompletedTask;
            });

            await Program.Checks.RunAsync($"{layer.Name}: geometry type agreement", () =>
            {
                var gdalLayer = GdalEnv.ReadLayer(layer.ShpPath);
                var sample = gdalLayer.Features.FirstOrDefault(f => !string.IsNullOrEmpty(f.Wkt));
                Program.Require(sample.Wkt != null &&
                                sample.Wkt.StartsWith(layer.Shp.ShapeTypeName, StringComparison.OrdinalIgnoreCase),
                    $"{layer.Name}: GDAL WKT kind matches SHP header type",
                    $"header={layer.Shp.ShapeTypeName} wkt={sample.Wkt?.Split('(')[0]}");
                if (layer.GeoJson != null)
                {
                    // GeoJSON says MultiPolygon/MultiLineString; SHP header kind is Polygon/Line.
                    var gjBase = layer.GeoJson.FirstGeometryType.Replace("Multi", "");
                    var shpBase = layer.Shp.ShapeTypeName switch
                    {
                        "Line" => "LineString",
                        _ => layer.Shp.ShapeTypeName
                    };
                    Program.Require(gjBase == shpBase,
                        $"{layer.Name}: source GeoJSON type matches SHP type",
                        $"geojson={layer.GeoJson.FirstGeometryType} shp={layer.Shp.ShapeTypeName}");
                }
                return Task.CompletedTask;
            });
        }
    }
}
