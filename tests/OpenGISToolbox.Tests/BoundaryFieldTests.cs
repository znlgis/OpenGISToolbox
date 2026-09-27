using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGISToolbox.Tools;
using Xunit;

namespace OpenGISToolbox.Tests;

/// <summary>
/// 边界字段测试（数据运行时生成，保持数据无关）：
/// 日期字段（闰日、最小/最大日期、空值）经 GeoJSON/GPKG/KML 转换的保真与查询；
/// Unicode/中文属性（拉丁、emoji、引号、逗号）在 SHP↔GeoJSON↔GPKG 的保真、
/// 中文 WHERE 查询、中文目录/文件名输入输出。
/// </summary>
public class BoundaryFieldTests : IDisposable
{
    private readonly string _dir = TestEnv.Dir("boundary-fields");

    // ─── fixtures ───

    private string MakeDateFieldsShp(string name = "date_fields.shp")
    {
        var path = Path.Combine(_dir, name);
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POINT;
            l.Wkid = 4326;
            l.AddField(new OguField { Name = "id", DataType = FieldDataType.INTEGER });
            l.AddField(new OguField { Name = "label", DataType = FieldDataType.STRING, Length = 40 });
            l.AddField(new OguField { Name = "d_std", DataType = FieldDataType.DATE });
            l.AddField(new OguField { Name = "d_leap", DataType = FieldDataType.DATE });
            l.AddField(new OguField { Name = "d_null", DataType = FieldDataType.DATE });

            void Add(int fid, string label, DateTime? dStd, DateTime? dLeap, DateTime? dNull)
            {
                l.AddFeature(new OguFeature { Fid = fid, Wkt = $"POINT ({100 + fid} {30 + fid})" }
                    .Also(f =>
                    {
                        f.SetValue("id", fid + 1);
                        f.SetValue("label", label);
                        f.SetValue("d_std", dStd);
                        f.SetValue("d_leap", dLeap);
                        f.SetValue("d_null", dNull);
                    }));
            }

            Add(0, "normal", new DateTime(2024, 1, 15), new DateTime(2024, 2, 29), null);
            Add(1, "all-null", null, null, null);
            Add(2, "leap-2000", new DateTime(2000, 2, 29), null, null);
            Add(3, "min-date", new DateTime(1900, 1, 1), null, null);
            Add(4, "max-date", new DateTime(9999, 12, 31), null, null);
            Add(5, "same", new DateTime(2024, 6, 15), new DateTime(2024, 6, 15), new DateTime(2024, 6, 15));
        }, path);
        return path;
    }

    private string MakeUnicodeShp(string name = "unicode_attrs.shp")
    {
        var path = Path.Combine(_dir, name);
        var rows = new[]
        {
            (1, "北京市朝阳区建国门外大街1号", "GIS工具箱 v1.1 测试", "😀🎉🌍", "He said \"hello\" & 'bye'"),
            (2, "上海市浦东新区", "中英mix文本ABC", "🧭🚩", "双引号\"内嵌\"测试"),
            (3, "广州市天河区", "坐标:116.4,39.9", "🚀", "单引号 'quote'"),
            (4, "深圳市南山区", "值with,comma", "😅", "back\\slash\\path"),
            (5, "成都市武侯区", "percent%100", "🎯", "braces{curly}"),
        };
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POINT;
            l.Wkid = 4326;
            l.AddField(new OguField { Name = "id", DataType = FieldDataType.INTEGER });
            l.AddField(new OguField { Name = "name_zh", DataType = FieldDataType.STRING, Length = 120 });
            l.AddField(new OguField { Name = "name_mix", DataType = FieldDataType.STRING, Length = 120 });
            l.AddField(new OguField { Name = "emoji", DataType = FieldDataType.STRING, Length = 60 });
            l.AddField(new OguField { Name = "quotes", DataType = FieldDataType.STRING, Length = 120 });
            foreach (var (id, zh, mix, emoji, quotes) in rows)
            {
                var fid = id - 1;
                l.AddFeature(new OguFeature { Fid = fid, Wkt = $"POINT ({80 + fid * 5} {20 + fid * 4})" }
                    .Also(f =>
                    {
                        f.SetValue("id", id);
                        f.SetValue("name_zh", zh);
                        f.SetValue("name_mix", mix);
                        f.SetValue("emoji", emoji);
                        f.SetValue("quotes", quotes);
                    }));
            }
        }, path);
        return path;
    }

    // ─── helpers ───

    private static FormatConversionTool ConvertTool(DataFormatType dst, string dstExt)
        => new("boundary-convert", "Boundary Convert", "边界转换",
            "Convert boundary layer", "转换边界图层",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            dst, dstExt, "out|*" + dstExt);

    private async Task<string> ConvertAsync(string input, string output, DataFormatType dst)
    {
        var tool = ConvertTool(dst, Path.GetExtension(output));
        var result = await TestEnv.RunAsync(tool, new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);
        return output;
    }

    /// <summary>Normalizes any date-ish value (DateTime, "2024/01/15", "2024-01-15T00:00:00") to ISO yyyy-MM-dd.</summary>
    private static string? IsoDate(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case DateTime dt:
                return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case DateOnly d:
                return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            default:
                var s = value.ToString()?.Trim() ?? "";
                if (s.Length == 0) return null;
                if (DateTime.TryParseExact(s,
                        new[] { "yyyy-MM-dd", "yyyy/MM/dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
                    return exact.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : s;
        }
    }

    private static Dictionary<string, OguFeature> ByLabel(OguLayer layer)
        => layer.Features.ToDictionary(f => f.GetValue("label")?.ToString() ?? "", f => f);

    // ─── 1. 日期字段：GeoJSON / GPKG 往返 ───

    [Fact]
    public async Task DateFields_Survive_GeoJson_And_Gpkg_RoundTrips()
    {
        var src = MakeDateFieldsShp();

        var gj = Path.Combine(_dir, "dates.geojson");
        await ConvertAsync(src, gj, DataFormatType.GEOJSON);
        var geojson = TestEnv.ReadLayer(gj);
        Assert.Equal(6, geojson.GetFeatureCount());
        var g = ByLabel(geojson);
        Assert.Equal("2024-01-15", IsoDate(g["normal"].GetValue("d_std")));
        Assert.Equal("2024-02-29", IsoDate(g["normal"].GetValue("d_leap")));
        Assert.Null(IsoDate(g["normal"].GetValue("d_null")));
        Assert.Equal("2000-02-29", IsoDate(g["leap-2000"].GetValue("d_std")));
        Assert.Equal("1900-01-01", IsoDate(g["min-date"].GetValue("d_std")));
        Assert.Equal("9999-12-31", IsoDate(g["max-date"].GetValue("d_std")));
        Assert.Equal("2024-06-15", IsoDate(g["same"].GetValue("d_null")));
        Assert.Null(IsoDate(g["all-null"].GetValue("d_std")));

        var gpkg = Path.Combine(_dir, "dates.gpkg");
        await ConvertAsync(src, gpkg, DataFormatType.GEOPACKAGE);
        var gk = TestEnv.ReadLayer(gpkg);
        Assert.Equal(6, gk.GetFeatureCount());
        var kg = ByLabel(gk);
        Assert.Equal("2024-01-15", IsoDate(kg["normal"].GetValue("d_std")));
        Assert.Equal("9999-12-31", IsoDate(kg["max-date"].GetValue("d_std")));
        Assert.Null(IsoDate(kg["all-null"].GetValue("d_std")));
    }

    // ─── 2. 日期字段：KML（1.1.0 引擎降级 ISO 文本后应整层成功） ───

    [Fact]
    public async Task DateFields_Kml_Conversion_Succeeds_With_Iso_Values()
    {
        // 1.0.8：含 DATE 列的图层转 KML 整层失败（"Export of geometry to KML failed"）；
        // 1.1.0：日期列降级为文本列后转换成功，但驱动内建字段未计入索引映射、值整体错位；
        // 1.1.1：字段映射修正后，日期列降级为 string 且各字段值对齐，可严格断言。
        var src = MakeDateFieldsShp();
        var kml = Path.Combine(_dir, "dates.kml");
        await ConvertAsync(src, kml, DataFormatType.KML);

        var layer = TestEnv.ReadLayer(kml);
        Assert.Equal(6, layer.GetFeatureCount());
        var byLabel = ByLabel(layer);
        Assert.Equal("2024-01-15", IsoDate(byLabel["normal"].GetValue("d_std")));
        Assert.Equal("2024-02-29", IsoDate(byLabel["normal"].GetValue("d_leap")));
        Assert.Equal("2000-02-29", IsoDate(byLabel["leap-2000"].GetValue("d_std")));
        Assert.Equal("9999-12-31", IsoDate(byLabel["max-date"].GetValue("d_std")));
        Assert.Equal("2024-06-15", IsoDate(byLabel["same"].GetValue("d_null")));
        Assert.Null(IsoDate(byLabel["all-null"].GetValue("d_std")));
        // 注：KML 的 Placemark id（"<图层>.<FID>"）读回时会占用名为 "id" 的字段，
        // 与源数据同名字段的冲突属 KML 格式特性，故此处不对 id 列做断言。

        var text = File.ReadAllText(kml);
        Assert.Contains("SimpleField name=\"d_std\" type=\"string\"", text);
    }

    // ─── 3. 日期字段：属性查询 ───

    [Fact]
    public async Task DateFields_Support_Attribute_Queries()
    {
        var src = MakeDateFieldsShp();

        var byLabel = Path.Combine(_dir, "q_label.geojson");
        var r1 = await TestEnv.RunAsync(new AttributeQueryTool(), new Dictionary<string, string>
        {
            ["input"] = src, ["output"] = byLabel, ["whereClause"] = "label = 'normal'"
        });
        TestEnv.AssertSucceeded(r1);
        var l1 = TestEnv.ReadLayer(byLabel);
        Assert.Equal(1, l1.GetFeatureCount());
        Assert.Equal("2024-01-15", IsoDate(l1.Features[0].GetValue("d_std")));

        var notNull = Path.Combine(_dir, "q_notnull.geojson");
        var r2 = await TestEnv.RunAsync(new AttributeQueryTool(), new Dictionary<string, string>
        {
            ["input"] = src, ["output"] = notNull, ["whereClause"] = "d_std IS NOT NULL"
        });
        TestEnv.AssertSucceeded(r2);
        // 6 行中仅 all-null 行的 d_std 为空
        Assert.Equal(5, TestEnv.ReadLayer(notNull).GetFeatureCount());
    }

    // ─── 4. Unicode/中文：GeoJSON / GPKG 保真 ───

    [Fact]
    public async Task Unicode_Values_Survive_Shp_To_GeoJson_And_Gpkg()
    {
        var src = MakeUnicodeShp();

        var gj = Path.Combine(_dir, "unicode.geojson");
        await ConvertAsync(src, gj, DataFormatType.GEOJSON);
        var layer = TestEnv.ReadLayer(gj);
        Assert.Equal(5, layer.GetFeatureCount());
        var byId = layer.Features.ToDictionary(f => Convert.ToInt32(f.GetValue("id"), CultureInfo.InvariantCulture));
        Assert.Equal("北京市朝阳区建国门外大街1号", byId[1].GetValue("name_zh")?.ToString());
        Assert.Equal("😀🎉🌍", byId[1].GetValue("emoji")?.ToString());
        Assert.Equal("He said \"hello\" & 'bye'", byId[1].GetValue("quotes")?.ToString());
        Assert.Equal("值with,comma", byId[4].GetValue("name_mix")?.ToString());
        Assert.Equal("back\\slash\\path", byId[4].GetValue("quotes")?.ToString());

        var gpkg = Path.Combine(_dir, "unicode.gpkg");
        await ConvertAsync(src, gpkg, DataFormatType.GEOPACKAGE);
        var gk = TestEnv.ReadLayer(gpkg);
        var kg = gk.Features.ToDictionary(f => Convert.ToInt32(f.GetValue("id"), CultureInfo.InvariantCulture));
        Assert.Equal("上海市浦东新区", kg[2].GetValue("name_zh")?.ToString());
        Assert.Equal("🚀", kg[3].GetValue("emoji")?.ToString());
        Assert.Equal("双引号\"内嵌\"测试", kg[2].GetValue("quotes")?.ToString());
    }

    // ─── 5. 中文 WHERE 查询 ───

    [Fact]
    public async Task Chinese_Where_Clause_Selects_Exact_And_Like_Matches()
    {
        var src = MakeUnicodeShp();

        var exact = Path.Combine(_dir, "q_cn.geojson");
        var r1 = await TestEnv.RunAsync(new AttributeQueryTool(), new Dictionary<string, string>
        {
            ["input"] = src, ["output"] = exact,
            ["whereClause"] = "name_zh = '北京市朝阳区建国门外大街1号'"
        });
        TestEnv.AssertSucceeded(r1);
        var l1 = TestEnv.ReadLayer(exact);
        Assert.Equal(1, l1.GetFeatureCount());
        Assert.Equal("北京市朝阳区建国门外大街1号", l1.Features[0].GetValue("name_zh")?.ToString());

        var like = Path.Combine(_dir, "q_cn_like.geojson");
        var r2 = await TestEnv.RunAsync(new AttributeQueryTool(), new Dictionary<string, string>
        {
            ["input"] = src, ["output"] = like, ["whereClause"] = "name_zh LIKE '上海市%'"
        });
        TestEnv.AssertSucceeded(r2);
        var l2 = TestEnv.ReadLayer(like);
        Assert.Equal(1, l2.GetFeatureCount());
        Assert.Equal("上海市浦东新区", l2.Features[0].GetValue("name_zh")?.ToString());
    }

    // ─── 6. 中文目录与文件名 ───

    [Fact]
    public async Task Chinese_Directory_And_File_Names_Work_End_To_End()
    {
        var cnDir = Path.Combine(_dir, "中文目录_测试");
        Directory.CreateDirectory(cnDir);
        var src = Path.Combine(cnDir, "城市数据_中文.shp");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POINT;
            l.Wkid = 4326;
            l.AddField(new OguField { Name = "city", DataType = FieldDataType.STRING, Length = 20 });
            l.AddFeature(new OguFeature { Fid = 0, Wkt = "POINT (116.397 39.909)" }.Also(f => f.SetValue("city", "北京")));
            l.AddFeature(new OguFeature { Fid = 1, Wkt = "POINT (121.474 31.231)" }.Also(f => f.SetValue("city", "上海")));
            l.AddFeature(new OguFeature { Fid = 2, Wkt = "POINT (113.265 23.129)" }.Also(f => f.SetValue("city", "广州")));
        }, src);

        var outPath = Path.Combine(cnDir, "转换输出_中文.geojson");
        await ConvertAsync(src, outPath, DataFormatType.GEOJSON);

        var layer = TestEnv.ReadLayer(outPath);
        Assert.Equal(3, layer.GetFeatureCount());
        var names = layer.Features.Select(f => f.GetValue("city")?.ToString()).ToList();
        Assert.Contains("北京", names);
        Assert.Contains("上海", names);
        Assert.Contains("广州", names);
    }

    public void Dispose() { }
}
