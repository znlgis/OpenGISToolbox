using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGIS.Utils.Engine.Util;
using OpenGIS.Utils.Geometry;
using OpenGIS.Utils.Utils;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Services;

public static class ToolRegistry
{
    public static List<ToolInfo> GetAllTools()
    {
        var tools = new List<ToolInfo>();

        // Conversion tools
        tools.Add(CreateConversionTool("shp-to-geojson", "SHP → GeoJSON", "Convert Shapefile to GeoJSON format",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.GEOJSON, ".geojson", "GeoJSON|*.geojson"));

        tools.Add(CreateConversionTool("geojson-to-shp", "GeoJSON → SHP", "Convert GeoJSON to Shapefile format",
            DataFormatType.GEOJSON, ".geojson", "GeoJSON|*.geojson",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp"));

        tools.Add(CreateConversionTool("shp-to-kml", "SHP → KML", "Convert Shapefile to KML format",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.KML, ".kml", "KML|*.kml"));

        tools.Add(CreateConversionTool("kml-to-shp", "KML → SHP", "Convert KML to Shapefile format",
            DataFormatType.KML, ".kml", "KML|*.kml",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp"));

        tools.Add(CreateConversionTool("shp-to-gpkg", "SHP → GeoPackage", "Convert Shapefile to GeoPackage format",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.GEOPACKAGE, ".gpkg", "GeoPackage|*.gpkg"));

        tools.Add(CreateConversionTool("gpkg-to-shp", "GeoPackage → SHP", "Convert GeoPackage to Shapefile format",
            DataFormatType.GEOPACKAGE, ".gpkg", "GeoPackage|*.gpkg",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp"));

        tools.Add(CreateConversionTool("shp-to-dxf", "SHP → DXF", "Convert Shapefile to DXF format",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.DXF, ".dxf", "DXF|*.dxf"));

        tools.Add(CreateConversionTool("dxf-to-shp", "DXF → SHP", "Convert DXF to Shapefile format",
            DataFormatType.DXF, ".dxf", "DXF|*.dxf",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp"));

        tools.Add(CreateConversionTool("geojson-to-kml", "GeoJSON → KML", "Convert GeoJSON to KML format",
            DataFormatType.GEOJSON, ".geojson", "GeoJSON|*.geojson",
            DataFormatType.KML, ".kml", "KML|*.kml"));

        tools.Add(CreateConversionTool("geojson-to-gpkg", "GeoJSON → GeoPackage", "Convert GeoJSON to GeoPackage format",
            DataFormatType.GEOJSON, ".geojson", "GeoJSON|*.geojson",
            DataFormatType.GEOPACKAGE, ".gpkg", "GeoPackage|*.gpkg"));

        tools.Add(CreateConversionTool("filegdb-to-shp", "FileGDB → SHP", "Convert FileGDB to Shapefile format",
            DataFormatType.FILEGDB, ".gdb", "FileGDB|*.gdb",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp"));

        tools.Add(CreateConversionTool("shp-to-filegdb", "SHP → FileGDB", "Convert Shapefile to FileGDB format",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.FILEGDB, ".gdb", "FileGDB|*.gdb"));

        tools.Add(CreateCsvToVectorTool());
        tools.Add(CreatePostgisImportTool());
        tools.Add(CreatePostgisExportTool());

        // Geometry tools
        tools.Add(CreateBufferTool());
        tools.Add(CreateUnionTool());
        tools.Add(CreateIntersectionTool());
        tools.Add(CreateDifferenceTool());
        tools.Add(CreateConvexHullTool());
        tools.Add(CreateCentroidTool());
        tools.Add(CreateSimplifyTool());
        tools.Add(CreateFixGeometriesTool());
        tools.Add(CreateMergeLayersTool());
        tools.Add(CreateSplitLayerTool());
        tools.Add(CreateClipTool());
        tools.Add(CreateSpatialJoinTool());

        // Validation tools
        tools.Add(CreateCheckGeometryTool());

        // Coordinate tools
        tools.Add(CreateReprojectTool());
        tools.Add(CreateBatchReprojectTool());

        // Analysis tools
        tools.Add(CreateCalculateAreaTool());
        tools.Add(CreateCalculateLengthTool());
        tools.Add(CreateSpatialFilterTool());
        tools.Add(CreateAttributeQueryTool());

        // Utility tools
        tools.Add(CreateZipCompressTool());
        tools.Add(CreateZipExtractTool());

        return tools;
    }

    private static ToolInfo CreateConversionTool(
        string id, string name, string description,
        DataFormatType sourceFormat, string sourceExt, string sourceFilter,
        DataFormatType targetFormat, string targetExt, string targetFilter)
    {
        return new ToolInfo
        {
            Id = id,
            Name = name,
            Description = description,
            Category = ToolCategory.Conversion,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    Description = $"Input {sourceExt} file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = sourceFilter
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    Description = $"Output {targetExt} file",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = targetFilter
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var outputPath = parameters["output"];

                    progress?.Report("Reading input file...");
                    var layer = OguLayerUtil.ReadLayer(sourceFormat, inputPath);

                    progress?.Report($"Read {layer.GetFeatureCount()} features. Writing output...");
                    OguLayerUtil.WriteLayer(targetFormat, layer, outputPath);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Conversion completed. {layer.GetFeatureCount()} features converted.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateBufferTool()
    {
        return new ToolInfo
        {
            Id = "buffer",
            Name = "Buffer",
            Description = "Create a buffer zone around a geometry",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input WKT File",
                    Description = "Text file containing WKT geometry",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                },
                new()
                {
                    Name = "output",
                    Label = "Output WKT File",
                    Description = "Output text file for the result WKT",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                },
                new()
                {
                    Name = "distance",
                    Label = "Buffer Distance",
                    Description = "Buffer distance (in coordinate system units)",
                    Type = ParameterType.Number,
                    Required = true,
                    DefaultValue = "1.0"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var outputPath = parameters["output"];
                    var distance = double.Parse(parameters["distance"]);

                    progress?.Report("Reading WKT geometry...");
                    var wkt = await File.ReadAllTextAsync(inputPath, ct);

                    progress?.Report("Applying buffer operation...");
                    var result = GeometryUtil.BufferWkt(wkt.Trim(), distance);

                    await File.WriteAllTextAsync(outputPath, result, ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Buffer operation completed with distance {distance}.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateUnionTool()
    {
        return new ToolInfo
        {
            Id = "union",
            Name = "Union",
            Description = "Compute the union of two geometries",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input1",
                    Label = "First WKT File",
                    Description = "Text file containing the first WKT geometry",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                },
                new()
                {
                    Name = "input2",
                    Label = "Second WKT File",
                    Description = "Text file containing the second WKT geometry",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                },
                new()
                {
                    Name = "output",
                    Label = "Output WKT File",
                    Description = "Output text file for the result WKT",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath1 = parameters["input1"];
                    var inputPath2 = parameters["input2"];
                    var outputPath = parameters["output"];

                    progress?.Report("Reading WKT geometries...");
                    var wkt1 = (await File.ReadAllTextAsync(inputPath1, ct)).Trim();
                    var wkt2 = (await File.ReadAllTextAsync(inputPath2, ct)).Trim();

                    progress?.Report("Computing union...");
                    var result = GeometryUtil.UnionWkt(new[] { wkt1, wkt2 });

                    await File.WriteAllTextAsync(outputPath, result, ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = "Union operation completed.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateIntersectionTool()
    {
        return new ToolInfo
        {
            Id = "intersection",
            Name = "Intersection",
            Description = "Compute the intersection of two geometries",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input1",
                    Label = "First WKT File",
                    Description = "Text file containing the first WKT geometry",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                },
                new()
                {
                    Name = "input2",
                    Label = "Second WKT File",
                    Description = "Text file containing the second WKT geometry",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                },
                new()
                {
                    Name = "output",
                    Label = "Output WKT File",
                    Description = "Output text file for the result WKT",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath1 = parameters["input1"];
                    var inputPath2 = parameters["input2"];
                    var outputPath = parameters["output"];

                    progress?.Report("Reading WKT geometries...");
                    var wkt1 = (await File.ReadAllTextAsync(inputPath1, ct)).Trim();
                    var wkt2 = (await File.ReadAllTextAsync(inputPath2, ct)).Trim();

                    progress?.Report("Computing intersection...");
                    var result = GeometryUtil.IntersectionWkt(wkt1, wkt2);

                    await File.WriteAllTextAsync(outputPath, result, ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = "Intersection operation completed.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateDifferenceTool()
    {
        return new ToolInfo
        {
            Id = "difference",
            Name = "Difference",
            Description = "Compute the difference of two geometries (A minus B)",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input1",
                    Label = "First WKT File (A)",
                    Description = "Text file containing the first WKT geometry",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                },
                new()
                {
                    Name = "input2",
                    Label = "Second WKT File (B)",
                    Description = "Text file containing the second WKT geometry",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                },
                new()
                {
                    Name = "output",
                    Label = "Output WKT File",
                    Description = "Output text file for the result WKT",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath1 = parameters["input1"];
                    var inputPath2 = parameters["input2"];
                    var outputPath = parameters["output"];

                    progress?.Report("Reading WKT geometries...");
                    var wkt1 = (await File.ReadAllTextAsync(inputPath1, ct)).Trim();
                    var wkt2 = (await File.ReadAllTextAsync(inputPath2, ct)).Trim();

                    progress?.Report("Computing difference...");
                    var geomA = GeometryUtil.Wkt2Geometry(wkt1);
                    var geomB = GeometryUtil.Wkt2Geometry(wkt2);
                    var diff = GeometryUtil.Difference(geomA, geomB);
                    var result = GeometryUtil.Geometry2Wkt(diff);

                    await File.WriteAllTextAsync(outputPath, result, ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = "Difference operation completed.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateConvexHullTool()
    {
        return new ToolInfo
        {
            Id = "convex-hull",
            Name = "Convex Hull",
            Description = "Compute the convex hull of a geometry",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input WKT File",
                    Description = "Text file containing WKT geometry",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                },
                new()
                {
                    Name = "output",
                    Label = "Output WKT File",
                    Description = "Output text file for the result WKT",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var outputPath = parameters["output"];

                    progress?.Report("Reading WKT geometry...");
                    var wkt = (await File.ReadAllTextAsync(inputPath, ct)).Trim();

                    progress?.Report("Computing convex hull...");
                    var geom = GeometryUtil.Wkt2Geometry(wkt);
                    var hull = GeometryUtil.ConvexHull(geom);
                    var result = GeometryUtil.Geometry2Wkt(hull);

                    await File.WriteAllTextAsync(outputPath, result, ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = "Convex hull operation completed.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateCentroidTool()
    {
        return new ToolInfo
        {
            Id = "centroid",
            Name = "Centroid",
            Description = "Compute the centroid of a geometry",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input WKT File",
                    Description = "Text file containing WKT geometry",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                },
                new()
                {
                    Name = "output",
                    Label = "Output WKT File",
                    Description = "Output text file for the result WKT",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var outputPath = parameters["output"];

                    progress?.Report("Reading WKT geometry...");
                    var wkt = (await File.ReadAllTextAsync(inputPath, ct)).Trim();

                    progress?.Report("Computing centroid...");
                    var result = GeometryUtil.CentroidWkt(wkt);

                    await File.WriteAllTextAsync(outputPath, result, ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = "Centroid operation completed.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateSimplifyTool()
    {
        return new ToolInfo
        {
            Id = "simplify",
            Name = "Simplify",
            Description = "Simplify a geometry using the Douglas-Peucker algorithm",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input WKT File",
                    Description = "Text file containing WKT geometry",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                },
                new()
                {
                    Name = "output",
                    Label = "Output WKT File",
                    Description = "Output text file for the result WKT",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                },
                new()
                {
                    Name = "tolerance",
                    Label = "Tolerance",
                    Description = "Simplification tolerance",
                    Type = ParameterType.Number,
                    Required = true,
                    DefaultValue = "0.001"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var outputPath = parameters["output"];
                    var tolerance = double.Parse(parameters["tolerance"]);

                    progress?.Report("Reading WKT geometry...");
                    var wkt = (await File.ReadAllTextAsync(inputPath, ct)).Trim();

                    progress?.Report("Simplifying geometry...");
                    var result = GeometryUtil.SimplifyWkt(wkt, tolerance);

                    await File.WriteAllTextAsync(outputPath, result, ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Simplify operation completed with tolerance {tolerance}.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateCheckGeometryTool()
    {
        return new ToolInfo
        {
            Id = "check-geometry",
            Name = "Check Geometry",
            Description = "Validate the geometry and report whether it is valid",
            Category = ToolCategory.Validation,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input WKT File",
                    Description = "Text file containing WKT geometry",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];

                    progress?.Report("Reading WKT geometry...");
                    var wkt = (await File.ReadAllTextAsync(inputPath, ct)).Trim();

                    progress?.Report("Validating geometry...");
                    var geom = GeometryUtil.Wkt2Geometry(wkt);
                    var isValid = GeometryUtil.IsValid(geom);
                    var isSimple = GeometryUtil.IsSimple(geom);
                    var geomType = GeometryUtil.GetGeometryType(geom);

                    var message = $"Geometry Type: {geomType}\nIs Valid: {isValid}\nIs Simple: {isSimple}";

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = message,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateReprojectTool()
    {
        return new ToolInfo
        {
            Id = "reproject",
            Name = "Reproject",
            Description = "Transform coordinates from one coordinate system to another",
            Category = ToolCategory.Coordinate,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input WKT File",
                    Description = "Text file containing WKT geometry",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                },
                new()
                {
                    Name = "output",
                    Label = "Output WKT File",
                    Description = "Output text file for the transformed WKT",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                },
                new()
                {
                    Name = "sourceWkid",
                    Label = "Source WKID",
                    Description = "Source coordinate system WKID (e.g. 4326 for WGS84)",
                    Type = ParameterType.Integer,
                    Required = true,
                    DefaultValue = "4326"
                },
                new()
                {
                    Name = "targetWkid",
                    Label = "Target WKID",
                    Description = "Target coordinate system WKID (e.g. 4490 for CGCS2000)",
                    Type = ParameterType.Integer,
                    Required = true,
                    DefaultValue = "4490"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var outputPath = parameters["output"];
                    var sourceWkid = int.Parse(parameters["sourceWkid"]);
                    var targetWkid = int.Parse(parameters["targetWkid"]);

                    progress?.Report("Reading WKT geometry...");
                    var wkt = (await File.ReadAllTextAsync(inputPath, ct)).Trim();

                    progress?.Report($"Transforming from WKID {sourceWkid} to {targetWkid}...");
                    var result = CrsUtil.Transform(wkt, sourceWkid, targetWkid);

                    await File.WriteAllTextAsync(outputPath, result, ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Reprojection completed from WKID {sourceWkid} to {targetWkid}.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateCalculateAreaTool()
    {
        return new ToolInfo
        {
            Id = "calculate-area",
            Name = "Calculate Area",
            Description = "Calculate the area of a geometry",
            Category = ToolCategory.Analysis,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input WKT File",
                    Description = "Text file containing WKT geometry",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];

                    progress?.Report("Reading WKT geometry...");
                    var wkt = (await File.ReadAllTextAsync(inputPath, ct)).Trim();

                    progress?.Report("Calculating area...");
                    var area = GeometryUtil.AreaWkt(wkt);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Area: {area} (in coordinate system units²)",
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateCalculateLengthTool()
    {
        return new ToolInfo
        {
            Id = "calculate-length",
            Name = "Calculate Length",
            Description = "Calculate the length of a geometry",
            Category = ToolCategory.Analysis,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input WKT File",
                    Description = "Text file containing WKT geometry",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];

                    progress?.Report("Reading WKT geometry...");
                    var wkt = (await File.ReadAllTextAsync(inputPath, ct)).Trim();

                    progress?.Report("Calculating length...");
                    var length = GeometryUtil.LengthWkt(wkt);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Length: {length} (in coordinate system units)",
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateZipCompressTool()
    {
        return new ToolInfo
        {
            Id = "zip-compress",
            Name = "ZIP Compress",
            Description = "Compress a folder into a ZIP archive",
            Category = ToolCategory.Utility,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input Folder",
                    Description = "Folder to compress",
                    Type = ParameterType.Text,
                    Required = true
                },
                new()
                {
                    Name = "output",
                    Label = "Output ZIP File",
                    Description = "Output ZIP file path",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "ZIP Archive|*.zip"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputFolder = parameters["input"];
                    var outputPath = parameters["output"];

                    progress?.Report("Compressing folder...");
                    await Task.Run(() => ZipUtil.Zip(inputFolder, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = "Compression completed.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateZipExtractTool()
    {
        return new ToolInfo
        {
            Id = "zip-extract",
            Name = "ZIP Extract",
            Description = "Extract a ZIP archive to a folder",
            Category = ToolCategory.Utility,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input ZIP File",
                    Description = "ZIP file to extract",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "ZIP Archive|*.zip"
                },
                new()
                {
                    Name = "output",
                    Label = "Output Folder",
                    Description = "Destination folder for extraction",
                    Type = ParameterType.Text,
                    Required = true
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var outputFolder = parameters["output"];

                    progress?.Report("Extracting ZIP archive...");
                    await Task.Run(() => ZipUtil.Unzip(inputPath, outputFolder), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = "Extraction completed.",
                        OutputPath = outputFolder,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static DataFormatType DetectFormat(string filePath)
    {
        var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
        return ext switch
        {
            ".shp" => DataFormatType.SHP,
            ".geojson" or ".json" => DataFormatType.GEOJSON,
            ".gpkg" => DataFormatType.GEOPACKAGE,
            ".kml" => DataFormatType.KML,
            ".dxf" => DataFormatType.DXF,
            ".gdb" => DataFormatType.FILEGDB,
            _ => throw new ArgumentException($"Unsupported file format: {ext}")
        };
    }

    private static ToolInfo CreateCsvToVectorTool()
    {
        return new ToolInfo
        {
            Id = "csv-to-vector",
            Name = "CSV → Vector",
            Description = "Convert CSV file with coordinates to Shapefile",
            Category = ToolCategory.Conversion,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    Description = "Input CSV file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "CSV files|*.csv"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    Description = "Output Shapefile",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp"
                },
                new()
                {
                    Name = "xField",
                    Label = "X (Longitude) Column Name",
                    Description = "Name of the column containing X/Longitude values",
                    Type = ParameterType.Text,
                    Required = true,
                    DefaultValue = "x"
                },
                new()
                {
                    Name = "yField",
                    Label = "Y (Latitude) Column Name",
                    Description = "Name of the column containing Y/Latitude values",
                    Type = ParameterType.Text,
                    Required = true,
                    DefaultValue = "y"
                },
                new()
                {
                    Name = "delimiter",
                    Label = "Delimiter",
                    Description = "Column delimiter character",
                    Type = ParameterType.Dropdown,
                    Required = true,
                    DefaultValue = ",",
                    Options = new[] { ",", "\t", ";", "|" }
                },
                new()
                {
                    Name = "wkid",
                    Label = "Coordinate System WKID",
                    Description = "WKID of the coordinate system (e.g. 4326 for WGS84)",
                    Type = ParameterType.Integer,
                    Required = true,
                    DefaultValue = "4326"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var outputPath = parameters["output"];
                    var xField = parameters["xField"];
                    var yField = parameters["yField"];
                    var delimiter = parameters["delimiter"];
                    var wkid = int.Parse(parameters["wkid"]);

                    progress?.Report("Reading CSV file...");
                    var lines = await File.ReadAllLinesAsync(inputPath, ct);
                    if (lines.Length < 2)
                        throw new ArgumentException("CSV file must have a header row and at least one data row.");

                    var headers = lines[0].Split(delimiter);
                    var xIndex = Array.IndexOf(headers, xField);
                    var yIndex = Array.IndexOf(headers, yField);

                    if (xIndex < 0)
                        throw new ArgumentException($"Column '{xField}' not found in CSV header.");
                    if (yIndex < 0)
                        throw new ArgumentException($"Column '{yField}' not found in CSV header.");

                    var layer = new OguLayer
                    {
                        Name = Path.GetFileNameWithoutExtension(inputPath),
                        Wkid = wkid,
                        GeometryType = OpenGIS.Utils.Engine.Enums.GeometryType.POINT
                    };

                    for (var i = 0; i < headers.Length; i++)
                    {
                        if (i == xIndex || i == yIndex) continue;
                        layer.AddField(new OguField { Name = headers[i], DataType = FieldDataType.STRING });
                    }

                    progress?.Report("Creating features...");
                    var fid = 0;
                    var skippedCount = 0;
                    for (var i = 1; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var values = line.Split(delimiter);
                        if (values.Length != headers.Length)
                        {
                            skippedCount++;
                            continue;
                        }

                        if (!double.TryParse(values[xIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                            throw new FormatException($"Row {i + 1}: cannot parse X value '{values[xIndex]}' in column '{xField}'.");
                        if (!double.TryParse(values[yIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                            throw new FormatException($"Row {i + 1}: cannot parse Y value '{values[yIndex]}' in column '{yField}'.");

                        var feature = new OguFeature
                        {
                            Fid = fid++,
                            Wkt = $"POINT ({x.ToString(CultureInfo.InvariantCulture)} {y.ToString(CultureInfo.InvariantCulture)})"
                        };

                        for (var j = 0; j < headers.Length; j++)
                        {
                            if (j == xIndex || j == yIndex) continue;
                            feature.SetValue(headers[j], values[j]);
                        }

                        layer.AddFeature(feature);
                    }

                    progress?.Report($"Writing {layer.GetFeatureCount()} features to Shapefile...");
                    await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, layer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Conversion completed. {layer.GetFeatureCount()} features created from CSV." +
                            (skippedCount > 0 ? $" {skippedCount} rows skipped due to column count mismatch." : ""),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateFixGeometriesTool()
    {
        return new ToolInfo
        {
            Id = "fix-geometries",
            Name = "Fix Geometries",
            Description = "Fix invalid geometries using the buffer(0) technique",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    Description = "Output vector file",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var outputPath = parameters["output"];
                    var inputFormat = DetectFormat(inputPath);
                    var outputFormat = DetectFormat(outputPath);

                    progress?.Report("Reading input layer...");
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report("Fixing geometries...");
                    var fixedCount = 0;
                    foreach (var feature in layer.Features)
                    {
                        if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;
                        var geom = GeometryUtil.Wkt2Geometry(feature.Wkt);
                        if (!GeometryUtil.IsValid(geom).IsValid)
                        {
                            var repairedGeom = GeometryUtil.Buffer(geom, 0);
                            feature.Wkt = GeometryUtil.Geometry2Wkt(repairedGeom);
                            fixedCount++;
                        }
                    }

                    progress?.Report("Writing output layer...");
                    await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, layer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Fix geometries completed. {fixedCount} of {layer.GetFeatureCount()} geometries were fixed.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateMergeLayersTool()
    {
        return new ToolInfo
        {
            Id = "merge-layers",
            Name = "Merge Layers",
            Description = "Merge two vector layers into one",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input1",
                    Label = "First Input File",
                    Description = "First input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg|KML|*.kml"
                },
                new()
                {
                    Name = "input2",
                    Label = "Second Input File",
                    Description = "Second input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg|KML|*.kml"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    Description = "Output Shapefile",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath1 = parameters["input1"];
                    var inputPath2 = parameters["input2"];
                    var outputPath = parameters["output"];
                    var format1 = DetectFormat(inputPath1);
                    var format2 = DetectFormat(inputPath2);

                    progress?.Report("Reading first layer...");
                    var layer1 = await Task.Run(() => OguLayerUtil.ReadLayer(format1, inputPath1), ct);

                    progress?.Report("Reading second layer...");
                    var layer2 = await Task.Run(() => OguLayerUtil.ReadLayer(format2, inputPath2), ct);

                    progress?.Report("Merging layers...");
                    var merged = new OguLayer
                    {
                        Name = Path.GetFileNameWithoutExtension(outputPath),
                        Wkid = layer1.Wkid,
                        GeometryType = layer1.GeometryType
                    };

                    foreach (var field in layer1.Fields)
                        merged.AddField(field.Clone());

                    var existingFieldNames = new HashSet<string>(layer1.Fields.Select(f => f.Name));
                    foreach (var field in layer2.Fields)
                    {
                        if (!existingFieldNames.Contains(field.Name))
                            merged.AddField(field.Clone());
                    }

                    foreach (var feature in layer1.Features)
                        merged.AddFeature(feature.Clone());
                    foreach (var feature in layer2.Features)
                        merged.AddFeature(feature.Clone());

                    progress?.Report("Writing merged layer...");
                    await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, merged, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Merge completed. {merged.GetFeatureCount()} total features ({layer1.GetFeatureCount()} + {layer2.GetFeatureCount()}).",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateSplitLayerTool()
    {
        return new ToolInfo
        {
            Id = "split-layer",
            Name = "Split Layer",
            Description = "Split a vector layer into multiple layers by field value",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "fieldName",
                    Label = "Split Field Name",
                    Description = "Name of the field to split by",
                    Type = ParameterType.Text,
                    Required = true
                },
                new()
                {
                    Name = "outputFolder",
                    Label = "Output Folder",
                    Description = "Folder to write split layers to",
                    Type = ParameterType.Text,
                    Required = true
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var fieldName = parameters["fieldName"];
                    var outputFolder = parameters["outputFolder"];
                    var inputFormat = DetectFormat(inputPath);

                    progress?.Report("Reading input layer...");
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    if (!Directory.Exists(outputFolder))
                        Directory.CreateDirectory(outputFolder);

                    progress?.Report("Grouping features by field value...");
                    var groups = layer.Features
                        .GroupBy(f => f.GetValue(fieldName)?.ToString() ?? "NULL")
                        .ToList();

                    var count = 0;
                    foreach (var group in groups)
                    {
                        var groupLayer = new OguLayer
                        {
                            Name = group.Key,
                            Wkid = layer.Wkid,
                            GeometryType = layer.GeometryType
                        };

                        foreach (var field in layer.Fields)
                            groupLayer.AddField(field.Clone());

                        foreach (var feature in group)
                            groupLayer.AddFeature(feature.Clone());

                        var safeName = string.Join("_", group.Key.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                        if (string.IsNullOrWhiteSpace(safeName))
                            safeName = $"group_{count}";
                        var outputPath = Path.Combine(outputFolder, $"{safeName}.shp");

                        progress?.Report($"Writing group '{group.Key}' ({groupLayer.GetFeatureCount()} features)...");
                        await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, groupLayer, outputPath), ct);
                        count++;
                    }

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Split completed. {count} layers created from {layer.GetFeatureCount()} features.",
                        OutputPath = outputFolder,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateClipTool()
    {
        return new ToolInfo
        {
            Id = "clip",
            Name = "Clip",
            Description = "Clip a vector layer by a polygon layer",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    Description = "Input vector file to clip",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "clipLayer",
                    Label = "Clip Polygon File",
                    Description = "Polygon layer to clip by",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    Description = "Output Shapefile",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var clipPath = parameters["clipLayer"];
                    var outputPath = parameters["output"];
                    var inputFormat = DetectFormat(inputPath);
                    var clipFormat = DetectFormat(clipPath);

                    progress?.Report("Reading input layer...");
                    var inputLayer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report("Reading clip layer...");
                    var clipLayer = await Task.Run(() => OguLayerUtil.ReadLayer(clipFormat, clipPath), ct);

                    progress?.Report("Computing clip geometry union...");
                    var clipWkts = clipLayer.Features
                        .Where(f => !string.IsNullOrWhiteSpace(f.Wkt))
                        .Select(f => f.Wkt!)
                        .ToList();

                    var clipGeom = GeometryUtil.Wkt2Geometry(GeometryUtil.UnionWkt(clipWkts));

                    progress?.Report("Clipping features...");
                    var outputLayer = new OguLayer
                    {
                        Name = Path.GetFileNameWithoutExtension(outputPath),
                        Wkid = inputLayer.Wkid,
                        GeometryType = inputLayer.GeometryType
                    };

                    foreach (var field in inputLayer.Fields)
                        outputLayer.AddField(field.Clone());

                    foreach (var feature in inputLayer.Features)
                    {
                        if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;
                        var geom = GeometryUtil.Wkt2Geometry(feature.Wkt);
                        var clipped = GeometryUtil.Intersection(geom, clipGeom);
                        if (!GeometryUtil.IsEmpty(clipped))
                        {
                            var newFeature = feature.Clone();
                            newFeature.Wkt = GeometryUtil.Geometry2Wkt(clipped);
                            outputLayer.AddFeature(newFeature);
                        }
                    }

                    progress?.Report("Writing output layer...");
                    await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, outputLayer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Clip completed. {outputLayer.GetFeatureCount()} of {inputLayer.GetFeatureCount()} features retained.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateSpatialJoinTool()
    {
        return new ToolInfo
        {
            Id = "spatial-join",
            Name = "Spatial Join",
            Description = "Join attributes from one layer to another based on spatial relationship",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Target Layer",
                    Description = "Target vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "joinLayer",
                    Label = "Join Layer",
                    Description = "Layer to join attributes from",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    Description = "Output Shapefile",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp"
                },
                new()
                {
                    Name = "joinType",
                    Label = "Spatial Relationship",
                    Description = "Type of spatial relationship for the join",
                    Type = ParameterType.Dropdown,
                    Required = true,
                    DefaultValue = "Intersects",
                    Options = new[] { "Intersects", "Contains", "Within" }
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var joinPath = parameters["joinLayer"];
                    var outputPath = parameters["output"];
                    var joinType = parameters["joinType"];
                    var inputFormat = DetectFormat(inputPath);
                    var joinFormat = DetectFormat(joinPath);

                    progress?.Report("Reading target layer...");
                    var targetLayer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report("Reading join layer...");
                    var joinLayer = await Task.Run(() => OguLayerUtil.ReadLayer(joinFormat, joinPath), ct);

                    progress?.Report("Performing spatial join...");
                    var outputLayer = new OguLayer
                    {
                        Name = Path.GetFileNameWithoutExtension(outputPath),
                        Wkid = targetLayer.Wkid,
                        GeometryType = targetLayer.GeometryType
                    };

                    foreach (var field in targetLayer.Fields)
                        outputLayer.AddField(field.Clone());

                    var targetFieldNames = new HashSet<string>(targetLayer.Fields.Select(f => f.Name));
                    foreach (var field in joinLayer.Fields)
                    {
                        if (!targetFieldNames.Contains(field.Name))
                            outputLayer.AddField(field.Clone());
                    }

                    var joinedCount = 0;
                    foreach (var targetFeature in targetLayer.Features)
                    {
                        var newFeature = targetFeature.Clone();
                        if (!string.IsNullOrWhiteSpace(targetFeature.Wkt))
                        {
                            foreach (var joinFeature in joinLayer.Features)
                            {
                                if (string.IsNullOrWhiteSpace(joinFeature.Wkt)) continue;

                                bool matches = joinType switch
                                {
                                    "Contains" => GeometryUtil.ContainsWkt(targetFeature.Wkt, joinFeature.Wkt),
                                    "Within" => GeometryUtil.ContainsWkt(joinFeature.Wkt, targetFeature.Wkt),
                                    _ => GeometryUtil.IntersectsWkt(targetFeature.Wkt, joinFeature.Wkt)
                                };

                                if (matches)
                                {
                                    foreach (var field in joinLayer.Fields)
                                    {
                                        if (!targetFieldNames.Contains(field.Name) && joinFeature.HasAttribute(field.Name))
                                            newFeature.SetValue(field.Name, joinFeature.GetValue(field.Name));
                                    }
                                    joinedCount++;
                                    break;
                                }
                            }
                        }
                        outputLayer.AddFeature(newFeature);
                    }

                    progress?.Report("Writing output layer...");
                    await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, outputLayer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Spatial join completed. {joinedCount} of {targetLayer.GetFeatureCount()} features joined.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateBatchReprojectTool()
    {
        return new ToolInfo
        {
            Id = "batch-reproject",
            Name = "Batch Reproject",
            Description = "Reproject all vector files in a folder to a different coordinate system",
            Category = ToolCategory.Coordinate,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "inputFolder",
                    Label = "Input Folder",
                    Description = "Folder containing vector files",
                    Type = ParameterType.Text,
                    Required = true
                },
                new()
                {
                    Name = "outputFolder",
                    Label = "Output Folder",
                    Description = "Folder to write reprojected files to",
                    Type = ParameterType.Text,
                    Required = true
                },
                new()
                {
                    Name = "sourceWkid",
                    Label = "Source WKID",
                    Description = "Source coordinate system WKID (e.g. 4326 for WGS84)",
                    Type = ParameterType.Integer,
                    Required = true,
                    DefaultValue = "4326"
                },
                new()
                {
                    Name = "targetWkid",
                    Label = "Target WKID",
                    Description = "Target coordinate system WKID (e.g. 4490 for CGCS2000)",
                    Type = ParameterType.Integer,
                    Required = true,
                    DefaultValue = "4490"
                },
                new()
                {
                    Name = "format",
                    Label = "File Format",
                    Description = "Vector file format to process",
                    Type = ParameterType.Dropdown,
                    Required = true,
                    DefaultValue = "SHP",
                    Options = new[] { "SHP", "GeoJSON", "GeoPackage" }
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputFolder = parameters["inputFolder"];
                    var outputFolder = parameters["outputFolder"];
                    var sourceWkid = int.Parse(parameters["sourceWkid"]);
                    var targetWkid = int.Parse(parameters["targetWkid"]);
                    var format = parameters["format"];

                    var (extension, dataFormat) = format switch
                    {
                        "GeoJSON" => ("*.geojson", DataFormatType.GEOJSON),
                        "GeoPackage" => ("*.gpkg", DataFormatType.GEOPACKAGE),
                        _ => ("*.shp", DataFormatType.SHP)
                    };

                    if (!Directory.Exists(outputFolder))
                        Directory.CreateDirectory(outputFolder);

                    var files = Directory.GetFiles(inputFolder, extension);
                    if (files.Length == 0)
                        throw new ArgumentException($"No {format} files found in the input folder.");

                    var processedCount = 0;
                    foreach (var file in files)
                    {
                        progress?.Report($"Processing {Path.GetFileName(file)} ({processedCount + 1}/{files.Length})...");

                        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(dataFormat, file), ct);

                        foreach (var feature in layer.Features)
                        {
                            if (!string.IsNullOrWhiteSpace(feature.Wkt))
                                feature.Wkt = CrsUtil.Transform(feature.Wkt, sourceWkid, targetWkid);
                        }

                        layer.Wkid = targetWkid;

                        var outputPath = Path.Combine(outputFolder, Path.GetFileName(file));
                        await Task.Run(() => OguLayerUtil.WriteLayer(dataFormat, layer, outputPath), ct);
                        processedCount++;
                    }

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Batch reproject completed. {processedCount} files reprojected from WKID {sourceWkid} to {targetWkid}.",
                        OutputPath = outputFolder,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateSpatialFilterTool()
    {
        return new ToolInfo
        {
            Id = "spatial-filter",
            Name = "Spatial Filter",
            Description = "Filter features by spatial extent (WKT polygon)",
            Category = ToolCategory.Analysis,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    Description = "Output vector file",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "extentWkt",
                    Label = "Filter Extent (WKT)",
                    Description = "WKT polygon to filter features by (e.g. POLYGON((...)))",
                    Type = ParameterType.Text,
                    Required = true
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var outputPath = parameters["output"];
                    var extentWkt = parameters["extentWkt"];
                    var inputFormat = DetectFormat(inputPath);
                    var outputFormat = DetectFormat(outputPath);

                    progress?.Report("Reading input layer...");
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report("Filtering features by spatial extent...");
                    var outputLayer = new OguLayer
                    {
                        Name = Path.GetFileNameWithoutExtension(outputPath),
                        Wkid = layer.Wkid,
                        GeometryType = layer.GeometryType
                    };

                    foreach (var field in layer.Fields)
                        outputLayer.AddField(field.Clone());

                    foreach (var feature in layer.Features)
                    {
                        if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;
                        if (GeometryUtil.IntersectsWkt(feature.Wkt, extentWkt))
                            outputLayer.AddFeature(feature.Clone());
                    }

                    progress?.Report("Writing output layer...");
                    await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, outputLayer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Spatial filter completed. {outputLayer.GetFeatureCount()} of {layer.GetFeatureCount()} features matched.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateAttributeQueryTool()
    {
        return new ToolInfo
        {
            Id = "attribute-query",
            Name = "Attribute Query",
            Description = "Filter features by attribute expression (SQL WHERE clause)",
            Category = ToolCategory.Analysis,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    Description = "Output Shapefile",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp"
                },
                new()
                {
                    Name = "whereClause",
                    Label = "WHERE Clause",
                    Description = "SQL WHERE expression (e.g. population > 1000)",
                    Type = ParameterType.Text,
                    Required = true
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var outputPath = parameters["output"];
                    var whereClause = parameters["whereClause"];
                    var inputFormat = DetectFormat(inputPath);

                    progress?.Report("Reading and filtering layer...");
                    var layer = await Task.Run(() =>
                        OguLayerUtil.ReadLayer(inputFormat, inputPath, null, whereClause, null, null, null), ct);

                    progress?.Report($"Writing {layer.GetFeatureCount()} filtered features...");
                    await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, layer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"Attribute query completed. {layer.GetFeatureCount()} features matched.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreatePostgisImportTool()
    {
        return new ToolInfo
        {
            Id = "postgis-import",
            Name = "PostGIS Import",
            Description = "Import a PostGIS table to a Shapefile",
            Category = ToolCategory.Conversion,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "connectionString",
                    Label = "Connection String",
                    Description = "PostgreSQL connection string (e.g. Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass)",
                    Type = ParameterType.Text,
                    Required = true
                },
                new()
                {
                    Name = "tableName",
                    Label = "Table Name",
                    Description = "PostGIS table name to import",
                    Type = ParameterType.Text,
                    Required = true
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    Description = "Output Shapefile",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var connectionString = parameters["connectionString"];
                    var tableName = parameters["tableName"];
                    var outputPath = parameters["output"];

                    progress?.Report("Reading from PostGIS...");
                    var layer = await Task.Run(() => PostgisUtil.ReadPostGIS(connectionString, tableName, null), ct);

                    progress?.Report($"Writing {layer.GetFeatureCount()} features to Shapefile...");
                    await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, layer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"PostGIS import completed. {layer.GetFeatureCount()} features imported.",
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreatePostgisExportTool()
    {
        return new ToolInfo
        {
            Id = "postgis-export",
            Name = "PostGIS Export",
            Description = "Export a vector file to a PostGIS table",
            Category = ToolCategory.Conversion,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "connectionString",
                    Label = "Connection String",
                    Description = "PostgreSQL connection string (e.g. Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass)",
                    Type = ParameterType.Text,
                    Required = true
                },
                new()
                {
                    Name = "tableName",
                    Label = "Table Name",
                    Description = "PostGIS table name to export to",
                    Type = ParameterType.Text,
                    Required = true
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var connectionString = parameters["connectionString"];
                    var tableName = parameters["tableName"];
                    var inputFormat = DetectFormat(inputPath);

                    progress?.Report("Reading input layer...");
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report($"Exporting {layer.GetFeatureCount()} features to PostGIS...");
                    await Task.Run(() => PostgisUtil.WritePostGIS(layer, connectionString, tableName), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = $"PostGIS export completed. {layer.GetFeatureCount()} features exported to table '{tableName}'.",
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
                }
            }
        };
    }
}
