# OpenGIS Toolbox

[English](#english) | [中文](#中文)

---

## English

### Overview

**OpenGIS Toolbox** is an open-source desktop GIS toolbox application built with **.NET 10** and **Avalonia UI**, inspired by [NextGIS Toolbox](https://toolbox.nextgis.com/). It provides a set of geoprocessing tools for vector data format conversion, geometry processing, coordinate transformation, spatial analysis, and more.

The application uses [OpenGIS.Utils](https://github.com/znlgis/opengis-utils-for-net) (via NuGet) as its core GIS processing engine, which is powered by GDAL/OGR.

### Features

- 🖥️ **Cross-Platform Desktop App**: Runs on Windows, Linux, and macOS via Avalonia UI
- 🔄 **Format Conversion**: Convert between Shapefile, GeoJSON, KML, GeoPackage, DXF, FileGDB, CSV, and PostGIS formats
- 📐 **Geometry Processing**: Buffer, Union, Intersection, Difference, Convex Hull, Centroid, Simplify, Fix Geometries, Merge, Split, Clip, and Spatial Join operations
- ✅ **Geometry Validation**: Check geometry validity with detailed error reporting
- 🌐 **Coordinate Transformation**: Reproject coordinates between different coordinate reference systems, with batch reprojection support
- 📏 **Spatial Analysis**: Calculate area and length, Spatial Filter, and Attribute Query
- 📦 **Utility Tools**: ZIP compression and extraction
- 🎯 **MVVM Architecture**: Clean separation of concerns using CommunityToolkit.Mvvm

### Tech Stack

| Component | Technology |
|-----------|-----------|
| Framework | .NET 10 |
| UI Framework | Avalonia UI 11.3 |
| MVVM Toolkit | CommunityToolkit.Mvvm 8.2 |
| GIS Engine | [OpenGIS.Utils](https://www.nuget.org/packages/OpenGIS.Utils) 1.0.0 (GDAL-based) |
| Theme | Fluent Theme |

### Getting Started

#### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later

#### Build and Run

```bash
# Clone the repository
git clone https://github.com/znlgis/OpenGISToolbox.git
cd OpenGISToolbox

# Build
dotnet build

# Run
dotnet run --project src/OpenGISToolbox
```

### Project Structure

```
OpenGISToolbox/
├── OpenGISToolbox.sln                    # Solution file
├── src/
│   └── OpenGISToolbox/
│       ├── OpenGISToolbox.csproj         # Project file (.NET 10 + Avalonia)
│       ├── App.axaml / App.axaml.cs      # Application entry
│       ├── Program.cs                    # Main entry point
│       ├── ViewLocator.cs               # View model to view mapper
│       ├── Models/
│       │   ├── ToolCategory.cs           # Tool category enum
│       │   ├── ToolInfo.cs               # Tool metadata and execution delegate
│       │   ├── ToolParameter.cs          # Parameter type definitions
│       │   └── ToolResult.cs             # Execution result model
│       ├── Services/
│       │   └── ToolRegistry.cs           # Tool registration and definitions
│       ├── ViewModels/
│       │   ├── ViewModelBase.cs          # Base ViewModel (ObservableObject)
│       │   ├── MainWindowViewModel.cs    # Main window ViewModel
│       │   ├── ToolExecutionViewModel.cs # Tool execution panel ViewModel
│       │   └── ToolParameterViewModel.cs # Parameter input ViewModel
│       └── Views/
│           ├── MainWindow.axaml          # Main window (3-panel layout)
│           └── ToolExecutionView.axaml   # Tool execution panel
└── README.md
```

### Implementation Status

The following table shows the mapping between NextGIS Toolbox features and the current implementation status.

#### ✅ Implemented Tools (36 tools)

| # | Category | Tool | NextGIS Equivalent | Status |
|---|----------|------|--------------------|--------|
| 1 | Conversion | SHP → GeoJSON | Vector format conversion | ✅ Done |
| 2 | Conversion | GeoJSON → SHP | Vector format conversion | ✅ Done |
| 3 | Conversion | SHP → KML | KML to geodata / reverse | ✅ Done |
| 4 | Conversion | KML → SHP | KML to geodata | ✅ Done |
| 5 | Conversion | SHP → GeoPackage | Vector format conversion | ✅ Done |
| 6 | Conversion | GeoPackage → SHP | Vector format conversion | ✅ Done |
| 7 | Conversion | SHP → DXF | DWG/DXF conversion | ✅ Done |
| 8 | Conversion | DXF → SHP | DWG/DXF conversion | ✅ Done |
| 9 | Conversion | GeoJSON → KML | Vector format conversion | ✅ Done |
| 10 | Conversion | GeoJSON → GeoPackage | Vector format conversion | ✅ Done |
| 11 | Geometry | Buffer | Buffer / Geometry processing | ✅ Done |
| 12 | Geometry | Union | Merge layers / Union | ✅ Done |
| 13 | Geometry | Intersection | Calculate areas of intersection | ✅ Done |
| 14 | Geometry | Difference | Geometry processing | ✅ Done |
| 15 | Geometry | Convex Hull | Geometry processing | ✅ Done |
| 16 | Geometry | Centroid | Points inside polygons | ✅ Done |
| 17 | Geometry | Simplify | Basic/Advanced vector generalization | ✅ Done |
| 18 | Validation | Check Geometry | Check and fix vector geometries | ✅ Done |
| 19 | Coordinate | Reproject | Reproject coordinates | ✅ Done |
| 20 | Analysis | Calculate Area | Spatial analysis | ✅ Done |
| 21 | Analysis | Calculate Length | Spatial analysis | ✅ Done |
| 22 | Utility | ZIP Compress | Data packaging | ✅ Done |
| 23 | Utility | ZIP Extract | Data extraction | ✅ Done |
| 24 | Conversion | FileGDB → SHP | Vector format conversion | ✅ Done |
| 25 | Conversion | SHP → FileGDB | Vector format conversion | ✅ Done |
| 26 | Conversion | CSV → Vector | Table to vector | ✅ Done |
| 27 | Conversion | PostGIS Import | Database operations | ✅ Done |
| 28 | Conversion | PostGIS Export | Database operations | ✅ Done |
| 29 | Geometry | Fix Geometries | Check and fix vector geometries | ✅ Done |
| 30 | Geometry | Merge Layers | Merge vector layers | ✅ Done |
| 31 | Geometry | Split Layer | Split vector layers | ✅ Done |
| 32 | Geometry | Clip | Clip layer by polygon | ✅ Done |
| 33 | Geometry | Spatial Join | Join by location | ✅ Done |
| 34 | Coordinate | Batch Reproject | Reproject coordinates (batch) | ✅ Done |
| 35 | Analysis | Spatial Filter | Filter by spatial extent | ✅ Done |
| 36 | Analysis | Attribute Query | Filter by attributes | ✅ Done |

#### 🔲 Planned / Not Yet Implemented

The following NextGIS Toolbox features are planned for future implementation:

| Category | Planned Tool | NextGIS Equivalent | Priority |
|----------|-------------|-------------------|----------|
| Geometry | Central Lines | Central lines of polygons | Low |
| Raster | Raster Format Conversion | Raster operations | Medium |
| Raster | Raster Calculator | GRASS/GDAL raster calculator | Low |
| Remote Sensing | Satellite Image Download | Sentinel-2/Landsat | Low |
| GPS | GPX Processing | Clip/merge/split GPX files | Low |
| Geocoding | Geocode Addresses | Geocoding service | Low |

### How It Works

1. **Tool Categories** (left panel): Browse tools by category - Conversion, Geometry, Validation, Coordinate, Analysis, Utility
2. **Tool List** (middle panel): Select a specific tool from the filtered list
3. **Execution Panel** (right panel): Configure parameters, execute the tool, and view results

Each tool execution:
- Validates required parameters before running
- Reports progress via the log panel
- Shows success/failure result with duration

### Contributing

Contributions are welcome! To add a new tool:

1. Define the tool in `Services/ToolRegistry.cs` with parameters and execution logic
2. The UI will automatically display it in the correct category
3. No view changes needed for standard parameter types

### License

[MIT](LICENSE)

---

## 中文

### 概述

**OpenGIS Toolbox** 是一个基于 **.NET 10** 和 **Avalonia UI** 构建的开源桌面GIS工具箱应用，灵感来自 [NextGIS Toolbox](https://toolbox.nextgis.com/)。它提供了一组地理处理工具，用于矢量数据格式转换、几何处理、坐标转换、空间分析等功能。

应用使用 [OpenGIS.Utils](https://github.com/znlgis/opengis-utils-for-net)（通过 NuGet 引用）作为核心GIS处理引擎，底层基于 GDAL/OGR。

### 特性

- 🖥️ **跨平台桌面应用**：通过 Avalonia UI 在 Windows、Linux 和 macOS 上运行
- 🔄 **格式转换**：在 Shapefile、GeoJSON、KML、GeoPackage、DXF、FileGDB、CSV 和 PostGIS 格式之间转换
- 📐 **几何处理**：缓冲区、合并、交集、差集、凸包、质心、简化、修复几何、合并图层、拆分图层、裁剪和空间连接操作
- ✅ **几何验证**：检查几何有效性并提供详细错误报告
- 🌐 **坐标转换**：在不同坐标参考系之间重投影坐标，支持批量重投影
- 📏 **空间分析**：计算几何对象的面积和长度、空间过滤和属性查询
- 📦 **实用工具**：ZIP 压缩和解压
- 🎯 **MVVM 架构**：使用 CommunityToolkit.Mvvm 实现清晰的关注点分离

### 技术栈

| 组件 | 技术 |
|------|------|
| 框架 | .NET 10 |
| UI 框架 | Avalonia UI 11.3 |
| MVVM 工具包 | CommunityToolkit.Mvvm 8.2 |
| GIS 引擎 | [OpenGIS.Utils](https://www.nuget.org/packages/OpenGIS.Utils) 1.0.0（基于 GDAL）|
| 主题 | Fluent 主题 |

### 快速开始

#### 前置条件

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本

#### 构建和运行

```bash
# 克隆仓库
git clone https://github.com/znlgis/OpenGISToolbox.git
cd OpenGISToolbox

# 构建
dotnet build

# 运行
dotnet run --project src/OpenGISToolbox
```

### 项目结构

```
OpenGISToolbox/
├── OpenGISToolbox.sln                    # 解决方案文件
├── src/
│   └── OpenGISToolbox/
│       ├── OpenGISToolbox.csproj         # 项目文件（.NET 10 + Avalonia）
│       ├── App.axaml / App.axaml.cs      # 应用入口
│       ├── Program.cs                    # 主入口点
│       ├── ViewLocator.cs               # ViewModel 到 View 的映射
│       ├── Models/
│       │   ├── ToolCategory.cs           # 工具类别枚举
│       │   ├── ToolInfo.cs               # 工具元数据和执行委托
│       │   ├── ToolParameter.cs          # 参数类型定义
│       │   └── ToolResult.cs             # 执行结果模型
│       ├── Services/
│       │   └── ToolRegistry.cs           # 工具注册和定义
│       ├── ViewModels/
│       │   ├── ViewModelBase.cs          # 基础 ViewModel
│       │   ├── MainWindowViewModel.cs    # 主窗口 ViewModel
│       │   ├── ToolExecutionViewModel.cs # 工具执行面板 ViewModel
│       │   └── ToolParameterViewModel.cs # 参数输入 ViewModel
│       └── Views/
│           ├── MainWindow.axaml          # 主窗口（三栏布局）
│           └── ToolExecutionView.axaml   # 工具执行面板
└── README.md
```

### 实现进度

下表展示了 NextGIS Toolbox 功能与当前实现状态的对应关系。

#### ✅ 已实现工具（36个）

| # | 类别 | 工具 | NextGIS 对应功能 | 状态 |
|---|------|------|-----------------|------|
| 1 | 格式转换 | SHP → GeoJSON | 矢量格式转换 | ✅ 已完成 |
| 2 | 格式转换 | GeoJSON → SHP | 矢量格式转换 | ✅ 已完成 |
| 3 | 格式转换 | SHP → KML | KML 与地理数据互转 | ✅ 已完成 |
| 4 | 格式转换 | KML → SHP | KML 转地理数据 | ✅ 已完成 |
| 5 | 格式转换 | SHP → GeoPackage | 矢量格式转换 | ✅ 已完成 |
| 6 | 格式转换 | GeoPackage → SHP | 矢量格式转换 | ✅ 已完成 |
| 7 | 格式转换 | SHP → DXF | DWG/DXF 转换 | ✅ 已完成 |
| 8 | 格式转换 | DXF → SHP | DWG/DXF 转换 | ✅ 已完成 |
| 9 | 格式转换 | GeoJSON → KML | 矢量格式转换 | ✅ 已完成 |
| 10 | 格式转换 | GeoJSON → GeoPackage | 矢量格式转换 | ✅ 已完成 |
| 11 | 几何处理 | 缓冲区 (Buffer) | 缓冲区 / 几何处理 | ✅ 已完成 |
| 12 | 几何处理 | 合并 (Union) | 合并图层 / 联合 | ✅ 已完成 |
| 13 | 几何处理 | 交集 (Intersection) | 计算交集面积 | ✅ 已完成 |
| 14 | 几何处理 | 差集 (Difference) | 几何处理 | ✅ 已完成 |
| 15 | 几何处理 | 凸包 (Convex Hull) | 几何处理 | ✅ 已完成 |
| 16 | 几何处理 | 质心 (Centroid) | 多边形内点 | ✅ 已完成 |
| 17 | 几何处理 | 简化 (Simplify) | 基础/高级矢量概化 | ✅ 已完成 |
| 18 | 几何验证 | 检查几何 | 检查和修复矢量几何 | ✅ 已完成 |
| 19 | 坐标转换 | 重投影 | 坐标重投影 | ✅ 已完成 |
| 20 | 空间分析 | 计算面积 | 空间分析 | ✅ 已完成 |
| 21 | 空间分析 | 计算长度 | 空间分析 | ✅ 已完成 |
| 22 | 实用工具 | ZIP 压缩 | 数据打包 | ✅ 已完成 |
| 23 | 实用工具 | ZIP 解压 | 数据解压 | ✅ 已完成 |
| 24 | 格式转换 | FileGDB → SHP | 矢量格式转换 | ✅ 已完成 |
| 25 | 格式转换 | SHP → FileGDB | 矢量格式转换 | ✅ 已完成 |
| 26 | 格式转换 | CSV → 矢量 | 表格转矢量 | ✅ 已完成 |
| 27 | 格式转换 | PostGIS 导入 | 数据库操作 | ✅ 已完成 |
| 28 | 格式转换 | PostGIS 导出 | 数据库操作 | ✅ 已完成 |
| 29 | 几何处理 | 修复几何 | 检查和修复矢量几何 | ✅ 已完成 |
| 30 | 几何处理 | 合并图层 | 合并矢量图层 | ✅ 已完成 |
| 31 | 几何处理 | 拆分图层 | 拆分矢量图层 | ✅ 已完成 |
| 32 | 几何处理 | 裁剪 | 按多边形裁剪图层 | ✅ 已完成 |
| 33 | 几何处理 | 空间连接 | 按位置连接 | ✅ 已完成 |
| 34 | 坐标转换 | 批量重投影 | 坐标重投影（批量）| ✅ 已完成 |
| 35 | 空间分析 | 空间过滤 | 按空间范围过滤 | ✅ 已完成 |
| 36 | 空间分析 | 属性查询 | 按属性过滤 | ✅ 已完成 |

#### 🔲 计划中 / 尚未实现

以下 NextGIS Toolbox 功能计划在未来实现：

| 类别 | 计划工具 | NextGIS 对应功能 | 优先级 |
|------|---------|-----------------|--------|
| 几何处理 | 中心线 | 多边形中心线 | 低 |
| 栅格 | 栅格格式转换 | 栅格操作 | 中 |
| 栅格 | 栅格计算器 | GRASS/GDAL 栅格计算器 | 低 |
| 遥感 | 卫星影像下载 | Sentinel-2/Landsat | 低 |
| GPS | GPX 处理 | 裁剪/合并/拆分 GPX 文件 | 低 |
| 地理编码 | 地址编码 | 地理编码服务 | 低 |

### 使用方法

1. **工具分类**（左侧面板）：按类别浏览工具 - 格式转换、几何处理、几何验证、坐标转换、空间分析、实用工具
2. **工具列表**（中间面板）：从筛选后的列表中选择具体工具
3. **执行面板**（右侧面板）：配置参数、执行工具并查看结果

每次工具执行：
- 运行前验证必填参数
- 通过日志面板报告执行进度
- 显示成功/失败结果及执行时长

### 如何继续实现

如需继续实现更多工具，请参考以下步骤：

1. 在 `Services/ToolRegistry.cs` 中添加新工具定义，包括参数和执行逻辑
2. UI 会自动将新工具显示在正确的类别下
3. 对于标准参数类型（文件选择、数字输入、下拉选择等），无需修改视图

**添加新工具的模板代码：**

```csharp
tools.Add(new ToolInfo
{
    Id = "my-new-tool",
    Name = "My New Tool",
    Description = "Description of what this tool does",
    Category = ToolCategory.Geometry,  // Choose appropriate category
    Parameters = new List<ToolParameter>
    {
        new() { Name = "input", Label = "Input File", Type = ParameterType.InputFile, FileFilter = "Shapefile|*.shp" },
        new() { Name = "output", Label = "Output File", Type = ParameterType.OutputFile, FileFilter = "Shapefile|*.shp" },
    },
    ExecuteAsync = async (parameters, progress, ct) =>
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Your processing logic here
            progress?.Report("Processing...");
            // ...
            sw.Stop();
            return new ToolResult { Success = true, Message = "Done.", Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ToolResult { Success = false, Message = $"Error: {ex.Message}", Duration = sw.Elapsed };
        }
    }
});
```

### 许可证

[MIT](LICENSE)
