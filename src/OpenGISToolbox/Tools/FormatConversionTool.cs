using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Parameterized concrete tool that handles all format conversion scenarios.
/// Each instance is configured with a specific source/target format pair.
/// </summary>
public class FormatConversionTool : ToolBase
{
    private readonly string _id;
    private readonly string _name;
    private readonly string _nameZh;
    private readonly string _description;
    private readonly string _descriptionZh;
    private readonly DataFormatType _sourceFormat;
    private readonly string _sourceExt;
    private readonly string _sourceFilter;
    private readonly DataFormatType _targetFormat;
    private readonly string _targetExt;
    private readonly string _targetFilter;

    public FormatConversionTool(
        string id,
        string name,
        string nameZh,
        string description,
        string descriptionZh,
        DataFormatType sourceFormat,
        string sourceExt,
        string sourceFilter,
        DataFormatType targetFormat,
        string targetExt,
        string targetFilter)
    {
        _id = id;
        _name = name;
        _nameZh = nameZh;
        _description = description;
        _descriptionZh = descriptionZh;
        _sourceFormat = sourceFormat;
        _sourceExt = sourceExt;
        _sourceFilter = sourceFilter;
        _targetFormat = targetFormat;
        _targetExt = targetExt;
        _targetFilter = targetFilter;
    }

    public override string Id => _id;
    public override string Name => _name;
    public override string NameZh => _nameZh;
    public override string Description => _description;
    public override string DescriptionZh => _descriptionZh;
    public override ToolCategory Category => ToolCategory.Conversion;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "input",
                Label = "Input File",
                LabelZh = "输入文件",
                Description = L("Source file to convert", "要转换的源文件"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = _sourceFilter
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Destination file path", "目标文件路径"),
                Type = ParameterType.OutputFile,
                Required = true,
                FileFilter = _targetFilter
            }
        };
    }

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var outputPath = GetRequired(parameters, "output");

        progress?.Report(L("Reading input file...", "读取输入文件..."));
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(_sourceFormat, inputPath), ct);

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Writing output...", $"已读取 {layer.GetFeatureCount()} 个要素，正在写入输出..."));
        await Task.Run(() => OguLayerUtil.WriteLayer(_targetFormat, layer, outputPath), ct);

        return new ToolResult
        {
            Success = true,
            Message = L($"Conversion completed. {layer.GetFeatureCount()} features converted.", $"转换完成，共转换 {layer.GetFeatureCount()} 个要素。"),
            OutputPath = outputPath
        };
    }
}
