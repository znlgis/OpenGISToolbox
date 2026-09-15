# OpenGISToolbox 测试

真实数据驱动的端到端测试，直接调用各 GIS 工具类（绕过 Avalonia UI），用真实矢量/栅格/GPX/ZIP 文件做输入并校验输出内容正确性。

## 运行方式

本项目在装有系统级 GDAL（OSGeo4W）的机器上，全量单进程运行会因 GDAL 原生库跨 testhost 污染而崩溃。因此**按测试类分进程运行**：

```bash
# 一键运行全部（逐类独立进程，聚合结果）
bash run-tests.sh

# 或单独跑某一类
dotnet test tests/OpenGISToolbox.Tests/OpenGISToolbox.Tests.csproj \
  --filter "FullyQualifiedName~ConversionTests"
```

> 若本机未安装 OSGeo4W，可去掉 `run-tests.sh` 中对 `PATH` 的过滤直接 `dotnet test`。

## 测试类与覆盖范围

| 类 | 覆盖 |
|----|------|
| `SmokeTests` | 工具注册完整性、ID 唯一性、缺参/坏文件/非法参数优雅失败 |
| `ConversionTests` | Shp↔GeoJSON↔GPKG↔KML 往返、CSV→点、单点/往返投影、批量投影、属性查询 |
| `GeometryToolTests` | Buffer（UTM 面积断言）、Union/Difference/Intersection（面积断言）、Simplify、Centroid、ConvexHull、Merge、Split、Clip、SpatialFilter、SpatialJoin、几何校验/修复、面积/长度 |
| `RasterTests` | GeoTIFF 生成、栅格计算器（Threshold/Scale/Offset/NDVI 数值断言）、栅格格式转换 |
| `MiscToolTests` | GPX 提取航点/轨迹/摘要、ZIP 压缩解压往返 |
| `RealDataTests` | 下载 Natural Earth 真实国家边界数据跑 转换→属性查询→重投影→缓冲区→合并 完整链路 |

测试数据由 `TestEnv` 在运行时自动生成（临时目录），无需人工准备。

## 测试中发现的问题（环境相关）

1. **系统 GDAL 冲突 —— 已修复（应用层）**：机器装有 OSGeo4W 时，其 `GDAL_DRIVER_PATH`/`GDAL_DATA`/`PROJ_LIB` 及 PATH 中的 `gdal.dll` 会与 NuGet 自带的 MaxRev GDAL 运行时冲突，导致 GDAL 初始化崩溃。修复（`src/OpenGISToolbox`）：
   - `OpenGISToolbox.csproj` 在 Windows 下指定 `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`，把自带 `gdal.dll`/`proj_9.dll`/`proj.db` 复制进输出目录，从根上避免从 PATH 加载系统 GDAL；
   - `Program.cs` 在 `Main` 最开头清空 `GDAL_DRIVER_PATH`/`GDAL_DATA`/`PROJ_LIB`/`PROJ_DATA`，防止系统变量把 GDAL 指向版本不兼容的插件/数据目录。

   测试侧（`TestEnv`/`run-tests.sh`）也做同样清理，保证 CI 与本地一致。

2. **GeoJSON 输出不保留 EPSG —— 第三方库+规范限制，暂无法在应用层修复**：`ReprojectTool` 会把坐标正确转换，但写出的 GeoJSON 不携带 CRS 信息（读取后 `layer.Wkid` 为 null）。根因：`OpenGIS.Utils` 的 GeoJSON 写出不暴露 CRS 选项，且 GeoJSON（RFC 7946）规范强制 WGS84、不推荐 CRS 字段。如需强制保留 CRS，需改写成带 `crs` 成员的 GeoJSON 或改用 GeoPackage/Shapefile 输出。

3. **BatchReproject 的 `format` 同时决定输入文件匹配后缀 —— 设计行为**：传 `GeoJSON` 只会处理文件夹下的 `*.geojson`，忽略 `.shp`。语义如此，使用时注意即可。

4. **全量单进程合跑崩溃 —— 测试基础设施问题，已规避**：6 个测试类共用一个 testhost 时触发 GDAL 原生访问冲突（0xC0000005），根因是 vstest 的 testhost 不在应用输出目录、仍从 PATH 解析 GDAL 原生库。逐类独立进程则全部通过，已用 `run-tests.sh` 规避。
