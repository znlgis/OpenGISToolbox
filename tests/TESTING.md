# OpenGISToolbox 测试

真实数据驱动的端到端测试：直接调用各 GIS 工具（绕过 Avalonia UI），用真实矢量/栅格/GPX/ZIP/PostGIS 数据做输入，并**从数据文件自身独立推导期望值**（不经 GDAL）做交叉校验。

## 工程结构

| 工程 | 作用 |
|------|------|
| `tests/OpenGISToolbox.TestKit` | 共用库：GDAL 运行时卫生、无 UI 调用工具、SHp/DBF/GeoJSON 二进制解析器、真实数据目录解析器、PostGIS 探测（Npgsql）、几何/属性比对、合成栅格/GPX/CSV 生成 |
| `tests/OpenGISToolbox.Tests` | xUnit 测试类（逐类分进程运行） |
| `tools/OpenGISToolbox.RealDataHarness` | 控制台 harness：全工具遍历真实数据，输出 `Pass/Warn/Fail`，有 Fail 时退出码 1（接 CI） |

## 运行方式

本机装有系统级 GDAL（OSGeo4W）时，全量单进程运行会因 GDAL 原生库跨 testhost 污染而崩溃（0xC0000005），因此**按测试类分进程运行**。`run-tests.sh` 还会从 `PATH` 过滤掉 OSGeo4W，确保只用自带的 MaxRev 运行时：

```bash
# 仅 xUnit 套件（逐类独立进程）
bash run-tests.sh

# xUnit 套件 + 控制台真实数据 harness（任一 Fail 则退出码 1）
bash run-tests.sh --with-harness

# 单独跑某一类
dotnet test tests/OpenGISToolbox.Tests/OpenGISToolbox.Tests.csproj \
  --filter "FullyQualifiedName~RealDataPipelineTests"
```

## 环境变量（数据无关，全部可选）

测试资产不内置任何数据集名称/路径/期望值；数据目录与连接均经环境变量注入：

| 变量 | 含义 |
|------|------|
| `OGT_REAL_DATA_DIR` | 真实 `.shp/.dbf` 目录（含子目录递归发现成对文件）；设置后作为唯一数据源，期望值从文件头独立解析交叉校验 |
| `OGT_REAL_DATA_CACHE` | Natural Earth 1:50m 锚数据集缓存目录（缺省 `%LOCALAPPDATA%\OpenGISToolboxTests\natural-earth-50m`）；未设 `OGT_REAL_DATA_DIR` 且无缓存时自动下载 |
| `OGT_TEST_PG_HOST/PORT/DB/USER/PASSWORD` | PostGIS 端点（缺省 `127.0.0.1:5432 postgres/postgres/postgres`）；探测不到可连的 PG 时 PostGIS 用例自动跳过 |
| `OGT_SKIP_NETWORK=1` | 跳过在线地理编码/瓦片下载用例 |

无网络、无 `OGT_REAL_DATA_DIR`、无缓存时，真实数据用例以「提前 return」方式跳过（xUnit v2 无公开的运行期 skip API），断言只在数据确实存在时执行。

## 测试类与覆盖范围

| 类 | 覆盖 |
|----|------|
| `SmokeTests` | 工具注册完整性、ID 唯一性、缺参/坏文件/非法参数优雅失败 |
| `ConversionTests` | Shp↔GeoJSON↔GPKG↔KML 往返、CSV→点、单点/往返投影、批量投影、属性查询 |
| `GeometryToolTests` | Buffer（UTM 面积断言）、Union/Difference/Intersection、Simplify、Centroid、ConvexHull、Merge、Split、Clip、SpatialFilter、SpatialJoin、几何校验/修复、面积/长度 |
| `RasterTests` | GeoTIFF 生成、栅格计算器（Threshold/Scale/Offset/NDVI 数值断言）、栅格格式转换 |
| `MiscToolTests` | GPX 提取航点/轨迹/摘要、ZIP 压缩解压往返 |
| `RealDataTests` | Natural Earth 真实国家边界：转换→属性查询→重投影→缓冲区→合并 全链路 |
| `RealDataPipelineTests` | **真实数据文件头三方交叉校验（.shp 链 == .dbf 头 == GDAL == 源 GeoJSON）**、SHP→各格式要素数保真、GeoJSON 独立解析、属性查询精确期望、重投影往返坐标指纹、空间过滤满/空、Split 分片求和、Merge 翻倍、面积报告 |
| `DxfFilegdbToolTests` | **此前无覆盖的工具**：SHP→DXF（含属性+多部件拆分）、真实国家层→DXF、FileGDB 往返（驱动能力探测）、CentralLines、SpatialJoin 全目标保留 |
| `PostgisRoundTripTests` | **PostGIS 往返**：导出→SQL 侧计数/FID 集合校验→导回→文件头计数+几何指纹；缺 PG 自动跳过 |
| `NetworkToolTests` | **联网工具**：卫星瓦片下载并校验 PNG/JPEG 魔数、Nominatim 地理编码坐标解析、空输入优雅失败；`OGT_SKIP_NETWORK=1` 跳过 |

合成数据由 `TestEnv`/`Synth` 在运行时生成（临时目录，已知数值），真实数据由 `RealDataCatalog` 解析（`OGT_REAL_DATA_DIR` 或 Natural Earth 缓存），均无需人工准备。

## 测试中发现并已处置的缺陷

### 依赖层（引擎）—— 通过升级 `OpenGIS.Utils` 1.0.1 → 1.0.8 修复
1. **DXF 写入把属性字段按序数映射到固定 schema 的错误内建列**，导致 SHP→DXF 全部要素写入失败。引擎已在 v1.0.5+ 修复固定 schema 驱动的字段创建跳过逻辑；旧版 `fieldIndexMap` 又按序数赋值造成属性错位。升级到 1.0.8 解决。
2. **FileGDB 驱动探测**：1.0.1 的写入探测在装有旧 FileGDB 映射时误判；1.0.5+ 优先用 `OpenFileGDB` 创建，往返可用。

### 应用层（本仓库，已在源码修复并由回归覆盖）
3. **必填参数缺省值未生效（headless 场景）**：`ToolBase.GetRequired/GetRequiredDouble/GetRequiredInt` 改为在缺参时回退到参数声明的 `DefaultValue`（UI 本就预填），使 CLI/harness/API 与 UI 行为一致；无缺省值的必填参数仍严格报错。
4. **含 NullShape 记录的真实图层导致整层写入失败**：GDAL 写后端把每个空几何要素计为失败并在末尾抛异常，一条 null 记录即让本可成功的转换整体失败。新增 `ToolBase.WriteLayerSafe`：写文件向量图层前先剔除无几何要素并经进度上报（这些要素本就无法经 WKT 管线往返）。
5. **SHP→PostGIS 多重几何列类型冲突**：Shapefile 面图层声明为单部件 `POLYGON`，但 OGR 读出的分片要素是 `MULTIPOLYGON`，PostGIS 建的 `Polygon` 列在 COPY 时以「Geometry type does not match」拒绝、零行入库。`PostgisExportTool.PrepareLayerForPostgis` 将列类型升为 MULTI 变体并同步包裹要素 WKT。
6. **SHP→PostGIS 数值字段溢出**：DBF 派生的 DOUBLE 字段带宽度/精度（如 `24,15`），映射成 `numeric(24,15)`（整数位仅 9 位），普通人口计数 `1.3e9` 即「numeric field overflow」致 COPY 中断。`PrepareLayerForPostgis` 将 DOUBLE/FLOAT 字段**保型扩位**（整数位补足到 ≥15 位，`numeric(30,15)`）。注：曾用「清空宽高→float8」方案，但在渠道真实数据上实测 float8 列会触发 GDAL PG 驱动 COPY 中途失败回退逐行插入、服务端行序重排（FID 保真 68 处乱序），故改回保留 numeric 类型。
7. **SHP→DXF 多部件几何无法存储**：DXF 实体 schema 不支持多重几何，世界国家面图层全部写入失败。`FormatConversionTool.ExplodeForDxf` 将多重几何拆为单部件要素（几何与 FID 保留），并**不携带属性**（DXF 固定 schema 下属性映射会错位，见上第 1 条）。注意：仅拆 `MULTI*` 容器，不迭代单一 `POLYGON` 的内环（内环是 `LINESTRING`，会被 DXF 面图层拒绝——该坑由单测 `ShpToDxf_WithAttributesAndMultipart...` 以双部件 `POLYGON` 覆盖）。

### 引擎层根因修复状态（opengis-utils-for-net 已修，待发版 ≥1.0.9）

以下缺陷根因在引擎，已在 `opengis-utils-for-net` 仓库修复并有回归测试（266 xunit + 真实数据 harness 284 项 0 Fail）；本仓库的应用层 workaround 为兼容 NuGet 1.0.8 而保留，引擎升版发布并升级依赖后可精简：

| 引擎修复 | 对应本仓库 workaround |
|---|---|
| 空几何要素改为跳过+告警，不再整层抛异常 | `ToolBase.WriteLayerSafe` |
| 固定 schema 驱动（DXF）字段索引按实际创建序列映射，属性不再污染内建列 | `ExplodeForDxf` 剥离属性 |
| PostgreSQL 列类型自动 MULTI 提升 + 单部件 WKT 包裹 | `PrepareLayerForPostgis` 升维包裹 |
| KML 的 DATE/DATETIME 列降级 ISO 文本（GDAL 3.13 原生 KML writer 对 OFTDate 全要素失败）| 暂无——含日期图层转 KML 在 1.0.8 下仍整层失败，属已知限制 |

### 已知限制（未改，已在测试中标注/绕过）
- **KML 属性名不保真（1.0.8 行为）**：GDAL KML 驱动无通用属性袋，写侧把 schema 字段映射进 `<name>`/`<description>`，读回后源字段 `label` 的值落在 `description` 字段（值不丢、键名变）。`ConversionTests` 对 KML 断言"值保真"而非"字段名保真"；GeoJSON/GPKG 仍严格校验字段名。注：1.0.1 时代 ExtendedData 曾保留原键名，升级 1.0.8 后为 GDAL 新版驱动行为。
- **含 DATE/DATETIME 字段的图层转 KML 整层失败（NuGet 1.0.8）**：GDAL 3.13 原生 KML writer 对 OFTDate 列逐要素 CreateFeature 报「Export of geometry to KML failed」。引擎侧已修复（日期降级 ISO 文本，见上表），待发布后升级依赖即解除。
- **GeoJSON 输出不保留 EPSG**：`ReprojectTool` 坐标转换正确，但 GeoJSON 写出不带 CRS（RFC 7946 强制 WGS84，且 `OpenGIS.Utils` 不暴露该选项）。需保 CRS 请用 GeoPackage/Shapefile，或改写带 `crs` 成员的 GeoJSON。
- **BatchReproject 的 `format` 同时决定输入匹配后缀**：传 `GeoJSON` 只处理 `*.geojson`。语义如此。
- **UTM/带投影的目标 CRS 对全球范围数据会「full reprojection failed」**：如国家层转 EPSG:32650 超范围；harness 批投影改用 EPSG:3857（全球可表示）。这是数据/CRS 匹配问题，非工具缺陷。
- **全量单进程合跑崩溃**：GDAL 原生库跨 testhost 污染，用 `run-tests.sh` 逐类分进程规避。

## 控制台 harness 覆盖分区

`tools/OpenGISToolbox.RealDataHarness` 对 ToolRegistry 全部注册工具做数据驱动遍历（当前约 185 项，0 Fail / 合理 Warn）：

1. 真实数据锚点完整性（三方计数 + 类型一致）
2. 格式转换遍历（GeoJSON/GPKG/KML/DXF/FileGDB 往返）
3. 几何处理遍历（Buffer/Centroid/ConvexHull/Simplify/Fix/Check/Merge/Split/Clip/Intersection/Difference/SpatialJoin/CentralLines）
4. 分析遍历（SpatialFilter 满/空、AttributeQuery 精确期望、面积/长度报告）
5. 坐标变换遍历（4326↔3857 往返指纹、4326→4490 近恒等、批量投影）
6. PostGIS 往返（SQL 侧计数/FID + 文件头/几何指纹，覆盖坑①②⑥）
7. 栅格/GPS/CSV/ZIP（合成数据，数值精确断言）
8. 联网工具（Nominatim、OSM 瓦片；`OGT_SKIP_NETWORK=1` 可跳过）
9. 异常/健壮性矩阵（所有注册工具的空参、坏文件、非法 EPSG/数字/SQL 优雅失败）

> 注：harness 与 xUnit 共用 `OpenGISToolbox.TestKit` 的解析与比对逻辑，二者期望值来源一致（文件头/Npgsql 独立于被测 GDAL），互为冗余校验。
