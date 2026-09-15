using OpenGISToolbox.Models;
using OpenGISToolbox.TestKit;

namespace OpenGISToolbox.RealDataHarness.Sections;

/// <summary>
/// Robustness matrix over the whole registry: empty parameters, garbage input
/// files, invalid numeric/EPSG values must all produce a graceful failed
/// ToolResult — never an uncaught crash and never a false success.
/// </summary>
public static class ErrorMatrix
{
    public static async Task Run()
    {
        Program.Checks.BeginSection("9. Error / robustness matrix (all registered tools)");

        var dir = GdalEnv.Dir("harness", "errors");

        // One shared garbage payload per file extension the tools accept.
        var junk = new Dictionary<string, string>
        {
            [".shp"] = MakeJunk(dir, "junk.shp"),
            [".geojson"] = MakeJunk(dir, "junk.geojson"),
            [".gpkg"] = MakeJunk(dir, "junk.gpkg"),
            [".kml"] = MakeJunk(dir, "junk.kml"),
            [".dxf"] = MakeJunk(dir, "junk.dxf"),
            [".csv"] = MakeJunk(dir, "junk.csv"),
            [".tif"] = MakeJunk(dir, "junk.tif"),
            [".gpx"] = MakeJunk(dir, "junk.gpx"),
            [".txt"] = MakeJunk(dir, "junk.txt"),
            [".zip"] = MakeJunk(dir, "junk.zip"),
            [".gdb"] = MakeJunkDir(dir, "junk.gdb"),
        };

        // 1) Every registered tool with completely empty parameters must fail cleanly.
        int emptyOk = 0;
        foreach (var info in Program.Registry.Values)
        {
            await Program.Checks.RunAsync($"{info.Id}: empty parameters rejected", async () =>
            {
                var result = await GdalEnv.RunToolAsync(info, new Dictionary<string, string>());
                Program.Require(!result.Success, $"{info.Id} empty params", "unexpected success");
                emptyOk++;
            });
        }

        // 2) Every tool with an input-file parameter must reject a garbage file of the right extension.
        //    Network-backed tools (geocode) are intentionally lenient: a garbage address
        //    file yields a partial CSV, not a hard failure, so they are excluded here.
        var lenientInputTools = new HashSet<string> { "geocode-addresses" };
        foreach (var info in Program.Registry.Values)
        {
            if (lenientInputTools.Contains(info.Id)) continue;
            var inputParam = info.Parameters.FirstOrDefault(p =>
                p.Type == ParameterType.InputFile && p.Required);
            if (inputParam == null) continue;
            var filter = inputParam.FileFilter ?? "";
            var ext = GuessExtension(filter, junk.Keys);
            if (ext == null) continue;

            await Program.Checks.RunAsync($"{info.Id}: garbage {ext} input rejected", async () =>
            {
                var parameters = new Dictionary<string, string> { [inputParam.Name] = junk[ext] };
                foreach (var p in info.Parameters)
                {
                    if (parameters.ContainsKey(p.Name) || !p.Required) continue;
                    parameters[p.Name] = p.Type switch
                    {
                        ParameterType.OutputFile or ParameterType.FolderPath => Path.Combine(dir, $"out_{info.Id}"),
                        ParameterType.Number or ParameterType.Integer => "1",
                        ParameterType.Dropdown => p.Options?.FirstOrDefault() ?? "1",
                        _ => p.DefaultValue ?? "x"
                    };
                }
                var result = await GdalEnv.RunToolAsync(info, parameters);
                Program.Require(!result.Success, $"{info.Id} junk input", "unexpected success on garbage data");
            });
        }

        // 3) Value-level validation on tools that parse numbers/EPSG/SQL.
        var poly = Program.Catalog.Polygon;
        if (poly != null)
        {
            await Program.Checks.RunAsync("invalid EPSG rejected", async () =>
            {
                var result = await GdalEnv.RunToolAsync(Program.Registry["reproject"], new()
                {
                    ["input"] = poly.ShpPath,
                    ["output"] = Path.Combine(dir, "bad_epsg.shp"),
                    ["sourceWkid"] = "4326", ["targetWkid"] = "99999999"
                });
                Program.Require(!result.Success, "invalid target wkid", "unexpected success");
            });

            await Program.Checks.RunAsync("non-numeric buffer distance rejected", async () =>
            {
                var result = await GdalEnv.RunToolAsync(Program.Registry["buffer"], new()
                {
                    ["input"] = poly.ShpPath,
                    ["output"] = Path.Combine(dir, "bad_buf.shp"),
                    ["distance"] = "not-a-number"
                });
                Program.Require(!result.Success, "bad distance", "unexpected success");
            });

            await Program.Checks.RunAsync("invalid extent WKT rejected", async () =>
            {
                var result = await GdalEnv.RunToolAsync(Program.Registry["spatial-filter"], new()
                {
                    ["input"] = poly.ShpPath,
                    ["output"] = Path.Combine(dir, "bad_filter.shp"),
                    ["extentWkt"] = "POLYGON ((garbage"
                });
                Program.Require(!result.Success, "bad extent wkt", "unexpected success");
            });

            await Program.Checks.RunAsync("bad SQL where clause rejected or empty", async () =>
            {
                var result = await GdalEnv.RunToolAsync(Program.Registry["attribute-query"], new()
                {
                    ["input"] = poly.ShpPath,
                    ["output"] = Path.Combine(dir, "bad_sql.shp"),
                    ["whereClause"] = "no_such_field_zz = 'x' AND (("
                });
                Program.Require(!result.Success, "bad where clause", "unexpected success");
            });

            await Program.Checks.RunAsync("empty layer handled", async () =>
            {
                var empty = Path.Combine(dir, "empty.shp");
                WriteEmptyPolygonLayer(empty);
                var result = await GdalEnv.RunToolAsync(Program.Registry["buffer"], new()
                {
                    ["input"] = empty, ["output"] = Path.Combine(dir, "empty_buf.shp"), ["distance"] = "1"
                });
                // Either a clean failure or success producing an empty output — never a crash.
                Program.Require(result.Success || result.Message.Length > 0, "empty layer graceful",
                    "no message and failure");
            });
        }

        Console.WriteLine($"  ({emptyOk} tools covered by the empty-parameter matrix)");
    }

    static string MakeJunk(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        var bytes = new byte[2048];
        new Random(1234).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    static string MakeJunkDir(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "garbage.txt"), "not a gdb");
        return path;
    }

    static string? GuessExtension(string? fileFilter, IEnumerable<string> available)
    {
        if (string.IsNullOrEmpty(fileFilter)) return null;
        foreach (var ext in available)
            if (fileFilter.Contains("*" + ext, StringComparison.OrdinalIgnoreCase))
                return ext;
        return null;
    }

    static void WriteEmptyPolygonLayer(string path)
    {
        GdalEnv.Ensure();
        var layer = new OpenGIS.Utils.Engine.Model.Layer.OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(path),
            GeometryType = OpenGIS.Utils.Engine.Enums.GeometryType.POLYGON,
            Wkid = 4326
        };
        OpenGIS.Utils.DataSource.OguLayerUtil.WriteLayer(
            OpenGIS.Utils.Engine.Enums.DataFormatType.SHP, layer, path);
    }
}
