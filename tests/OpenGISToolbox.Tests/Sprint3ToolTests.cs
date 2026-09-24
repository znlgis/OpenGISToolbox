using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGISToolbox.TestKit;
using OpenGISToolbox.Tools;
using Xunit;

namespace OpenGISToolbox.Tests;

/// <summary>
/// Sprint-3 raster tools: DEM terrain (slope/aspect/hillshade), contours, warp,
/// clip, mosaic and zonal statistics. Expectations are derived from synthetic
/// rasters with a known geotransform, independent of the GDAL output.
/// </summary>
public class Sprint3ToolTests : IDisposable
{
    private readonly string _dir = TestEnv.Dir("sprint3");

    private static double[] Interior(double[] v, int w, int h)
    {
        var list = new List<double>();
        for (var r = 1; r < h - 1; r++)
            for (var c = 1; c < w - 1; c++)
                list.Add(v[r * w + c]);
        return list.ToArray();
    }

    // ─── 3.1 DEM terrain ───

    [Fact]
    public async Task DemTerrain_Slope_Of_Linear_Ramp_Is_About_45_Degrees()
    {
        var dem = Synth.MakePlanarDem(_dir, "slope_dem.tif", 12, 6);
        var outPath = Path.Combine(_dir, "slope.tif");
        var result = await TestEnv.RunAsync(new DemTerrainTool(), new Dictionary<string, string>
        {
            ["input"] = dem, ["output"] = outPath, ["algorithm"] = "Slope",
            ["slopeFormat"] = "degrees", ["zFactor"] = "1"
        });
        TestEnv.AssertSucceeded(result);

        var (w, h, values, _) = Synth.ReadRaster(outPath);
        var interior = Interior(values, w, h).Where(x => !double.IsNaN(x)).ToArray();
        Assert.NotEmpty(interior);
        // elevation = column index, pixel size 1 ⇒ dz/dx = 1 ⇒ slope = 45° everywhere.
        Assert.All(interior, s => Assert.InRange(s, 44.0, 46.0));
    }

    [Fact]
    public async Task DemTerrain_Slope_Percent_Is_PostScaled()
    {
        var dem = Synth.MakePlanarDem(_dir, "slope_pct_dem.tif", 12, 6);
        var outPath = Path.Combine(_dir, "slope_pct.tif");
        var result = await TestEnv.RunAsync(new DemTerrainTool(), new Dictionary<string, string>
        {
            ["input"] = dem, ["output"] = outPath, ["algorithm"] = "Slope",
            ["slopeFormat"] = "percent"
        });
        TestEnv.AssertSucceeded(result);

        var (w, h, values, _) = Synth.ReadRaster(outPath);
        var interior = Interior(values, w, h).Where(x => !double.IsNaN(x)).ToArray();
        Assert.NotEmpty(interior);
        // 45° plane ⇒ tan(45°)·100 = 100% everywhere.
        Assert.All(interior, s => Assert.InRange(s, 99.0, 101.0));
    }

    [Fact]
    public async Task DemTerrain_Aspect_Of_Linear_Ramp_Is_Uniform()
    {
        var dem = Synth.MakePlanarDem(_dir, "aspect_dem.tif", 12, 6);
        var outPath = Path.Combine(_dir, "aspect.tif");
        var result = await TestEnv.RunAsync(new DemTerrainTool(), new Dictionary<string, string>
        {
            ["input"] = dem, ["output"] = outPath, ["algorithm"] = "Aspect"
        });
        TestEnv.AssertSucceeded(result);

        var (w, h, values, _) = Synth.ReadRaster(outPath);
        var interior = Interior(values, w, h).Where(x => !double.IsNaN(x)).ToArray();
        Assert.NotEmpty(interior);
        Assert.InRange(interior.Min(), 0, 360);
        Assert.True(interior.Max() - interior.Min() < 1e-6,
            $"aspect should be constant on a planar ramp (spread={interior.Max() - interior.Min()})");
    }

    [Fact]
    public async Task DemTerrain_Hillshade_Is_In_Byte_Range()
    {
        var dem = Synth.MakePlanarDem(_dir, "hs_dem.tif", 12, 6);
        var outPath = Path.Combine(_dir, "hillshade.tif");
        var result = await TestEnv.RunAsync(new DemTerrainTool(), new Dictionary<string, string>
        {
            ["input"] = dem, ["output"] = outPath, ["algorithm"] = "Hillshade",
            ["azimuth"] = "315", ["altitude"] = "45", ["zFactor"] = "1"
        });
        TestEnv.AssertSucceeded(result);

        var (_, _, values, _) = Synth.ReadRaster(outPath);
        var finite = values.Where(x => !double.IsNaN(x)).ToArray();
        Assert.NotEmpty(finite);
        Assert.All(finite, v => Assert.InRange(v, 0, 255));
    }

    // ─── 3.2 Contours ───

    [Fact]
    public async Task Contour_Extracts_Lines_At_Interval()
    {
        var dem = Synth.MakePlanarDem(_dir, "contour_dem.tif", 20, 6); // elevations 0..19
        var outPath = Path.Combine(_dir, "contours.geojson");
        var result = await TestEnv.RunAsync(new ContourTool(), new Dictionary<string, string>
        {
            ["input"] = dem, ["output"] = outPath, ["interval"] = "5", ["base"] = "0"
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(outPath);
        // Levels 5,10,15 ⇒ a handful of lines (allow for edge effects).
        Assert.InRange(layer.GetFeatureCount(), 2, 6);
        Assert.All(layer.Features, f =>
            Assert.Contains("LINESTRING", f.Wkt!, StringComparison.OrdinalIgnoreCase));
        Assert.All(layer.Features, f =>
            Assert.NotNull(f.GetValue("ELEV")));
    }

    // ─── 3.3 Warp / Clip / Mosaic ───

    [Fact]
    public async Task RasterWarp_Reprojects_And_Writes_Output()
    {
        var src = Synth.MakePlanarDem(_dir, "warp_src.tif", 12, 6, epsg: 32650);
        var outPath = Path.Combine(_dir, "warp_out.tif");
        var result = await TestEnv.RunAsync(new RasterWarpTool(), new Dictionary<string, string>
        {
            ["input"] = src, ["output"] = outPath, ["targetWkid"] = "4326", ["resampling"] = "nearest"
        });
        TestEnv.AssertSucceeded(result);

        Assert.True(File.Exists(outPath));
        var (w, h, _, gt) = Synth.ReadRaster(outPath);
        Assert.True(w > 0 && h > 0);
        // Reprojected into geographic CRS: pixel size now in degrees (tiny).
        Assert.True(Math.Abs(gt[1]) < 1.0, $"expected sub-degree pixel size, got {gt[1]}");
    }

    [Fact]
    public async Task RasterClip_Reduces_Extent()
    {
        var src = Synth.MakeConstantRaster(_dir, "clip_src.tif", 8, 8, value: 3, originX: 0, originY: 0);
        var outPath = Path.Combine(_dir, "clip_out.tif");
        var result = await TestEnv.RunAsync(new RasterClipTool(), new Dictionary<string, string>
        {
            ["input"] = src, ["output"] = outPath,
            ["xmin"] = "2", ["ymin"] = "-6", ["xmax"] = "6", ["ymax"] = "-2"
        });
        TestEnv.AssertSucceeded(result);

        var (w, h, _, _) = Synth.ReadRaster(outPath);
        Assert.True(w > 0 && h > 0);
        Assert.True(w < 8 && h < 8, $"clip should shrink 8x8, got {w}x{h}");
    }

    [Fact]
    public async Task RasterMosaic_Covers_Union_Extent()
    {
        var a = Synth.MakeConstantRaster(_dir, "mos_a.tif", 4, 4, value: 5, originX: 0, originY: 0, epsg: 32650);
        var b = Synth.MakeConstantRaster(_dir, "mos_b.tif", 4, 4, value: 6, originX: 4, originY: 0, epsg: 32650);
        var outPath = Path.Combine(_dir, "mosaic.tif");
        var result = await TestEnv.RunAsync(new RasterMosaicTool(), new Dictionary<string, string>
        {
            ["input1"] = a, ["input2"] = b, ["output"] = outPath, ["resampling"] = "nearest"
        });
        TestEnv.AssertSucceeded(result);

        var (w, h, _, _) = Synth.ReadRaster(outPath);
        Assert.True(w > 4, $"mosaic width should exceed a single 4-wide tile, got {w}");
        Assert.True(h >= 1);
    }

    // ─── 3.4 Zonal statistics ───

    [Fact]
    public async Task ZonalStatistics_Constant_Raster_Gives_Exact_Tally()
    {
        var raster = Synth.MakeConstantRaster(_dir, "zone.tif", 4, 4, value: 7, originX: 0, originY: 0);
        var polys = Path.Combine(_dir, "zone_poly.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POLYGON; l.Wkid = 32650;
            l.AddField(new OguField { Name = "tag", DataType = FieldDataType.STRING, Length = 10 });
            // Covers all 16 cell centres (x∈[0.5,3.5], y∈[-3.5,-0.5]).
            l.AddFeature(new OguFeature { Fid = 0, Wkt = "POLYGON ((0 0,4 0,4 -4,0 -4,0 0))" }
                .Also(f => f.SetValue("tag", "all")));
        }, polys);

        var outPath = Path.Combine(_dir, "zone_out.geojson");
        var result = await TestEnv.RunAsync(new ZonalStatisticsTool(), new Dictionary<string, string>
        {
            ["polygons"] = polys, ["raster"] = raster, ["output"] = outPath, ["band"] = "1"
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(outPath);
        var feat = layer.Features[0];
        Assert.Equal(16, Convert.ToInt32(feat.GetValue("zone_count"), System.Globalization.CultureInfo.InvariantCulture));
        var mean = Convert.ToDouble(feat.GetValue("zone_mean"), System.Globalization.CultureInfo.InvariantCulture);
        var min = Convert.ToDouble(feat.GetValue("zone_min"), System.Globalization.CultureInfo.InvariantCulture);
        var max = Convert.ToDouble(feat.GetValue("zone_max"), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(mean, 6.99, 7.01);
        Assert.InRange(min, 6.99, 7.01);
        Assert.InRange(max, 6.99, 7.01);
    }

    public void Dispose() { /* %TEMP% scratch is OS-cleaned */ }
}
