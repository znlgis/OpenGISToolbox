using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.Utils;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Compresses a folder into a ZIP archive.
/// </summary>
public class ZipCompressTool : ToolBase
{
    public override string Id => "zip-compress";
    public override string Name => "ZIP Compress";
    public override string NameZh => "ZIP 压缩";
    public override string Description => "Compress a folder into a ZIP archive";
    public override string DescriptionZh => "将文件夹压缩为 ZIP 归档";
    public override ToolCategory Category => ToolCategory.Utility;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "input",
                Label = "Input Folder",
                LabelZh = "输入文件夹",
                Description = L("Folder to compress", "要压缩的文件夹"),
                Type = ParameterType.FolderPath,
                Required = true
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Output ZIP archive path", "输出 ZIP 归档路径"),
                Type = ParameterType.OutputFile,
                Required = true,
                FileFilter = "ZIP Archive|*.zip"
            }
        };
    }

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var inputFolder = GetRequired(parameters, "input");
        var outputPath = GetRequired(parameters, "output");

        progress?.Report(L("Compressing folder...", "正在压缩文件夹..."));
        await Task.Run(() => ZipUtil.Zip(inputFolder, outputPath), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"ZIP compression completed. Archive saved to: {outputPath}",
                $"ZIP 压缩完成。归档已保存到: {outputPath}"),
            OutputPath = outputPath
        };
    }
}
