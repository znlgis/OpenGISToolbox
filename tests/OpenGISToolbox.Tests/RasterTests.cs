using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using MaxRev.Gdal.Core;
using OpenGISToolbox.Tools;
using OSGeo.GDAL;
using Xunit;

namespace OpenGISToolbox.Tests;

public class RasterTests : IDisposable
{
    private readonly string _dir = TestEnv.Dir("raster");
    private static bool _gdalReady;

    private static void InitGdal()
    {
        if (!_gdalReady) { GdalBase.ConfigureAll(); _gdalReady = true; }
    }

    /// <summary>Creates a 4-band float GeoTIFF with known values.</summary>
    private string MakeTif(string name, int width = 8, int height = 8, int bands = 4, DataType dtype = DataType.GDT_Float64)
    {
        InitGdal();
        var path = Path.Combine(_dir, name);
        var driver = Gdal.GetDriverByName("GTiff") ?? throw new InvalidOperationException("GTiff driver missing");
        using var ds = driver.Create(path, width, height, bands, dtype, null);
        // band1: 1..64 ; band3 (red): constant 2 ; band4 (nir): constant 8
        for (int b = 1; b <= bands; b++)
        {
            var data = new double[width * height];
            for (int i = 0; i < data.Length; i++)
                data[i] = b == 1 ? (i + 1) % 256 : (b == 3 ? 2.0 : (b == 4 ? 8.0 : 0.0));
            ds.GetRasterBand(b).WriteRaster(0, 0, width, height, data, width, height, 0, 0);
            ds.GetRasterBand(b).FlushCache();
        }
        ds.FlushCache();
        return path;
    }

    private static double[] ReadBand(string path, int band = 1)
    {
        InitGdal();
        using var ds = Gdal.Open(path, Access.GA_ReadOnly);
        var w = ds.RasterXSize; var h = ds.RasterYSize;
        var buf = new double[w * h];
        ds.GetRasterBand(band).ReadRaster(0, 0, w, h, buf, w, h, 0, 0);
        return buf;
    }

    [Fact]
    public async Task RasterCalculator_Threshold_Produces_Binary_Values()
    {
        var input = MakeTif("src.tif");
        var output = Path.Combine(_dir, "threshold.tif");
        var result = await TestEnv.RunAsync(new RasterCalculatorTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output, ["operation"] = "Threshold (Band1 > Value)", ["value"] = "32"
        });
        TestEnv.AssertSucceeded(result);

        var data = ReadBand(output);
        Assert.All(data, v => Assert.True(v == 0 || v == 1, $"Non-binary value {v}"));
        Assert.Contains(data, v => v == 0);
        Assert.Contains(data, v => v == 1);
        // values 1..64, threshold > 32 => 32 ones
        var ones = data.Count(v => v == 1);
        Assert.True(ones == 32, $"Expected 32 ones (values 1..64, threshold >32), got {ones}");
    }

    [Fact]
    public async Task RasterCalculator_Scale_Multiplies_Values()
    {
        var input = MakeTif("src2.tif");
        var output = Path.Combine(_dir, "scale.tif");
        var result = await TestEnv.RunAsync(new RasterCalculatorTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output, ["operation"] = "Scale (Band1 * Factor)", ["value"] = "10"
        });
        TestEnv.AssertSucceeded(result);

        var data = ReadBand(output);
        Assert.Equal(10.0, data[0], 5);   // 1 * 10
        Assert.Equal(640.0, data[^1], 5); // 64 * 10
    }

    [Fact]
    public async Task RasterCalculator_Offset_Adds_Value()
    {
        var input = MakeTif("src3.tif");
        var output = Path.Combine(_dir, "offset.tif");
        var result = await TestEnv.RunAsync(new RasterCalculatorTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output, ["operation"] = "Offset (Band1 + Value)", ["value"] = "0.5"
        });
        TestEnv.AssertSucceeded(result);

        var data = ReadBand(output);
        Assert.Equal(1.5, data[0], 5);
        Assert.Equal(64.5, data[^1], 5);
    }

    [Fact]
    public async Task RasterCalculator_NDVI_Computes_Expected_Ratio()
    {
        var input = MakeTif("src4.tif"); // red=2, nir=8
        var output = Path.Combine(_dir, "ndvi.tif");
        var result = await TestEnv.RunAsync(new RasterCalculatorTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output, ["operation"] = "NDVI (Band4-Band3)/(Band4+Band3)", ["value"] = "0"
        });
        TestEnv.AssertSucceeded(result);

        var data = ReadBand(output);
        var expected = (8.0 - 2.0) / (8.0 + 2.0); // 0.6
        Assert.All(data, v => Assert.Equal(expected, v, 4));
    }

    [Fact]
    public async Task RasterFormatConvert_Converts_To_Png()
    {
        var input = MakeTif("src5.tif", bands: 1, dtype: DataType.GDT_Byte);
        var output = Path.Combine(_dir, "out.png");
        var result = await TestEnv.RunAsync(new RasterFormatConvertTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);
        Assert.True(File.Exists(output));
        InitGdal();
        using var ds = Gdal.Open(output, Access.GA_ReadOnly);
        Assert.Equal(8, ds.RasterXSize);
        Assert.Equal(8, ds.RasterYSize);
    }

    public void Dispose() { }
}
