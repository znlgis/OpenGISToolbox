using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenGISToolbox.Models;
using OpenGISToolbox.Services;
using OpenGISToolbox.Tools;
using Xunit;

namespace OpenGISToolbox.Tests;

public class SmokeTests : IDisposable
{
    private readonly string _dir = TestEnv.Dir("smoke");

    [Fact]
    public void Registry_Contains_All_Tools_With_Unique_Ids()
    {
        var tools = ToolRegistry.GetAllTools();
        Assert.True(tools.Count >= 30, $"Expected >=30 tools, got {tools.Count}");
        var ids = tools.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(tools, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.NameZh));
            Assert.NotNull(t.ExecuteAsync);
            Assert.NotEmpty(t.Parameters);
        });
    }

    [Fact]
    public async Task Missing_Required_Input_File_Fails_Gracefully()
    {
        var result = await TestEnv.RunAsync(new BufferTool(), new Dictionary<string, string>
        {
            ["output"] = Path.Combine(_dir, "out.shp"),
            ["distance"] = "1.0"
            // input missing
        });
        Assert.False(result.Success);
        Assert.Contains("input", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nonexistent_Input_File_Fails_Gracefully()
    {
        var result = await TestEnv.RunAsync(new BufferTool(), new Dictionary<string, string>
        {
            ["input"] = Path.Combine(_dir, "does-not-exist.shp"),
            ["output"] = Path.Combine(_dir, "out.shp"),
            ["distance"] = "1.0"
        });
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Corrupt_Input_File_Fails_Gracefully()
    {
        var corrupt = Path.Combine(_dir, "corrupt.geojson");
        File.WriteAllText(corrupt, "{ not valid geojson !!!");
        var result = await TestEnv.RunAsync(new BufferTool(), new Dictionary<string, string>
        {
            ["input"] = corrupt,
            ["output"] = Path.Combine(_dir, "out.geojson"),
            ["distance"] = "1.0"
        });
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Invalid_Distance_Fails_Gracefully()
    {
        var input = TestEnv.MakePointsGeoJson(_dir);
        var result = await TestEnv.RunAsync(new BufferTool(), new Dictionary<string, string>
        {
            ["input"] = input,
            ["output"] = Path.Combine(_dir, "out.geojson"),
            ["distance"] = "not-a-number"
        });
        Assert.False(result.Success);
    }

    public void Dispose()
    {
        // temp dir under %TEMP% is cleaned by OS; nothing else needed
    }
}
