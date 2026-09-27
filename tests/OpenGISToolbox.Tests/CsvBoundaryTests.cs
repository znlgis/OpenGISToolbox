using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGISToolbox.Tools;
using Xunit;

namespace OpenGISToolbox.Tests;

/// <summary>
/// CsvToVectorTool 边界用例（数据运行时生成，保持数据无关）：
/// 引号包裹值（含内嵌逗号）、引号数字、重复表头、UTF-8 BOM 与 GBK 编码、
/// 分隔符变体、CRLF 与空行、空坐标、缺列/多列、科学计数法与负零等。
/// </summary>
public class CsvBoundaryTests : IDisposable
{
    private readonly string _dir = TestEnv.Dir("csv-boundary");

    static CsvBoundaryTests()
    {
        // GBK 等代码页编码需要注册 CodePages 提供程序（本类写测试数据时使用）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private async Task<(bool Success, string Message, OguLayer? Layer)> ConvertAsync(
        string name, string content, string delimiter = ",", string xField = "x", string yField = "y",
        Encoding? encoding = null, bool expectSuccess = true)
    {
        var csv = Path.Combine(_dir, name);
        File.WriteAllText(csv, content, encoding ?? new UTF8Encoding(false));
        var output = Path.Combine(_dir, Path.GetFileNameWithoutExtension(name) + "_out.shp");
        var result = await TestEnv.RunAsync(new CsvToVectorTool(), new Dictionary<string, string>
        {
            ["input"] = csv, ["output"] = output, ["delimiter"] = delimiter,
            ["xField"] = xField, ["yField"] = yField, ["wkid"] = "4326"
        });
        if (!expectSuccess)
            return (result.Success, result.Message, null);
        TestEnv.AssertSucceeded(result);
        return (result.Success, result.Message, TestEnv.ReadLayer(output));
    }

    private static List<string?> ValuesOf(OguLayer layer, string field) =>
        layer.Features.OrderBy(f => f.Fid).Select(f => f.GetValue(field)?.ToString()).ToList();

    private static double[] ParsePoint(string wkt)
    {
        var s = wkt.Substring(wkt.IndexOf('(') + 1, wkt.LastIndexOf(')') - wkt.IndexOf('(') - 1);
        return s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }

    [Fact]
    public async Task Standard_Csv_Converts_All_Rows_With_Attributes()
    {
        var (_, _, layer) = await ConvertAsync("std.csv",
            "x,y,name\n116.4,39.9,beijing\n121.5,31.2,shanghai\n113.3,23.1,guangzhou\n");
        Assert.Equal(3, layer!.GetFeatureCount());
        Assert.Equal(new[] { "beijing", "shanghai", "guangzhou" }, ValuesOf(layer, "name"));
    }

    [Fact]
    public async Task Delimiter_Variants_Semicolon_And_Tab()
    {
        var (_, _, semi) = await ConvertAsync("semi.csv",
            "x;y;name\n116.4;39.9;beijing\n121.5;31.2;shanghai\n", delimiter: ";");
        Assert.Equal(2, semi!.GetFeatureCount());
        Assert.Equal(new[] { "beijing", "shanghai" }, ValuesOf(semi, "name"));

        var (_, _, tab) = await ConvertAsync("tab.tsv",
            "x\ty\tname\n116.4\t39.9\tbeijing\n121.5\t31.2\tshanghai\n", delimiter: "\t");
        Assert.Equal(2, tab!.GetFeatureCount());
    }

    [Fact]
    public async Task Quoted_Fields_Preserve_Embedded_Commas()
    {
        var (_, _, layer) = await ConvertAsync("quoted.csv",
            "x,y,name\n116.4,39.9,\"北京, 中国\"\n121.5,31.2,\"Shanghai, CN\"\n113.3,23.1,\"GZ, Guangdong\"\n");
        Assert.Equal(3, layer!.GetFeatureCount());
        Assert.Equal(new[] { "北京, 中国", "Shanghai, CN", "GZ, Guangdong" }, ValuesOf(layer, "name"));
    }

    [Fact]
    public async Task Quoted_Numbers_Are_Parsed()
    {
        var (_, _, layer) = await ConvertAsync("quoted_num.csv", "x,y\n\"116.4\",39.9\n\"121.5\",31.2\n");
        Assert.Equal(2, layer!.GetFeatureCount());
    }

    [Fact]
    public async Task Duplicate_Headers_Use_First_Matching_Column()
    {
        var (_, _, layer) = await ConvertAsync("dup_headers.csv", "x,y,x\n116.4,39.9,foo\n121.5,31.2,bar\n");
        Assert.Equal(2, layer!.GetFeatureCount());
        // 首个 x 列作为坐标；后出现的同名列按普通属性保留
        Assert.Equal(new[] { "foo", "bar" }, ValuesOf(layer, "x"));
    }

    [Fact]
    public async Task Utf8_Bom_Chinese_Headers_And_Values()
    {
        var (_, _, layer) = await ConvertAsync("bom_cn.csv",
            "经度,纬度,名称\n116.4,39.9,北京\n121.5,31.2,上海\n113.3,23.1,广州\n",
            xField: "经度", yField: "纬度", encoding: new UTF8Encoding(true));
        Assert.Equal(3, layer!.GetFeatureCount());
        Assert.Equal(new[] { "北京", "上海", "广州" }, ValuesOf(layer, "名称"));
    }

    [Fact]
    public async Task Gbk_Encoded_Chinese_Values_Are_Decoded()
    {
        var (_, _, layer) = await ConvertAsync("gbk.csv",
            "x,y,name\n116.4,39.9,北京\n121.5,31.2,上海\n",
            encoding: Encoding.GetEncoding("GBK"));
        Assert.Equal(2, layer!.GetFeatureCount());
        Assert.Equal(new[] { "北京", "上海" }, ValuesOf(layer, "name"));
    }

    [Fact]
    public async Task Scientific_Notation_And_Negative_Zero()
    {
        var (_, _, sci) = await ConvertAsync("sci.csv", "x,y\n1.164e2,3.99e1\n-1.164e2,-3.99e1\n1.21E2,3.12E1\n");
        Assert.Equal(3, sci!.GetFeatureCount());
        var first = ParsePoint(sci.Features.OrderBy(f => f.Fid).First().Wkt!);
        Assert.InRange(first[0], 116.39, 116.41);
        Assert.InRange(first[1], 39.89, 39.91);

        var (_, _, nz) = await ConvertAsync("neg_zero.csv", "x,y\n0,0\n-0.0,-0.0\n-180,-90\n180,90\n");
        Assert.Equal(4, nz!.GetFeatureCount());
    }

    [Fact]
    public async Task Surrounding_Spaces_Are_Trimmed()
    {
        var (_, _, layer) = await ConvertAsync("spaces.csv",
            "x , y , name\n 116.4 , 39.9 , beijing \n 121.5 , 31.2 , shanghai \n");
        Assert.Equal(2, layer!.GetFeatureCount());
        Assert.Equal(new[] { "beijing", "shanghai" }, ValuesOf(layer, "name"));
    }

    [Fact]
    public async Task Ragged_Rows_Missing_And_Extra_Columns()
    {
        var (_, _, layer) = await ConvertAsync("ragged.csv",
            "x,y,name\n116.4,39.9\n121.5,31.2,shanghai,extra\n113.3,23.1,guangzhou\n");
        Assert.Equal(3, layer!.GetFeatureCount());
        var names = ValuesOf(layer, "name");
        Assert.True(string.IsNullOrEmpty(names[0]));                              // 缺列 → 空值
        Assert.Equal(new[] { "shanghai", "guangzhou" }, names.Skip(1).ToList()); // 多列 → 忽略多余值
    }

    [Fact]
    public async Task Empty_Coordinates_Are_Skipped_And_Counted()
    {
        var (_, message, layer) = await ConvertAsync("empty_xy.csv",
            "x,y\n116.4,39.9\n,39.9\n121.5,\n,\n113.3,23.1\n");
        Assert.Equal(2, layer!.GetFeatureCount());
        Assert.Matches("跳过 3 行|3 rows skipped", message);
    }

    [Fact]
    public async Task Header_Only_Fails_With_Clear_Message()
    {
        var (success, message, _) = await ConvertAsync("header_only.csv", "x,y\n", expectSuccess: false);
        Assert.False(success);
        Assert.Matches("表头|header", message);
    }

    [Fact]
    public async Task Blank_Lines_Ignored_And_Crlf_Parsed()
    {
        var (_, _, blanks) = await ConvertAsync("blank_lines.csv", "x,y\n\n116.4,39.9\n\n\n121.5,31.2\n\n");
        Assert.Equal(2, blanks!.GetFeatureCount());

        var (_, _, crlf) = await ConvertAsync("crlf.csv", "x,y,name\r\n116.4,39.9,beijing\r\n121.5,31.2,shanghai\r\n");
        Assert.Equal(2, crlf!.GetFeatureCount());
        Assert.Equal(new[] { "beijing", "shanghai" }, ValuesOf(crlf, "name"));
    }

    [Fact]
    public async Task Single_Data_Row_Converts()
    {
        var (_, _, layer) = await ConvertAsync("one_row.csv", "x,y\n116.4,39.9\n");
        Assert.Equal(1, layer!.GetFeatureCount());
    }

    public void Dispose() { }
}
