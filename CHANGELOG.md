# Changelog 变更日志

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.1.0] - 2026-09-24

本轮按四个 Sprint 系统性扩充地理处理能力，工具数 42 → 62，全部纳入真实数据驱动的测试体系（合成数据精确断言 + 真实数据独立推导交叉校验 + 控制台 harness）。

### Added 新增

**Sprint 1 · 矢量几何高频补全**
- Dissolve 按属性融合
- Symmetric Difference 对称差
- Multipart ↔ Singlepart 互转（多部件↔单部件）
- Extract Vertices 提取顶点
- Polygon to Line 面转线、Line to Polygon 线转面
- Batch Format Conversion 目录批量格式转换

**Sprint 2 · 空间分析深化**
- Extract by Location 按位置提取（Intersects/Contains/Within/Disjoint）
- Nearest Neighbor 最近邻连接（GEOS 最小距离 + 外包框下界剪枝）
- Count Points in Polygon 点在多边形计数
- Create Grid 创建渔网网格

**Sprint 3 · 栅格专题**
- DEM Terrain Analysis 地形分析（坡度 / 坡向 / 山体阴影）
- Contour Extraction 等高线提取
- Raster Reproject / Clip / Mosaic 栅格重投影 / 裁剪 / 镶嵌
- Zonal Statistics 分区统计（逐像元托管实现）

**Sprint 4 · 高级几何与工程**
- Voronoi Diagram 泰森多边形（裁剪至范围、精确铺满，托管实现）
- Delaunay Triangulation Delaunay 三角网（Bowyer–Watson 托管实现）
- 持续集成：GitHub Actions（`Tests` 工作流，windows-latest 逐类运行 xUnit 套件）
- 发布：`Release` 工作流（按 tag 构建 Windows / Linux / macOS 自包含免安装包并附于 GitHub Release）

### Fixed 修复
- 栅格 DEM 百分比坡度：该 GDAL 绑定的 `gdaldem slope` 拒绝 `-s`/`--format` 参数，改由坡度结果 `tan(°)·100` 后处理得到，避免整工具失败。
- 分区统计像元定位：geotransform 的 Y 像元尺寸取自 `gt[5]`（此前误用旋转分量 `gt[4]`），修正后像元中心定位正确。
- `Layer.GetFeatureCount` 需带 `force` 参数的绑定差异修正。

### Engineering 工程
- 测试基线：xUnit 由 61 增至 90（新增 Sprint1–4 四个测试类），控制台真实数据 harness 增至 244 项检查（0 失败 0 告警）。
- 栅格工具统一经 `RasterGdal` 懒初始化 MaxRev 运行时，零系统 GDAL 依赖。

## [1.0.0] - 2025

### Added
- 初始版本：42 个工具，10 大类（格式转换、几何处理、几何验证、坐标转换、空间分析、栅格、遥感、GPS、地理编码、实用工具）。
- 基于 .NET 10 + Avalonia UI 的跨平台桌面工具箱，MVVM 架构，中英文双语界面。
- 以 OpenGIS.Utils（GDAL 内核）为处理引擎。
