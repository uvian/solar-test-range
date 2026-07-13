# SolarTestRange — AutoCAD 日照测试范围绘制插件

自动生成建筑南北两侧的日照测试扇形范围，适用于 AutoCAD 2020 / 中望CAD 2020。

## 功能

输入建筑轮廓闭合多段线 + 建筑高度，一键绘制：

- 八角射线（±35° 方向，北向 2×H，南向 200m）
- 闭合线 + 偏移线，分图层编组
- 四段圆弧

## 安装（三选一）

### 方式一：Bundle 插件包（推荐，自动加载）

1. 从 [Releases](https://github.com/uvian/solar-test-range/releases) 下载 `SolarTestRange.bundle.zip`
2. 解压得到 `SolarTestRange.bundle` **文件夹**
3. 复制到 `C:\ProgramData\Autodesk\ApplicationPlugins\`
4. 重启 AutoCAD，插件自动加载
5. 输入 **`HUATU`** 运行

### 方式二：LSP 引导（需配置启动组）

1. 下载 `SolarTestRange.dll` 和 `HUATU.lsp`，放到同一文件夹（如 `E:\CAD插件\`）
2. AutoCAD 输 `APPLOAD` → 启动组 → 内容 → 添加 → 选中 `HUATU.lsp`
3. 重启 AutoCAD，自动加载
4. 输入 **`HUATU`** 运行

### 方式三：手动加载（临时使用）

1. 下载 `SolarTestRange.dll`
2. 拖入 AutoCAD 绘图区，或输 `NETLOAD` 选择 `.dll`
3. 输 **`HUATU`** 运行

## 图层结构

| 图层名 | 内容 |
|--------|------|
| `00包围矩形` | 建筑轮廓包围盒（编组） |
| `00二倍楼高范围` | 北向射线·闭合线·偏移线·圆弧（编组） |
| `00二百米范围` | 南向射线·闭合线·偏移线·圆弧（编组） |

所有元素颜色随层，在图层管理器修改颜色即可统一切换。

### 开发

```bash
dotnet restore
dotnet build -c Release
```

NuGet 引用：`AutoCAD.NET` 23.1.0（AutoCAD 2020 API）

## 兼容性

| 平台 | 兼容 |
|------|------|
| AutoCAD 2020 | ✅ 已验证 |
| 中望CAD 2020 | ✅ AutoCAD 兼容模式 |
| AutoCAD 2014 | ❌ 暂不支持（API 差异较大） |
| AutoCAD 2021+ | ✅ 理论上兼容 |
