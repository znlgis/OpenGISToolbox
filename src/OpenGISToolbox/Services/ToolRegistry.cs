using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using MaxRev.Gdal.Core;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGIS.Utils.Engine.Util;
using OpenGIS.Utils.Geometry;
using OpenGIS.Utils.Utils;
using OpenGISToolbox.Models;
using OSGeo.GDAL;

namespace OpenGISToolbox.Services;

public static class ToolRegistry
{
    private static readonly Lazy<HttpClient> SharedHttpClient = new(() =>
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenGISToolbox/1.0 (github.com/znlgis/OpenGISToolbox)");
        return client;
    });

    private static readonly Lazy<List<ToolInfo>> CachedTools = new(BuildAllTools);

    private static bool _gdalInitialized;

    private static void EnsureGdalInitialized()
    {
        if (_gdalInitialized) return;
        GdalBase.ConfigureAll();
        _gdalInitialized = true;
    }

    public static List<ToolInfo> GetAllTools() => CachedTools.Value;

    /// <summary>
    /// Returns the localized string based on current language setting.
    /// </summary>
    private static string L(string en, string zh) =>
        LanguageManager.Instance.CurrentLanguage == "zh" ? zh : en;

    private static List<ToolInfo> BuildAllTools()
    {
        var tools = new List<ToolInfo>();

        // Conversion tools
        tools.Add(CreateConversionTool("shp-to-geojson", "SHP → GeoJSON", "SHP → GeoJSON", "Convert Shapefile to GeoJSON format", "将 Shapefile 转换为 GeoJSON 格式",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.GEOJSON, ".geojson", "GeoJSON|*.geojson"));

        tools.Add(CreateConversionTool("geojson-to-shp", "GeoJSON → SHP", "GeoJSON → SHP", "Convert GeoJSON to Shapefile format", "将 GeoJSON 转换为 Shapefile 格式",
            DataFormatType.GEOJSON, ".geojson", "GeoJSON|*.geojson",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp"));

        tools.Add(CreateConversionTool("shp-to-kml", "SHP → KML", "SHP → KML", "Convert Shapefile to KML format", "将 Shapefile 转换为 KML 格式",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.KML, ".kml", "KML|*.kml"));

        tools.Add(CreateConversionTool("kml-to-shp", "KML → SHP", "KML → SHP", "Convert KML to Shapefile format", "将 KML 转换为 Shapefile 格式",
            DataFormatType.KML, ".kml", "KML|*.kml",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp"));

        tools.Add(CreateConversionTool("shp-to-gpkg", "SHP → GeoPackage", "SHP → GeoPackage", "Convert Shapefile to GeoPackage format", "将 Shapefile 转换为 GeoPackage 格式",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.GEOPACKAGE, ".gpkg", "GeoPackage|*.gpkg"));

        tools.Add(CreateConversionTool("gpkg-to-shp", "GeoPackage → SHP", "GeoPackage → SHP", "Convert GeoPackage to Shapefile format", "将 GeoPackage 转换为 Shapefile 格式",
            DataFormatType.GEOPACKAGE, ".gpkg", "GeoPackage|*.gpkg",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp"));

        tools.Add(CreateConversionTool("shp-to-dxf", "SHP → DXF", "SHP → DXF", "Convert Shapefile to DXF format", "将 Shapefile 转换为 DXF 格式",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.DXF, ".dxf", "DXF|*.dxf"));

        tools.Add(CreateConversionTool("dxf-to-shp", "DXF → SHP", "DXF → SHP", "Convert DXF to Shapefile format", "将 DXF 转换为 Shapefile 格式",
            DataFormatType.DXF, ".dxf", "DXF|*.dxf",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp"));

        tools.Add(CreateConversionTool("geojson-to-kml", "GeoJSON → KML", "GeoJSON → KML", "Convert GeoJSON to KML format", "将 GeoJSON 转换为 KML 格式",
            DataFormatType.GEOJSON, ".geojson", "GeoJSON|*.geojson",
            DataFormatType.KML, ".kml", "KML|*.kml"));

        tools.Add(CreateConversionTool("geojson-to-gpkg", "GeoJSON → GeoPackage", "GeoJSON → GeoPackage", "Convert GeoJSON to GeoPackage format", "将 GeoJSON 转换为 GeoPackage 格式",
            DataFormatType.GEOJSON, ".geojson", "GeoJSON|*.geojson",
            DataFormatType.GEOPACKAGE, ".gpkg", "GeoPackage|*.gpkg"));

        tools.Add(CreateConversionTool("filegdb-to-shp", "FileGDB → SHP", "FileGDB → SHP", "Convert FileGDB to Shapefile format", "将 FileGDB 转换为 Shapefile 格式",
            DataFormatType.FILEGDB, ".gdb", "FileGDB|*.gdb",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp"));

        tools.Add(CreateConversionTool("shp-to-filegdb", "SHP → FileGDB", "SHP → FileGDB", "Convert Shapefile to FileGDB format", "将 Shapefile 转换为 FileGDB 格式",
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

        // Geometry tools (additional)
        tools.Add(CreateCentralLinesTool());

        // Raster tools
        tools.Add(CreateRasterFormatConvertTool());
        tools.Add(CreateRasterCalculatorTool());

        // Remote Sensing tools
        tools.Add(CreateSatelliteDownloadTool());

        // GPS tools
        tools.Add(CreateGpxProcessingTool());

        // Geocoding tools
        tools.Add(CreateGeocodeAddressesTool());

        return tools;
    }

    private static ToolInfo CreateConversionTool(
        string id, string name, string nameZh, string description, string descriptionZh,
        DataFormatType sourceFormat, string sourceExt, string sourceFilter,
        DataFormatType targetFormat, string targetExt, string targetFilter)
    {
        return new ToolInfo
        {
            Id = id,
            Name = name,
            NameZh = nameZh,
            Description = description,
            DescriptionZh = descriptionZh,
            Category = ToolCategory.Conversion,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = $"Input {sourceExt} file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = sourceFilter
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
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

                    progress?.Report(L("Reading input file...", "读取输入文件..."));
                    var layer = OguLayerUtil.ReadLayer(sourceFormat, inputPath);

                    progress?.Report(L($"Read {layer.GetFeatureCount()} features. Writing output...", $"已读取 {layer.GetFeatureCount()} 个要素，正在写入输出..."));
                    OguLayerUtil.WriteLayer(targetFormat, layer, outputPath);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Conversion completed. {layer.GetFeatureCount()} features converted.", $"转换完成，共转换 {layer.GetFeatureCount()} 个要素。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "缓冲区",
            Description = "Create buffer zones around all features in a vector layer",
            DescriptionZh = "为矢量图层中的所有要素创建缓冲区",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output vector file",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "distance",
                    Label = "Buffer Distance",
                    LabelZh = "缓冲距离",
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
                    var distance = double.Parse(parameters["distance"], CultureInfo.InvariantCulture);
                    var inputFormat = DetectFormat(inputPath);
                    var outputFormat = DetectFormat(outputPath);

                    progress?.Report(L("Reading input layer...", "读取输入图层..."));
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report(L($"Applying buffer (distance={distance}) to {layer.GetFeatureCount()} features...", $"正在对 {layer.GetFeatureCount()} 个要素应用缓冲区（距离={distance}）..."));
                    var outputLayer = new OguLayer
                    {
                        Name = Path.GetFileNameWithoutExtension(outputPath),
                        Wkid = layer.Wkid,
                        GeometryType = GeometryType.POLYGON
                    };

                    foreach (var field in layer.Fields)
                        outputLayer.AddField(field.Clone());

                    var processedCount = 0;
                    foreach (var feature in layer.Features)
                    {
                        if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;
                        var newFeature = feature.Clone();
                        newFeature.Wkt = GeometryUtil.BufferWkt(feature.Wkt, distance);
                        outputLayer.AddFeature(newFeature);
                        processedCount++;
                    }

                    progress?.Report(L("Writing output layer...", "写入输出图层..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, outputLayer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Buffer completed. {processedCount} features buffered with distance {distance}.", $"缓冲区完成。{processedCount} 个要素已缓冲，距离 {distance}。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "合并",
            Description = "Compute the geometric union of two vector layers",
            DescriptionZh = "计算两个矢量图层的几何并集",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input1",
                    Label = "First Input File",
                    LabelZh = "第一个输入文件",
                    Description = "First input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "input2",
                    Label = "Second Input File",
                    LabelZh = "第二个输入文件",
                    Description = "Second input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output vector file with unioned geometries",
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
                    var inputPath1 = parameters["input1"];
                    var inputPath2 = parameters["input2"];
                    var outputPath = parameters["output"];
                    var format1 = DetectFormat(inputPath1);
                    var format2 = DetectFormat(inputPath2);
                    var outputFormat = DetectFormat(outputPath);

                    progress?.Report(L("Reading first layer...", "读取第一个图层..."));
                    var layer1 = await Task.Run(() => OguLayerUtil.ReadLayer(format1, inputPath1), ct);

                    progress?.Report(L("Reading second layer...", "读取第二个图层..."));
                    var layer2 = await Task.Run(() => OguLayerUtil.ReadLayer(format2, inputPath2), ct);

                    progress?.Report(L("Computing union of all geometries...", "计算所有几何体的并集..."));
                    var allWkts = layer1.Features
                        .Concat(layer2.Features)
                        .Where(f => !string.IsNullOrWhiteSpace(f.Wkt))
                        .Select(f => f.Wkt!)
                        .ToList();

                    var unionWkt = GeometryUtil.UnionWkt(allWkts);

                    var outputLayer = new OguLayer
                    {
                        Name = Path.GetFileNameWithoutExtension(outputPath),
                        Wkid = layer1.Wkid,
                        GeometryType = layer1.GeometryType
                    };

                    outputLayer.AddFeature(new OguFeature { Fid = 0, Wkt = unionWkt });

                    progress?.Report(L("Writing output layer...", "写入输出图层..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, outputLayer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Union completed. {allWkts.Count} geometries merged into 1 feature.", $"合并完成。{allWkts.Count} 个几何体合并为 1 个要素。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "交集",
            Description = "Compute the geometric intersection of two vector layers",
            DescriptionZh = "计算两个矢量图层的几何交集",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input1",
                    Label = "First Input File",
                    LabelZh = "第一个输入文件",
                    Description = "First input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "input2",
                    Label = "Second Input File",
                    LabelZh = "第二个输入文件",
                    Description = "Second input vector file (overlay layer)",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output vector file with intersected geometries",
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
                    var inputPath1 = parameters["input1"];
                    var inputPath2 = parameters["input2"];
                    var outputPath = parameters["output"];
                    var format1 = DetectFormat(inputPath1);
                    var format2 = DetectFormat(inputPath2);
                    var outputFormat = DetectFormat(outputPath);

                    progress?.Report(L("Reading first layer...", "读取第一个图层..."));
                    var layer1 = await Task.Run(() => OguLayerUtil.ReadLayer(format1, inputPath1), ct);

                    progress?.Report(L("Reading second layer...", "读取第二个图层..."));
                    var layer2 = await Task.Run(() => OguLayerUtil.ReadLayer(format2, inputPath2), ct);

                    progress?.Report(L("Computing overlay geometry union...", "计算叠加几何体并集..."));
                    var overlayWkts = layer2.Features
                        .Where(f => !string.IsNullOrWhiteSpace(f.Wkt))
                        .Select(f => f.Wkt!)
                        .ToList();
                    var overlayGeom = GeometryUtil.Wkt2Geometry(GeometryUtil.UnionWkt(overlayWkts));

                    progress?.Report(L("Computing intersection for each feature...", "计算每个要素的交集..."));
                    var outputLayer = new OguLayer
                    {
                        Name = Path.GetFileNameWithoutExtension(outputPath),
                        Wkid = layer1.Wkid,
                        GeometryType = layer1.GeometryType
                    };

                    foreach (var field in layer1.Fields)
                        outputLayer.AddField(field.Clone());

                    var retainedCount = 0;
                    foreach (var feature in layer1.Features)
                    {
                        if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;
                        var geom = GeometryUtil.Wkt2Geometry(feature.Wkt);
                        var intersected = GeometryUtil.Intersection(geom, overlayGeom);
                        if (!GeometryUtil.IsEmpty(intersected))
                        {
                            var newFeature = feature.Clone();
                            newFeature.Wkt = GeometryUtil.Geometry2Wkt(intersected);
                            outputLayer.AddFeature(newFeature);
                            retainedCount++;
                        }
                    }

                    progress?.Report(L("Writing output layer...", "写入输出图层..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, outputLayer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Intersection completed. {retainedCount} of {layer1.GetFeatureCount()} features intersected.", $"交集完成。{layer1.GetFeatureCount()} 个要素中有 {retainedCount} 个相交。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "差集",
            Description = "Compute the geometric difference of two vector layers (A minus B)",
            DescriptionZh = "计算两个矢量图层的几何差集（A 减去 B）",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input1",
                    Label = "Input File (A)",
                    LabelZh = "输入文件 (A)",
                    Description = "Input vector file (features to subtract from)",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "input2",
                    Label = "Erase File (B)",
                    LabelZh = "擦除文件 (B)",
                    Description = "Erase vector file (geometry to subtract)",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output vector file with difference geometries",
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
                    var inputPath1 = parameters["input1"];
                    var inputPath2 = parameters["input2"];
                    var outputPath = parameters["output"];
                    var format1 = DetectFormat(inputPath1);
                    var format2 = DetectFormat(inputPath2);
                    var outputFormat = DetectFormat(outputPath);

                    progress?.Report(L("Reading input layer (A)...", "读取输入图层 (A)..."));
                    var layer1 = await Task.Run(() => OguLayerUtil.ReadLayer(format1, inputPath1), ct);

                    progress?.Report(L("Reading erase layer (B)...", "读取擦除图层 (B)..."));
                    var layer2 = await Task.Run(() => OguLayerUtil.ReadLayer(format2, inputPath2), ct);

                    progress?.Report(L("Computing erase geometry union...", "计算擦除几何体并集..."));
                    var eraseWkts = layer2.Features
                        .Where(f => !string.IsNullOrWhiteSpace(f.Wkt))
                        .Select(f => f.Wkt!)
                        .ToList();
                    var eraseGeom = GeometryUtil.Wkt2Geometry(GeometryUtil.UnionWkt(eraseWkts));

                    progress?.Report(L("Computing difference for each feature...", "计算每个要素的差集..."));
                    var outputLayer = new OguLayer
                    {
                        Name = Path.GetFileNameWithoutExtension(outputPath),
                        Wkid = layer1.Wkid,
                        GeometryType = layer1.GeometryType
                    };

                    foreach (var field in layer1.Fields)
                        outputLayer.AddField(field.Clone());

                    var retainedCount = 0;
                    foreach (var feature in layer1.Features)
                    {
                        if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;
                        var geom = GeometryUtil.Wkt2Geometry(feature.Wkt);
                        var diff = GeometryUtil.Difference(geom, eraseGeom);
                        if (!GeometryUtil.IsEmpty(diff))
                        {
                            var newFeature = feature.Clone();
                            newFeature.Wkt = GeometryUtil.Geometry2Wkt(diff);
                            outputLayer.AddFeature(newFeature);
                            retainedCount++;
                        }
                    }

                    progress?.Report(L("Writing output layer...", "写入输出图层..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, outputLayer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Difference completed. {retainedCount} of {layer1.GetFeatureCount()} features retained.", $"差集完成。{layer1.GetFeatureCount()} 个要素中保留了 {retainedCount} 个。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "凸包",
            Description = "Compute the convex hull of each feature in a vector layer",
            DescriptionZh = "计算矢量图层中每个要素的凸包",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output vector file with convex hull geometries",
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

                    progress?.Report(L("Reading input layer...", "读取输入图层..."));
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report(L($"Computing convex hull for {layer.GetFeatureCount()} features...", $"正在计算 {layer.GetFeatureCount()} 个要素的凸包..."));
                    var outputLayer = new OguLayer
                    {
                        Name = Path.GetFileNameWithoutExtension(outputPath),
                        Wkid = layer.Wkid,
                        GeometryType = GeometryType.POLYGON
                    };

                    foreach (var field in layer.Fields)
                        outputLayer.AddField(field.Clone());

                    var processedCount = 0;
                    foreach (var feature in layer.Features)
                    {
                        if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;
                        var geom = GeometryUtil.Wkt2Geometry(feature.Wkt);
                        var hull = GeometryUtil.ConvexHull(geom);
                        var newFeature = feature.Clone();
                        newFeature.Wkt = GeometryUtil.Geometry2Wkt(hull);
                        outputLayer.AddFeature(newFeature);
                        processedCount++;
                    }

                    progress?.Report(L("Writing output layer...", "写入输出图层..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, outputLayer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Convex hull completed for {processedCount} features.", $"凸包计算完成，共处理 {processedCount} 个要素。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "质心",
            Description = "Compute the centroid of each feature in a vector layer",
            DescriptionZh = "计算矢量图层中每个要素的质心",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output point vector file with centroids",
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

                    progress?.Report(L("Reading input layer...", "读取输入图层..."));
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report(L($"Computing centroids for {layer.GetFeatureCount()} features...", $"正在计算 {layer.GetFeatureCount()} 个要素的质心..."));
                    var outputLayer = new OguLayer
                    {
                        Name = Path.GetFileNameWithoutExtension(outputPath),
                        Wkid = layer.Wkid,
                        GeometryType = GeometryType.POINT
                    };

                    foreach (var field in layer.Fields)
                        outputLayer.AddField(field.Clone());

                    var processedCount = 0;
                    foreach (var feature in layer.Features)
                    {
                        if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;
                        var newFeature = feature.Clone();
                        newFeature.Wkt = GeometryUtil.CentroidWkt(feature.Wkt);
                        outputLayer.AddFeature(newFeature);
                        processedCount++;
                    }

                    progress?.Report(L("Writing output layer...", "写入输出图层..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, outputLayer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Centroid completed for {processedCount} features.", $"质心计算完成，共处理 {processedCount} 个要素。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "简化",
            Description = "Simplify geometries in a vector layer using the Douglas-Peucker algorithm",
            DescriptionZh = "使用 Douglas-Peucker 算法简化矢量图层中的几何体",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output vector file with simplified geometries",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "tolerance",
                    Label = "Tolerance",
                    LabelZh = "容差",
                    Description = "Simplification tolerance (in coordinate system units)",
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
                    var tolerance = double.Parse(parameters["tolerance"], CultureInfo.InvariantCulture);
                    var inputFormat = DetectFormat(inputPath);
                    var outputFormat = DetectFormat(outputPath);

                    progress?.Report(L("Reading input layer...", "读取输入图层..."));
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report(L($"Simplifying {layer.GetFeatureCount()} features (tolerance={tolerance})...", $"正在简化 {layer.GetFeatureCount()} 个要素（容差={tolerance}）..."));
                    var outputLayer = new OguLayer
                    {
                        Name = Path.GetFileNameWithoutExtension(outputPath),
                        Wkid = layer.Wkid,
                        GeometryType = layer.GeometryType
                    };

                    foreach (var field in layer.Fields)
                        outputLayer.AddField(field.Clone());

                    var processedCount = 0;
                    foreach (var feature in layer.Features)
                    {
                        if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;
                        var newFeature = feature.Clone();
                        newFeature.Wkt = GeometryUtil.SimplifyWkt(feature.Wkt, tolerance);
                        outputLayer.AddFeature(newFeature);
                        processedCount++;
                    }

                    progress?.Report(L("Writing output layer...", "写入输出图层..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, outputLayer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Simplify completed. {processedCount} features simplified with tolerance {tolerance}.", $"简化完成。{processedCount} 个要素已简化，容差 {tolerance}。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "检查几何",
            Description = "Validate geometries in a vector layer and report issues",
            DescriptionZh = "验证矢量图层中的几何体并报告问题",
            Category = ToolCategory.Validation,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input vector file to validate",
                    Type = ParameterType.InputFile,
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
                    var inputFormat = DetectFormat(inputPath);

                    progress?.Report(L("Reading input layer...", "读取输入图层..."));
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report(L($"Validating {layer.GetFeatureCount()} features...", $"正在验证 {layer.GetFeatureCount()} 个要素..."));
                    var totalCount = 0;
                    var validCount = 0;
                    var invalidCount = 0;
                    var emptyCount = 0;
                    var invalidDetails = new List<string>();

                    foreach (var feature in layer.Features)
                    {
                        totalCount++;
                        if (string.IsNullOrWhiteSpace(feature.Wkt))
                        {
                            emptyCount++;
                            continue;
                        }

                        var geom = GeometryUtil.Wkt2Geometry(feature.Wkt);
                        var validResult = GeometryUtil.IsValid(geom);

                        if (validResult.IsValid)
                        {
                            validCount++;
                        }
                        else
                        {
                            invalidCount++;
                            var reason = !string.IsNullOrWhiteSpace(validResult.ErrorMessage)
                                ? validResult.ErrorMessage
                                : L("Unknown validation error", "未知验证错误");
                            invalidDetails.Add($"  FID {feature.Fid}: {reason}");
                        }
                    }

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine(L("Geometry Validation Report:", "几何验证报告："));
                    sb.AppendLine(L($"  Total features: {totalCount}", $"  总要素数：{totalCount}"));
                    sb.AppendLine(L($"  Valid: {validCount}", $"  有效：{validCount}"));
                    sb.AppendLine(L($"  Invalid: {invalidCount}", $"  无效：{invalidCount}"));
                    if (emptyCount > 0)
                        sb.AppendLine(L($"  Empty/null geometry: {emptyCount}", $"  空几何体：{emptyCount}"));

                    if (invalidDetails.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine(L("Invalid features:", "无效要素："));
                        var limit = Math.Min(invalidDetails.Count, 20);
                        for (var i = 0; i < limit; i++)
                            sb.AppendLine(invalidDetails[i]);
                        if (invalidDetails.Count > 20)
                            sb.AppendLine(L($"  ... and {invalidDetails.Count - 20} more", $"  ... 以及其他 {invalidDetails.Count - 20} 个"));
                    }

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = sb.ToString().TrimEnd(),
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "重投影",
            Description = "Transform a vector layer from one coordinate system to another",
            DescriptionZh = "将矢量图层从一个坐标系转换到另一个坐标系",
            Category = ToolCategory.Coordinate,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output vector file",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "sourceWkid",
                    Label = "Source WKID",
                    LabelZh = "源 WKID",
                    Description = "Source coordinate system WKID (e.g. 4326 for WGS84)",
                    Type = ParameterType.Integer,
                    Required = true,
                    DefaultValue = "4326"
                },
                new()
                {
                    Name = "targetWkid",
                    Label = "Target WKID",
                    LabelZh = "目标 WKID",
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
                    var inputFormat = DetectFormat(inputPath);
                    var outputFormat = DetectFormat(outputPath);

                    progress?.Report(L("Reading input layer...", "读取输入图层..."));
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report(L($"Reprojecting {layer.GetFeatureCount()} features from WKID {sourceWkid} to {targetWkid}...", $"正在将 {layer.GetFeatureCount()} 个要素从 WKID {sourceWkid} 重投影到 {targetWkid}..."));
                    foreach (var feature in layer.Features)
                    {
                        if (!string.IsNullOrWhiteSpace(feature.Wkt))
                            feature.Wkt = CrsUtil.Transform(feature.Wkt, sourceWkid, targetWkid);
                    }

                    layer.Wkid = targetWkid;

                    progress?.Report(L("Writing output layer...", "写入输出图层..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, layer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Reprojection completed. {layer.GetFeatureCount()} features transformed from WKID {sourceWkid} to {targetWkid}.", $"重投影完成。{layer.GetFeatureCount()} 个要素已从 WKID {sourceWkid} 转换到 {targetWkid}。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "计算面积",
            Description = "Calculate the area of each feature in a vector layer",
            DescriptionZh = "计算矢量图层中每个要素的面积",
            Category = ToolCategory.Analysis,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input vector file (polygon layer)",
                    Type = ParameterType.InputFile,
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
                    var inputFormat = DetectFormat(inputPath);

                    progress?.Report(L("Reading input layer...", "读取输入图层..."));
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    var featureCount = layer.GetFeatureCount();
                    if (featureCount == 0)
                    {
                        sw.Stop();
                        return new ToolResult
                        {
                            Success = true,
                            Message = L("The layer contains no features.", "图层不包含任何要素。"),
                            Duration = sw.Elapsed
                        };
                    }

                    progress?.Report(L($"Calculating area for {featureCount} features...", $"正在计算 {featureCount} 个要素的面积..."));
                    var totalArea = 0.0;
                    var sb = new StringBuilder();
                    sb.AppendLine(L($"Layer: {layer.Name ?? Path.GetFileNameWithoutExtension(inputPath)}", $"图层：{layer.Name ?? Path.GetFileNameWithoutExtension(inputPath)}"));
                    sb.AppendLine(L($"Features: {featureCount}", $"要素数：{featureCount}"));
                    sb.AppendLine(new string('-', 40));

                    var maxDetailRows = 50;
                    var detailCount = 0;
                    foreach (var feature in layer.Features)
                    {
                        if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;
                        var area = GeometryUtil.AreaWkt(feature.Wkt);
                        totalArea += area;

                        if (detailCount < maxDetailRows)
                        {
                            sb.AppendLine($"  FID {feature.Fid}: {area:N6}");
                        }
                        detailCount++;
                    }

                    if (detailCount > maxDetailRows)
                    {
                        sb.AppendLine(L($"  ... and {detailCount - maxDetailRows} more features", $"  ... 以及其他 {detailCount - maxDetailRows} 个要素"));
                    }

                    sb.AppendLine(new string('-', 40));
                    sb.AppendLine(L($"Total Area: {totalArea:N6} (coordinate system units²)", $"总面积：{totalArea:N6}（坐标系单位²）"));

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = sb.ToString(),
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "计算长度",
            Description = "Calculate the length of each feature in a vector layer",
            DescriptionZh = "计算矢量图层中每个要素的长度",
            Category = ToolCategory.Analysis,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input vector file (line or polygon layer)",
                    Type = ParameterType.InputFile,
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
                    var inputFormat = DetectFormat(inputPath);

                    progress?.Report(L("Reading input layer...", "读取输入图层..."));
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    var featureCount = layer.GetFeatureCount();
                    if (featureCount == 0)
                    {
                        sw.Stop();
                        return new ToolResult
                        {
                            Success = true,
                            Message = L("The layer contains no features.", "图层不包含任何要素。"),
                            Duration = sw.Elapsed
                        };
                    }

                    progress?.Report(L($"Calculating length for {featureCount} features...", $"正在计算 {featureCount} 个要素的长度..."));
                    var totalLength = 0.0;
                    var sb = new StringBuilder();
                    sb.AppendLine(L($"Layer: {layer.Name ?? Path.GetFileNameWithoutExtension(inputPath)}", $"图层：{layer.Name ?? Path.GetFileNameWithoutExtension(inputPath)}"));
                    sb.AppendLine(L($"Features: {featureCount}", $"要素数：{featureCount}"));
                    sb.AppendLine(new string('-', 40));

                    var maxDetailRows = 50;
                    var detailCount = 0;
                    foreach (var feature in layer.Features)
                    {
                        if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;
                        var length = GeometryUtil.LengthWkt(feature.Wkt);
                        totalLength += length;

                        if (detailCount < maxDetailRows)
                        {
                            sb.AppendLine($"  FID {feature.Fid}: {length:N6}");
                        }
                        detailCount++;
                    }

                    if (detailCount > maxDetailRows)
                    {
                        sb.AppendLine(L($"  ... and {detailCount - maxDetailRows} more features", $"  ... 以及其他 {detailCount - maxDetailRows} 个要素"));
                    }

                    sb.AppendLine(new string('-', 40));
                    sb.AppendLine(L($"Total Length: {totalLength:N6} (coordinate system units)", $"总长度：{totalLength:N6}（坐标系单位）"));

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = sb.ToString(),
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "ZIP 压缩",
            Description = "Compress a folder into a ZIP archive",
            DescriptionZh = "将文件夹压缩为 ZIP 归档",
            Category = ToolCategory.Utility,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input Folder",
                    LabelZh = "输入文件夹",
                    Description = "Folder to compress",
                    Type = ParameterType.FolderPath,
                    Required = true
                },
                new()
                {
                    Name = "output",
                    Label = "Output ZIP File",
                    LabelZh = "输出ZIP文件",
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

                    progress?.Report(L("Compressing folder...", "正在压缩文件夹..."));
                    await Task.Run(() => ZipUtil.Zip(inputFolder, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L("Compression completed.", "压缩完成。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "ZIP 解压",
            Description = "Extract a ZIP archive to a folder",
            DescriptionZh = "将 ZIP 归档解压到文件夹",
            Category = ToolCategory.Utility,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input ZIP File",
                    LabelZh = "输入ZIP文件",
                    Description = "ZIP file to extract",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "ZIP Archive|*.zip"
                },
                new()
                {
                    Name = "output",
                    Label = "Output Folder",
                    LabelZh = "输出文件夹",
                    Description = "Destination folder for extraction",
                    Type = ParameterType.FolderPath,
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

                    progress?.Report(L("Extracting ZIP archive...", "正在解压 ZIP 归档..."));
                    await Task.Run(() => ZipUtil.Unzip(inputPath, outputFolder), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L("Extraction completed.", "解压完成。"),
                        OutputPath = outputFolder,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "CSV → 矢量",
            Description = "Convert CSV file with coordinates to Shapefile",
            DescriptionZh = "将带坐标的 CSV 文件转换为 Shapefile",
            Category = ToolCategory.Conversion,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input CSV file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "CSV files|*.csv"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output Shapefile",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp"
                },
                new()
                {
                    Name = "xField",
                    Label = "X (Longitude) Column Name",
                    LabelZh = "X（经度）列名",
                    Description = "Name of the column containing X/Longitude values",
                    Type = ParameterType.Text,
                    Required = true,
                    DefaultValue = "x"
                },
                new()
                {
                    Name = "yField",
                    Label = "Y (Latitude) Column Name",
                    LabelZh = "Y（纬度）列名",
                    Description = "Name of the column containing Y/Latitude values",
                    Type = ParameterType.Text,
                    Required = true,
                    DefaultValue = "y"
                },
                new()
                {
                    Name = "delimiter",
                    Label = "Delimiter",
                    LabelZh = "分隔符",
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
                    LabelZh = "坐标系 WKID",
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

                    progress?.Report(L("Reading CSV file...", "读取 CSV 文件..."));
                    var lines = await File.ReadAllLinesAsync(inputPath, ct);
                    if (lines.Length < 2)
                        throw new ArgumentException(L("CSV file must have a header row and at least one data row.", "CSV 文件必须包含标题行和至少一行数据。"));

                    var headers = lines[0].Split(delimiter);
                    var xIndex = Array.IndexOf(headers, xField);
                    var yIndex = Array.IndexOf(headers, yField);

                    if (xIndex < 0)
                        throw new ArgumentException(L($"Column '{xField}' not found in CSV header.", $"CSV 标题中未找到列 '{xField}'。"));
                    if (yIndex < 0)
                        throw new ArgumentException(L($"Column '{yField}' not found in CSV header.", $"CSV 标题中未找到列 '{yField}'。"));

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

                    progress?.Report(L("Creating features...", "创建要素..."));
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
                            throw new FormatException(L($"Row {i + 1}: cannot parse X value '{values[xIndex]}' in column '{xField}'.", $"第 {i + 1} 行：无法解析列 '{xField}' 中的 X 值 '{values[xIndex]}'。"));
                        if (!double.TryParse(values[yIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                            throw new FormatException(L($"Row {i + 1}: cannot parse Y value '{values[yIndex]}' in column '{yField}'.", $"第 {i + 1} 行：无法解析列 '{yField}' 中的 Y 值 '{values[yIndex]}'。"));

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

                    progress?.Report(L($"Writing {layer.GetFeatureCount()} features to Shapefile...", $"正在将 {layer.GetFeatureCount()} 个要素写入 Shapefile..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, layer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L(
                            $"Conversion completed. {layer.GetFeatureCount()} features created from CSV." + (skippedCount > 0 ? $" {skippedCount} rows skipped due to column count mismatch." : ""),
                            $"转换完成。从 CSV 创建了 {layer.GetFeatureCount()} 个要素。" + (skippedCount > 0 ? $" 因列数不匹配跳过了 {skippedCount} 行。" : "")),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "修复几何",
            Description = "Fix invalid geometries using the buffer(0) technique",
            DescriptionZh = "使用 buffer(0) 技术修复无效几何体",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
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

                    progress?.Report(L("Reading input layer...", "读取输入图层..."));
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report(L("Fixing geometries...", "修复几何体..."));
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

                    progress?.Report(L("Writing output layer...", "写入输出图层..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, layer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Fix geometries completed. {fixedCount} of {layer.GetFeatureCount()} geometries were fixed.", $"几何修复完成。{layer.GetFeatureCount()} 个几何体中修复了 {fixedCount} 个。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "合并图层",
            Description = "Merge two vector layers into one",
            DescriptionZh = "将两个矢量图层合并为一个",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input1",
                    Label = "First Input File",
                    LabelZh = "第一个输入文件",
                    Description = "First input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg|KML|*.kml"
                },
                new()
                {
                    Name = "input2",
                    Label = "Second Input File",
                    LabelZh = "第二个输入文件",
                    Description = "Second input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg|KML|*.kml"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
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

                    progress?.Report(L("Reading first layer...", "读取第一个图层..."));
                    var layer1 = await Task.Run(() => OguLayerUtil.ReadLayer(format1, inputPath1), ct);

                    progress?.Report(L("Reading second layer...", "读取第二个图层..."));
                    var layer2 = await Task.Run(() => OguLayerUtil.ReadLayer(format2, inputPath2), ct);

                    progress?.Report(L("Merging layers...", "合并图层..."));
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

                    progress?.Report(L("Writing merged layer...", "写入合并后的图层..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, merged, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Merge completed. {merged.GetFeatureCount()} total features ({layer1.GetFeatureCount()} + {layer2.GetFeatureCount()}).", $"合并完成。共 {merged.GetFeatureCount()} 个要素（{layer1.GetFeatureCount()} + {layer2.GetFeatureCount()}）。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "拆分图层",
            Description = "Split a vector layer into multiple layers by field value",
            DescriptionZh = "按字段值将矢量图层拆分为多个图层",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "fieldName",
                    Label = "Split Field Name",
                    LabelZh = "拆分字段名",
                    Description = "Name of the field to split by",
                    Type = ParameterType.Text,
                    Required = true
                },
                new()
                {
                    Name = "outputFolder",
                    Label = "Output Folder",
                    LabelZh = "输出文件夹",
                    Description = "Folder to write split layers to",
                    Type = ParameterType.FolderPath,
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

                    progress?.Report(L("Reading input layer...", "正在读取输入图层..."));
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    if (!Directory.Exists(outputFolder))
                        Directory.CreateDirectory(outputFolder);

                    progress?.Report(L("Grouping features by field value...", "正在按字段值分组要素..."));
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

                        progress?.Report(L($"Writing group '{group.Key}' ({groupLayer.GetFeatureCount()} features)...",
                            $"正在写入分组 '{group.Key}'（{groupLayer.GetFeatureCount()} 个要素）..."));
                        await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, groupLayer, outputPath), ct);
                        count++;
                    }

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Split completed. {count} layers created from {layer.GetFeatureCount()} features.",
                            $"分割完成。从 {layer.GetFeatureCount()} 个要素创建了 {count} 个图层。"),
                        OutputPath = outputFolder,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "裁剪",
            Description = "Clip a vector layer by a polygon layer",
            DescriptionZh = "使用多边形图层裁剪矢量图层",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input vector file to clip",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "clipLayer",
                    Label = "Clip Polygon File",
                    LabelZh = "裁剪多边形文件",
                    Description = "Polygon layer to clip by",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
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

                    progress?.Report(L("Reading input layer...", "正在读取输入图层..."));
                    var inputLayer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report(L("Reading clip layer...", "正在读取裁剪图层..."));
                    var clipLayer = await Task.Run(() => OguLayerUtil.ReadLayer(clipFormat, clipPath), ct);

                    progress?.Report(L("Computing clip geometry union...", "正在计算裁剪几何合并..."));
                    var clipWkts = clipLayer.Features
                        .Where(f => !string.IsNullOrWhiteSpace(f.Wkt))
                        .Select(f => f.Wkt!)
                        .ToList();

                    var clipGeom = GeometryUtil.Wkt2Geometry(GeometryUtil.UnionWkt(clipWkts));

                    progress?.Report(L("Clipping features...", "正在裁剪要素..."));
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

                    progress?.Report(L("Writing output layer...", "正在写入输出图层..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, outputLayer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Clip completed. {outputLayer.GetFeatureCount()} of {inputLayer.GetFeatureCount()} features retained.",
                            $"裁剪完成。保留了 {inputLayer.GetFeatureCount()} 个要素中的 {outputLayer.GetFeatureCount()} 个。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "空间连接",
            Description = "Join attributes from one layer to another based on spatial relationship",
            DescriptionZh = "基于空间关系将一个图层的属性连接到另一个图层",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Target Layer",
                    LabelZh = "目标图层",
                    Description = "Target vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "joinLayer",
                    Label = "Join Layer",
                    LabelZh = "连接图层",
                    Description = "Layer to join attributes from",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output Shapefile",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp"
                },
                new()
                {
                    Name = "joinType",
                    Label = "Spatial Relationship",
                    LabelZh = "空间关系",
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

                    progress?.Report(L("Reading target layer...", "正在读取目标图层..."));
                    var targetLayer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report(L("Reading join layer...", "正在读取连接图层..."));
                    var joinLayer = await Task.Run(() => OguLayerUtil.ReadLayer(joinFormat, joinPath), ct);

                    progress?.Report(L("Performing spatial join...", "正在执行空间连接..."));
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

                    progress?.Report(L("Writing output layer...", "正在写入输出图层..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, outputLayer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Spatial join completed. {joinedCount} of {targetLayer.GetFeatureCount()} features joined.",
                            $"空间连接完成。{targetLayer.GetFeatureCount()} 个要素中有 {joinedCount} 个完成连接。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "批量重投影",
            Description = "Reproject all vector files in a folder to a different coordinate system",
            DescriptionZh = "将文件夹中所有矢量文件重投影到不同的坐标系",
            Category = ToolCategory.Coordinate,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "inputFolder",
                    Label = "Input Folder",
                    LabelZh = "输入文件夹",
                    Description = "Folder containing vector files",
                    Type = ParameterType.FolderPath,
                    Required = true
                },
                new()
                {
                    Name = "outputFolder",
                    Label = "Output Folder",
                    LabelZh = "输出文件夹",
                    Description = "Folder to write reprojected files to",
                    Type = ParameterType.FolderPath,
                    Required = true
                },
                new()
                {
                    Name = "sourceWkid",
                    Label = "Source WKID",
                    LabelZh = "源 WKID",
                    Description = "Source coordinate system WKID (e.g. 4326 for WGS84)",
                    Type = ParameterType.Integer,
                    Required = true,
                    DefaultValue = "4326"
                },
                new()
                {
                    Name = "targetWkid",
                    Label = "Target WKID",
                    LabelZh = "目标 WKID",
                    Description = "Target coordinate system WKID (e.g. 4490 for CGCS2000)",
                    Type = ParameterType.Integer,
                    Required = true,
                    DefaultValue = "4490"
                },
                new()
                {
                    Name = "format",
                    Label = "File Format",
                    LabelZh = "文件格式",
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
                        throw new ArgumentException(L($"No {format} files found in the input folder.", $"输入文件夹中未找到 {format} 文件。"));

                    var processedCount = 0;
                    foreach (var file in files)
                    {
                        progress?.Report(L($"Processing {Path.GetFileName(file)} ({processedCount + 1}/{files.Length})...",
                            $"正在处理 {Path.GetFileName(file)}（{processedCount + 1}/{files.Length}）..."));

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
                        Message = L($"Batch reproject completed. {processedCount} files reprojected from WKID {sourceWkid} to {targetWkid}.",
                            $"批量重投影完成。{processedCount} 个文件已从 WKID {sourceWkid} 重投影到 {targetWkid}。"),
                        OutputPath = outputFolder,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "空间过滤",
            Description = "Filter features by spatial extent (WKT polygon)",
            DescriptionZh = "按空间范围过滤要素（WKT 多边形）",
            Category = ToolCategory.Analysis,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output vector file",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "extentWkt",
                    Label = "Filter Extent (WKT)",
                    LabelZh = "过滤范围 (WKT)",
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

                    progress?.Report(L("Reading input layer...", "正在读取输入图层..."));
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report(L("Filtering features by spatial extent...", "正在按空间范围过滤要素..."));
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

                    progress?.Report(L("Writing output layer...", "正在写入输出图层..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, outputLayer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Spatial filter completed. {outputLayer.GetFeatureCount()} of {layer.GetFeatureCount()} features matched.",
                            $"空间过滤完成。{layer.GetFeatureCount()} 个要素中有 {outputLayer.GetFeatureCount()} 个匹配。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "属性查询",
            Description = "Filter features by attribute expression (SQL WHERE clause)",
            DescriptionZh = "按属性表达式过滤要素（SQL WHERE 子句）",
            Category = ToolCategory.Analysis,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output Shapefile",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp"
                },
                new()
                {
                    Name = "whereClause",
                    Label = "WHERE Clause",
                    LabelZh = "WHERE 子句",
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

                    progress?.Report(L("Reading and filtering layer...", "正在读取并过滤图层..."));
                    var layer = await Task.Run(() =>
                        OguLayerUtil.ReadLayer(inputFormat, inputPath, null, whereClause, null, null, null), ct);

                    progress?.Report(L($"Writing {layer.GetFeatureCount()} filtered features...",
                        $"正在写入 {layer.GetFeatureCount()} 个过滤后的要素..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, layer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Attribute query completed. {layer.GetFeatureCount()} features matched.",
                            $"属性查询完成。匹配了 {layer.GetFeatureCount()} 个要素。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "PostGIS 导入",
            Description = "Import a PostGIS table to a Shapefile",
            DescriptionZh = "将 PostGIS 表导入为 Shapefile",
            Category = ToolCategory.Conversion,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "connectionString",
                    Label = "Connection String",
                    LabelZh = "连接字符串",
                    Description = "PostgreSQL connection string (e.g. Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass)",
                    Type = ParameterType.Text,
                    Required = true
                },
                new()
                {
                    Name = "tableName",
                    Label = "Table Name",
                    LabelZh = "表名",
                    Description = "PostGIS table name to import",
                    Type = ParameterType.Text,
                    Required = true
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
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

                    progress?.Report(L("Reading from PostGIS...", "正在从 PostGIS 读取..."));
                    var layer = await Task.Run(() => PostgisUtil.ReadPostGIS(connectionString, tableName, null), ct);

                    progress?.Report(L($"Writing {layer.GetFeatureCount()} features to Shapefile...",
                        $"正在将 {layer.GetFeatureCount()} 个要素写入 Shapefile..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, layer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"PostGIS import completed. {layer.GetFeatureCount()} features imported.",
                            $"PostGIS 导入完成。已导入 {layer.GetFeatureCount()} 个要素。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
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
            NameZh = "PostGIS 导出",
            Description = "Export a vector file to a PostGIS table",
            DescriptionZh = "将矢量文件导出到 PostGIS 表",
            Category = ToolCategory.Conversion,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "connectionString",
                    Label = "Connection String",
                    LabelZh = "连接字符串",
                    Description = "PostgreSQL connection string (e.g. Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass)",
                    Type = ParameterType.Text,
                    Required = true
                },
                new()
                {
                    Name = "tableName",
                    Label = "Table Name",
                    LabelZh = "表名",
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

                    progress?.Report(L("Reading input layer...", "正在读取输入图层..."));
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report(L($"Exporting {layer.GetFeatureCount()} features to PostGIS...",
                        $"正在将 {layer.GetFeatureCount()} 个要素导出到 PostGIS..."));
                    await Task.Run(() => PostgisUtil.WritePostGIS(layer, connectionString, tableName), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"PostGIS export completed. {layer.GetFeatureCount()} features exported to table '{tableName}'.",
                            $"PostGIS 导出完成。已将 {layer.GetFeatureCount()} 个要素导出到表 '{tableName}'。"),
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateCentralLinesTool()
    {
        return new ToolInfo
        {
            Id = "central-lines",
            Name = "Central Lines",
            NameZh = "中心线",
            Description = "Approximate center lines of polygons using negative buffering",
            DescriptionZh = "使用负缓冲区近似计算多边形的中心线",
            Category = ToolCategory.Geometry,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Input polygon vector file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output Shapefile with center lines",
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
                    var outputPath = parameters["output"];
                    var inputFormat = DetectFormat(inputPath);

                    progress?.Report(L("Reading input layer...", "正在读取输入图层..."));
                    var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

                    progress?.Report(L("Computing center lines...", "正在计算中心线..."));
                    var outputLayer = new OguLayer
                    {
                        Name = Path.GetFileNameWithoutExtension(outputPath),
                        Wkid = layer.Wkid,
                        GeometryType = GeometryType.MULTILINESTRING
                    };

                    foreach (var field in layer.Fields)
                        outputLayer.AddField(field.Clone());

                    var processedCount = 0;
                    foreach (var feature in layer.Features)
                    {
                        if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;

                        var geom = GeometryUtil.Wkt2Geometry(feature.Wkt);
                        var area = GeometryUtil.Area(geom);
                        var boundary = GeometryUtil.Boundary(geom);
                        var boundaryLength = GeometryUtil.Length(boundary);

                        var newFeature = feature.Clone();

                        if (boundaryLength > 0 && area > 0)
                        {
                            var approxWidth = 2.0 * area / boundaryLength;
                            var negativeBuffer = GeometryUtil.Buffer(geom, -approxWidth * 0.45);

                            if (!GeometryUtil.IsEmpty(negativeBuffer))
                            {
                                var centerLine = GeometryUtil.Boundary(negativeBuffer);
                                newFeature.Wkt = GeometryUtil.Geometry2Wkt(centerLine);
                            }
                            else
                            {
                                var centroid = GeometryUtil.Centroid(geom);
                                newFeature.Wkt = GeometryUtil.Geometry2Wkt(centroid);
                            }
                        }
                        else
                        {
                            var centroid = GeometryUtil.Centroid(geom);
                            newFeature.Wkt = GeometryUtil.Geometry2Wkt(centroid);
                        }

                        outputLayer.AddFeature(newFeature);
                        processedCount++;
                    }

                    progress?.Report(L("Writing output layer...", "正在写入输出图层..."));
                    await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, outputLayer, outputPath), ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Center lines computed for {processedCount} features.",
                            $"已为 {processedCount} 个要素计算中心线。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateRasterFormatConvertTool()
    {
        return new ToolInfo
        {
            Id = "raster-format-convert",
            Name = "Raster Format Conversion",
            NameZh = "栅格格式转换",
            Description = "Convert raster data between different formats (GeoTIFF, PNG, JPEG, BMP)",
            DescriptionZh = "在不同格式之间转换栅格数据（GeoTIFF、PNG、JPEG、BMP）",
            Category = ToolCategory.Raster,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input Raster File",
                    LabelZh = "输入栅格文件",
                    Description = "Input raster file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Raster files|*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.bmp"
                },
                new()
                {
                    Name = "output",
                    Label = "Output Raster File",
                    LabelZh = "输出栅格文件",
                    Description = "Output raster file",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "GeoTIFF|*.tif|PNG|*.png|JPEG|*.jpg|BMP|*.bmp"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var outputPath = parameters["output"];

                    progress?.Report(L("Initializing GDAL...", "正在初始化 GDAL..."));
                    await Task.Run(EnsureGdalInitialized, ct);

                    var ext = Path.GetExtension(outputPath)?.ToLowerInvariant();
                    var driverName = ext switch
                    {
                        ".png" => "PNG",
                        ".jpg" or ".jpeg" => "JPEG",
                        ".bmp" => "BMP",
                        _ => "GTiff"
                    };

                    progress?.Report(L("Opening input raster...", "正在打开输入栅格..."));
                    using var srcDs = Gdal.Open(inputPath, Access.GA_ReadOnly);
                    if (srcDs == null)
                        throw new Exception(L("Failed to open input raster file.", "无法打开输入栅格文件。"));

                    progress?.Report(L($"Converting to {driverName} format...", $"正在转换为 {driverName} 格式..."));
                    var driver = Gdal.GetDriverByName(driverName);
                    if (driver == null)
                        throw new Exception(L($"GDAL driver '{driverName}' not found.", $"未找到 GDAL 驱动 '{driverName}'。"));

                    using var outDs = driver.CreateCopy(outputPath, srcDs, 0, null, null, null);
                    if (outDs == null)
                        throw new Exception(L("Failed to create output raster file.", "无法创建输出栅格文件。"));

                    outDs.FlushCache();

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Raster format conversion completed ({driverName}).",
                            $"栅格格式转换完成（{driverName}）。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateRasterCalculatorTool()
    {
        return new ToolInfo
        {
            Id = "raster-calculator",
            Name = "Raster Calculator",
            NameZh = "栅格计算器",
            Description = "Perform band math operations on raster data",
            DescriptionZh = "对栅格数据执行波段数学运算",
            Category = ToolCategory.Raster,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input Raster File",
                    LabelZh = "输入栅格文件",
                    Description = "Input GeoTIFF raster file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "GeoTIFF|*.tif;*.tiff"
                },
                new()
                {
                    Name = "output",
                    Label = "Output Raster File",
                    LabelZh = "输出栅格文件",
                    Description = "Output GeoTIFF raster file",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "GeoTIFF|*.tif"
                },
                new()
                {
                    Name = "operation",
                    Label = "Operation",
                    LabelZh = "运算",
                    Description = "Band math operation to perform",
                    Type = ParameterType.Dropdown,
                    Required = true,
                    DefaultValue = "NDVI (Band4-Band3)/(Band4+Band3)",
                    Options = new[] { "NDVI (Band4-Band3)/(Band4+Band3)", "Scale (Band1 * Factor)", "Offset (Band1 + Value)", "Threshold (Band1 > Value)" }
                },
                new()
                {
                    Name = "value",
                    Label = "Value",
                    LabelZh = "值",
                    Description = "Value for scale factor, offset, or threshold",
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
                    var operation = parameters["operation"];
                    var value = double.Parse(parameters["value"], CultureInfo.InvariantCulture);

                    progress?.Report(L("Initializing GDAL...", "正在初始化 GDAL..."));
                    await Task.Run(EnsureGdalInitialized, ct);

                    progress?.Report(L("Opening input raster...", "正在打开输入栅格..."));
                    using var srcDs = Gdal.Open(inputPath, Access.GA_ReadOnly);
                    if (srcDs == null)
                        throw new Exception(L("Failed to open input raster file.", "无法打开输入栅格文件。"));

                    var width = srcDs.RasterXSize;
                    var height = srcDs.RasterYSize;
                    var bandCount = srcDs.RasterCount;

                    progress?.Report(L($"Raster size: {width}x{height}, {bandCount} bands. Computing...",
                        $"栅格大小：{width}x{height}，{bandCount} 个波段。正在计算..."));

                    double[] result = new double[width * height];

                    if (operation.StartsWith("NDVI"))
                    {
                        if (bandCount < 4)
                            throw new Exception(L("NDVI requires at least 4 bands (Red=Band3, NIR=Band4).",
                                "NDVI 需要至少 4 个波段（红色=Band3，近红外=Band4）。"));

                        var red = new double[width * height];
                        var nir = new double[width * height];
                        srcDs.GetRasterBand(3).ReadRaster(0, 0, width, height, red, width, height, 0, 0);
                        srcDs.GetRasterBand(4).ReadRaster(0, 0, width, height, nir, width, height, 0, 0);

                        for (int i = 0; i < result.Length; i++)
                        {
                            var sum = nir[i] + red[i];
                            result[i] = sum == 0 ? 0 : (nir[i] - red[i]) / sum;
                        }
                    }
                    else if (operation.StartsWith("Scale"))
                    {
                        var band1 = new double[width * height];
                        srcDs.GetRasterBand(1).ReadRaster(0, 0, width, height, band1, width, height, 0, 0);
                        for (int i = 0; i < result.Length; i++)
                            result[i] = band1[i] * value;
                    }
                    else if (operation.StartsWith("Offset"))
                    {
                        var band1 = new double[width * height];
                        srcDs.GetRasterBand(1).ReadRaster(0, 0, width, height, band1, width, height, 0, 0);
                        for (int i = 0; i < result.Length; i++)
                            result[i] = band1[i] + value;
                    }
                    else if (operation.StartsWith("Threshold"))
                    {
                        var band1 = new double[width * height];
                        srcDs.GetRasterBand(1).ReadRaster(0, 0, width, height, band1, width, height, 0, 0);
                        for (int i = 0; i < result.Length; i++)
                            result[i] = band1[i] > value ? 1.0 : 0.0;
                    }

                    progress?.Report(L("Writing output raster...", "正在写入输出栅格..."));
                    var driver = Gdal.GetDriverByName("GTiff");
                    if (driver == null)
                        throw new Exception(L("GTiff driver not found.", "未找到 GTiff 驱动。"));

                    using var outDs = driver.Create(outputPath, width, height, 1, DataType.GDT_Float64, null);
                    if (outDs == null)
                        throw new Exception(L("Failed to create output raster file.", "无法创建输出栅格文件。"));

                    var geoTransform = new double[6];
                    srcDs.GetGeoTransform(geoTransform);
                    outDs.SetGeoTransform(geoTransform);
                    outDs.SetProjection(srcDs.GetProjection());

                    var outBand = outDs.GetRasterBand(1);
                    outBand.WriteRaster(0, 0, width, height, result, width, height, 0, 0);
                    outBand.FlushCache();
                    outDs.FlushCache();

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Raster calculator completed. Operation: {operation}, Value: {value}.",
                            $"栅格计算完成。运算：{operation}，值：{value}。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateSatelliteDownloadTool()
    {
        return new ToolInfo
        {
            Id = "satellite-download",
            Name = "Satellite Image Download",
            NameZh = "卫星影像下载",
            Description = "Download satellite imagery tiles from public tile services (OpenStreetMap, Esri World Imagery)",
            DescriptionZh = "从公共瓦片服务下载卫星影像瓦片（OpenStreetMap、Esri World Imagery）",
            Category = ToolCategory.RemoteSensing,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "longitude",
                    Label = "Center Longitude",
                    LabelZh = "中心经度",
                    Description = "Center longitude in degrees",
                    Type = ParameterType.Number,
                    Required = true,
                    DefaultValue = "116.4"
                },
                new()
                {
                    Name = "latitude",
                    Label = "Center Latitude",
                    LabelZh = "中心纬度",
                    Description = "Center latitude in degrees",
                    Type = ParameterType.Number,
                    Required = true,
                    DefaultValue = "39.9"
                },
                new()
                {
                    Name = "zoom",
                    Label = "Zoom Level",
                    LabelZh = "缩放级别",
                    Description = "Zoom level (0-19)",
                    Type = ParameterType.Integer,
                    Required = true,
                    DefaultValue = "10"
                },
                new()
                {
                    Name = "source",
                    Label = "Tile Source",
                    LabelZh = "瓦片来源",
                    Description = "Tile service provider",
                    Type = ParameterType.Dropdown,
                    Required = true,
                    DefaultValue = "OpenStreetMap",
                    Options = new[] { "OpenStreetMap", "Esri World Imagery" }
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output image file",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "PNG|*.png"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var lon = double.Parse(parameters["longitude"], CultureInfo.InvariantCulture);
                    var lat = double.Parse(parameters["latitude"], CultureInfo.InvariantCulture);
                    var zoom = int.Parse(parameters["zoom"]);
                    var source = parameters["source"];
                    var outputPath = parameters["output"];

                    progress?.Report(L("Computing tile coordinates...", "正在计算瓦片坐标..."));
                    var n = Math.Pow(2, zoom);
                    var tileX = (int)Math.Floor((lon + 180.0) / 360.0 * n);
                    var latRad = lat * Math.PI / 180.0;
                    var tileY = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);

                    var url = source == "Esri World Imagery"
                        ? $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{zoom}/{tileY}/{tileX}"
                        : $"https://tile.openstreetmap.org/{zoom}/{tileX}/{tileY}.png";

                    progress?.Report(L($"Downloading tile ({tileX}, {tileY}) at zoom {zoom}...",
                        $"正在下载瓦片（{tileX}, {tileY}），缩放级别 {zoom}..."));
                    var httpClient = SharedHttpClient.Value;

                    var imageBytes = await httpClient.GetByteArrayAsync(url, ct);
                    await File.WriteAllBytesAsync(outputPath, imageBytes, ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Tile downloaded from {source}. Tile ({tileX}, {tileY}) at zoom {zoom}. Size: {imageBytes.Length} bytes.",
                            $"已从 {source} 下载瓦片。瓦片（{tileX}, {tileY}），缩放级别 {zoom}。大小：{imageBytes.Length} 字节。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateGpxProcessingTool()
    {
        return new ToolInfo
        {
            Id = "gpx-processing",
            Name = "GPX Processing",
            NameZh = "GPX 处理",
            Description = "Process GPX files: merge multiple GPX files, extract tracks/waypoints, or convert GPX to GeoJSON",
            DescriptionZh = "处理 GPX 文件：合并多个 GPX 文件、提取轨迹/路点或将 GPX 转换为 GeoJSON",
            Category = ToolCategory.GPS,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input GPX File",
                    LabelZh = "输入GPX文件",
                    Description = "Input GPX file",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "GPX files|*.gpx"
                },
                new()
                {
                    Name = "operation",
                    Label = "Operation",
                    LabelZh = "操作",
                    Description = "Processing operation to perform",
                    Type = ParameterType.Dropdown,
                    Required = true,
                    DefaultValue = "Extract Waypoints to CSV",
                    Options = new[] { "Extract Waypoints to CSV", "Extract Tracks to GeoJSON", "GPX Summary" }
                },
                new()
                {
                    Name = "output",
                    Label = "Output File",
                    LabelZh = "输出文件",
                    Description = "Output file (not required for GPX Summary)",
                    Type = ParameterType.OutputFile,
                    Required = false,
                    FileFilter = "CSV|*.csv|GeoJSON|*.geojson|Text|*.txt"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var operation = parameters["operation"];
                    var outputPath = parameters.GetValueOrDefault("output", "");

                    progress?.Report(L("Loading GPX file...", "正在加载 GPX 文件..."));
                    var doc = XDocument.Load(inputPath);
                    XNamespace gpxNs = "http://www.topografix.com/GPX/1/1";

                    if (operation == "Extract Waypoints to CSV")
                    {
                        if (string.IsNullOrWhiteSpace(outputPath))
                            throw new ArgumentException(L("Output file is required for this operation.",
                                "此操作需要输出文件。"));

                        progress?.Report(L("Extracting waypoints...", "正在提取路点..."));
                        var waypoints = doc.Descendants(gpxNs + "wpt").ToList();
                        var lines = new List<string> { "Name,Latitude,Longitude,Elevation,Description" };

                        foreach (var wpt in waypoints)
                        {
                            var lat = wpt.Attribute("lat")?.Value ?? "";
                            var lon = wpt.Attribute("lon")?.Value ?? "";
                            var name = wpt.Element(gpxNs + "name")?.Value ?? "";
                            var ele = wpt.Element(gpxNs + "ele")?.Value ?? "";
                            var desc = wpt.Element(gpxNs + "desc")?.Value ?? "";
                            lines.Add($"{EscapeCsv(name)},{lat},{lon},{ele},{EscapeCsv(desc)}");
                        }

                        await File.WriteAllLinesAsync(outputPath, lines, ct);

                        sw.Stop();
                        return new ToolResult
                        {
                            Success = true,
                            Message = L($"Extracted {waypoints.Count} waypoints to CSV.",
                                $"已提取 {waypoints.Count} 个路点到 CSV。"),
                            OutputPath = outputPath,
                            Duration = sw.Elapsed
                        };
                    }
                    else if (operation == "Extract Tracks to GeoJSON")
                    {
                        if (string.IsNullOrWhiteSpace(outputPath))
                            throw new ArgumentException(L("Output file is required for this operation.",
                                "此操作需要输出文件。"));

                        progress?.Report(L("Extracting tracks...", "正在提取轨迹..."));
                        var tracks = doc.Descendants(gpxNs + "trk").ToList();
                        var features = new List<object>();

                        foreach (var trk in tracks)
                        {
                            var trackName = trk.Element(gpxNs + "name")?.Value ?? "";
                            var segments = trk.Descendants(gpxNs + "trkseg").ToList();

                            foreach (var seg in segments)
                            {
                                var points = seg.Descendants(gpxNs + "trkpt")
                                    .Select(pt => new[]
                                    {
                                        double.Parse(pt.Attribute("lon")?.Value ?? "0", CultureInfo.InvariantCulture),
                                        double.Parse(pt.Attribute("lat")?.Value ?? "0", CultureInfo.InvariantCulture)
                                    })
                                    .ToList();

                                if (points.Count >= 2)
                                {
                                    features.Add(new
                                    {
                                        type = "Feature",
                                        properties = new { name = trackName },
                                        geometry = new
                                        {
                                            type = "LineString",
                                            coordinates = points
                                        }
                                    });
                                }
                            }
                        }

                        var geojson = new { type = "FeatureCollection", features };
                        var json = JsonSerializer.Serialize(geojson, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(outputPath, json, ct);

                        sw.Stop();
                        return new ToolResult
                        {
                            Success = true,
                            Message = L($"Extracted {features.Count} track segments to GeoJSON.",
                                $"已提取 {features.Count} 个轨迹段到 GeoJSON。"),
                            OutputPath = outputPath,
                            Duration = sw.Elapsed
                        };
                    }
                    else // GPX Summary
                    {
                        progress?.Report(L("Analyzing GPX file...", "正在分析 GPX 文件..."));
                        var waypointCount = doc.Descendants(gpxNs + "wpt").Count();
                        var trackCount = doc.Descendants(gpxNs + "trk").Count();
                        var segmentCount = doc.Descendants(gpxNs + "trkseg").Count();
                        var trackPointCount = doc.Descendants(gpxNs + "trkpt").Count();

                        var message = L(
                            $"GPX Summary:\n" +
                            $"  Waypoints: {waypointCount}\n" +
                            $"  Tracks: {trackCount}\n" +
                            $"  Track Segments: {segmentCount}\n" +
                            $"  Total Track Points: {trackPointCount}",
                            $"GPX 摘要：\n" +
                            $"  路点数：{waypointCount}\n" +
                            $"  轨迹数：{trackCount}\n" +
                            $"  轨迹段数：{segmentCount}\n" +
                            $"  总轨迹点数：{trackPointCount}");

                        sw.Stop();
                        return new ToolResult
                        {
                            Success = true,
                            Message = message,
                            Duration = sw.Elapsed
                        };
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
                }
            }
        };
    }

    private static ToolInfo CreateGeocodeAddressesTool()
    {
        return new ToolInfo
        {
            Id = "geocode-addresses",
            Name = "Geocode Addresses",
            NameZh = "地址编码",
            Description = "Geocode addresses to coordinates using OpenStreetMap Nominatim service",
            DescriptionZh = "使用 OpenStreetMap Nominatim 服务将地址编码为坐标",
            Category = ToolCategory.Geocoding,
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "input",
                    Label = "Input File",
                    LabelZh = "输入文件",
                    Description = "Text file with one address per line",
                    Type = ParameterType.InputFile,
                    Required = true,
                    FileFilter = "Text files|*.txt|CSV files|*.csv"
                },
                new()
                {
                    Name = "output",
                    Label = "Output CSV File",
                    LabelZh = "输出CSV文件",
                    Description = "Output CSV file with geocoded coordinates",
                    Type = ParameterType.OutputFile,
                    Required = true,
                    FileFilter = "CSV|*.csv"
                }
            },
            ExecuteAsync = async (parameters, progress, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var inputPath = parameters["input"];
                    var outputPath = parameters["output"];

                    progress?.Report(L("Reading addresses...", "正在读取地址..."));
                    var lines = await File.ReadAllLinesAsync(inputPath, ct);
                    var addresses = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

                    if (addresses.Count == 0)
                        throw new ArgumentException(L("No addresses found in input file.", "输入文件中未找到地址。"));

                    var results = new List<string> { "Address,Latitude,Longitude,DisplayName" };

                    var httpClient = SharedHttpClient.Value;

                    var geocodedCount = 0;
                    for (var i = 0; i < addresses.Count; i++)
                    {
                        var address = addresses[i].Trim();
                        progress?.Report(L($"Geocoding {i + 1}/{addresses.Count}: {address}...",
                            $"正在编码 {i + 1}/{addresses.Count}：{address}..."));

                        var lat = "";
                        var lon = "";
                        var displayName = "";

                        try
                        {
                            var encodedAddress = Uri.EscapeDataString(address);
                            var url = $"https://nominatim.openstreetmap.org/search?q={encodedAddress}&format=json&limit=1";
                            var response = await httpClient.GetStringAsync(url, ct);

                            using var jsonDoc = JsonDocument.Parse(response);
                            var root = jsonDoc.RootElement;

                            if (root.GetArrayLength() > 0)
                            {
                                var first = root[0];
                                lat = first.GetProperty("lat").GetString() ?? "";
                                lon = first.GetProperty("lon").GetString() ?? "";
                                displayName = first.GetProperty("display_name").GetString() ?? "";
                                geocodedCount++;
                            }
                        }
                        catch (HttpRequestException)
                        {
                            progress?.Report(L($"  Warning: HTTP request failed for '{address}'.",
                                $"  警告：'{address}' 的 HTTP 请求失败。"));
                        }
                        catch (JsonException)
                        {
                            progress?.Report(L($"  Warning: Failed to parse response for '{address}'.",
                                $"  警告：解析 '{address}' 的响应失败。"));
                        }

                        results.Add($"{EscapeCsv(address)},{lat},{lon},{EscapeCsv(displayName)}");

                        // Nominatim usage policy requires max 1 request per second
                        if (i < addresses.Count - 1)
                            await Task.Delay(1000, ct);
                    }

                    await File.WriteAllLinesAsync(outputPath, results, ct);

                    sw.Stop();
                    return new ToolResult
                    {
                        Success = true,
                        Message = L($"Geocoding completed. {geocodedCount} of {addresses.Count} addresses geocoded.",
                            $"地址编码完成。{addresses.Count} 个地址中有 {geocodedCount} 个编码成功。"),
                        OutputPath = outputPath,
                        Duration = sw.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new ToolResult { Success = false, Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"), Duration = sw.Elapsed };
                }
            }
        };
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
