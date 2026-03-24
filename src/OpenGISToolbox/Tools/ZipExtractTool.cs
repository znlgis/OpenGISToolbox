using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.Utils;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Extracts a ZIP archive to a folder.
/// </summary>
public class ZipExtractTool : ToolBase
{
    public override string Id => "zip-extract";
    public override string Name => "ZIP Extract";
    public override string NameZh => "ZIP 解压";
    public override string Description => "Extract a ZIP archive to a folder";
    public override string DescriptionZh => "将 ZIP 归档解压到文件夹";
    public override ToolCategory Category => ToolCategory.Utility;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "input",
                Label = "Input File",
                LabelZh = "输入文件",
                Description = L("ZIP archive to extract", "要解压的 ZIP 归档"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = "ZIP Archive|*.zip"
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output Folder",
                LabelZh = "输出文件夹",
                Description = L("Destination folder for extraction", "解压目标文件夹"),
                Type = ParameterType.FolderPath,
                Required = true
            }
        };
    }

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var outputFolder = GetRequired(parameters, "output");

        progress?.Report(L("Extracting ZIP archive...", "正在解压 ZIP 归档..."));
        await Task.Run(() => ZipUtil.Unzip(inputPath, outputFolder), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"ZIP extraction completed. Files extracted to: {outputFolder}",
                $"ZIP 解压完成。文件已解压到: {outputFolder}"),
            OutputPath = outputFolder
        };
    }
}
