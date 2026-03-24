using System;
using System.Collections.Generic;
using OpenGIS.Utils.Engine.Enums;
using OpenGISToolbox.Models;
using OpenGISToolbox.Tools;

namespace OpenGISToolbox.Services;

/// <summary>
/// Registry that discovers and caches all available GIS tools.
/// Each tool is implemented as a <see cref="ToolBase"/> subclass in the Tools/ directory.
/// </summary>
public static class ToolRegistry
{
    private static readonly Lazy<List<ToolInfo>> CachedTools = new(BuildAllTools);

    public static List<ToolInfo> GetAllTools() => CachedTools.Value;

    private static List<ToolInfo> BuildAllTools()
    {
        var tools = new List<ToolInfo>();

        // ── Format Conversion tools (11 parameterized instances) ──
        tools.Add(new FormatConversionTool("shp-to-geojson", "SHP → GeoJSON", "SHP → GeoJSON",
            "Convert Shapefile to GeoJSON format", "将 Shapefile 转换为 GeoJSON 格式",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.GEOJSON, ".geojson", "GeoJSON|*.geojson").ToToolInfo());

        tools.Add(new FormatConversionTool("geojson-to-shp", "GeoJSON → SHP", "GeoJSON → SHP",
            "Convert GeoJSON to Shapefile format", "将 GeoJSON 转换为 Shapefile 格式",
            DataFormatType.GEOJSON, ".geojson", "GeoJSON|*.geojson",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp").ToToolInfo());

        tools.Add(new FormatConversionTool("shp-to-kml", "SHP → KML", "SHP → KML",
            "Convert Shapefile to KML format", "将 Shapefile 转换为 KML 格式",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.KML, ".kml", "KML|*.kml").ToToolInfo());

        tools.Add(new FormatConversionTool("kml-to-shp", "KML → SHP", "KML → SHP",
            "Convert KML to Shapefile format", "将 KML 转换为 Shapefile 格式",
            DataFormatType.KML, ".kml", "KML|*.kml",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp").ToToolInfo());

        tools.Add(new FormatConversionTool("shp-to-gpkg", "SHP → GeoPackage", "SHP → GeoPackage",
            "Convert Shapefile to GeoPackage format", "将 Shapefile 转换为 GeoPackage 格式",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.GEOPACKAGE, ".gpkg", "GeoPackage|*.gpkg").ToToolInfo());

        tools.Add(new FormatConversionTool("gpkg-to-shp", "GeoPackage → SHP", "GeoPackage → SHP",
            "Convert GeoPackage to Shapefile format", "将 GeoPackage 转换为 Shapefile 格式",
            DataFormatType.GEOPACKAGE, ".gpkg", "GeoPackage|*.gpkg",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp").ToToolInfo());

        tools.Add(new FormatConversionTool("shp-to-dxf", "SHP → DXF", "SHP → DXF",
            "Convert Shapefile to DXF format", "将 Shapefile 转换为 DXF 格式",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.DXF, ".dxf", "DXF|*.dxf").ToToolInfo());

        tools.Add(new FormatConversionTool("dxf-to-shp", "DXF → SHP", "DXF → SHP",
            "Convert DXF to Shapefile format", "将 DXF 转换为 Shapefile 格式",
            DataFormatType.DXF, ".dxf", "DXF|*.dxf",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp").ToToolInfo());

        tools.Add(new FormatConversionTool("geojson-to-kml", "GeoJSON → KML", "GeoJSON → KML",
            "Convert GeoJSON to KML format", "将 GeoJSON 转换为 KML 格式",
            DataFormatType.GEOJSON, ".geojson", "GeoJSON|*.geojson",
            DataFormatType.KML, ".kml", "KML|*.kml").ToToolInfo());

        tools.Add(new FormatConversionTool("geojson-to-gpkg", "GeoJSON → GeoPackage", "GeoJSON → GeoPackage",
            "Convert GeoJSON to GeoPackage format", "将 GeoJSON 转换为 GeoPackage 格式",
            DataFormatType.GEOJSON, ".geojson", "GeoJSON|*.geojson",
            DataFormatType.GEOPACKAGE, ".gpkg", "GeoPackage|*.gpkg").ToToolInfo());

        tools.Add(new FormatConversionTool("filegdb-to-shp", "FileGDB → SHP", "FileGDB → SHP",
            "Convert FileGDB to Shapefile format", "将 FileGDB 转换为 Shapefile 格式",
            DataFormatType.FILEGDB, ".gdb", "FileGDB|*.gdb",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp").ToToolInfo());

        tools.Add(new FormatConversionTool("shp-to-filegdb", "SHP → FileGDB", "SHP → FileGDB",
            "Convert Shapefile to FileGDB format", "将 Shapefile 转换为 FileGDB 格式",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.FILEGDB, ".gdb", "FileGDB|*.gdb").ToToolInfo());

        // ── Other Conversion tools ──
        tools.Add(new CsvToVectorTool().ToToolInfo());
        tools.Add(new PostgisImportTool().ToToolInfo());
        tools.Add(new PostgisExportTool().ToToolInfo());

        // ── Geometry tools ──
        tools.Add(new BufferTool().ToToolInfo());
        tools.Add(new UnionTool().ToToolInfo());
        tools.Add(new IntersectionTool().ToToolInfo());
        tools.Add(new DifferenceTool().ToToolInfo());
        tools.Add(new ConvexHullTool().ToToolInfo());
        tools.Add(new CentroidTool().ToToolInfo());
        tools.Add(new SimplifyTool().ToToolInfo());
        tools.Add(new FixGeometriesTool().ToToolInfo());
        tools.Add(new MergeLayersTool().ToToolInfo());
        tools.Add(new SplitLayerTool().ToToolInfo());
        tools.Add(new ClipTool().ToToolInfo());
        tools.Add(new SpatialJoinTool().ToToolInfo());
        tools.Add(new CentralLinesTool().ToToolInfo());

        // ── Validation tools ──
        tools.Add(new CheckGeometryTool().ToToolInfo());

        // ── Coordinate tools ──
        tools.Add(new ReprojectTool().ToToolInfo());
        tools.Add(new BatchReprojectTool().ToToolInfo());

        // ── Analysis tools ──
        tools.Add(new CalculateAreaTool().ToToolInfo());
        tools.Add(new CalculateLengthTool().ToToolInfo());
        tools.Add(new SpatialFilterTool().ToToolInfo());
        tools.Add(new AttributeQueryTool().ToToolInfo());

        // ── Utility tools ──
        tools.Add(new ZipCompressTool().ToToolInfo());
        tools.Add(new ZipExtractTool().ToToolInfo());

        // ── Raster tools ──
        tools.Add(new RasterFormatConvertTool().ToToolInfo());
        tools.Add(new RasterCalculatorTool().ToToolInfo());

        // ── Remote Sensing tools ──
        tools.Add(new SatelliteDownloadTool().ToToolInfo());

        // ── GPS tools ──
        tools.Add(new GpxProcessingTool().ToToolInfo());

        // ── Geocoding tools ──
        tools.Add(new GeocodeAddressesTool().ToToolInfo());

        return tools;
    }
}
