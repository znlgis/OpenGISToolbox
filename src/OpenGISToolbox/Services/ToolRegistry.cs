using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
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

        // Geometry tools
        tools.Add(CreateBufferTool());
        tools.Add(CreateUnionTool());
        tools.Add(CreateIntersectionTool());
        tools.Add(CreateDifferenceTool());
        tools.Add(CreateConvexHullTool());
        tools.Add(CreateCentroidTool());
        tools.Add(CreateSimplifyTool());

        // Validation tools
        tools.Add(CreateCheckGeometryTool());

        // Coordinate tools
        tools.Add(CreateReprojectTool());

        // Analysis tools
        tools.Add(CreateCalculateAreaTool());
        tools.Add(CreateCalculateLengthTool());

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
}
