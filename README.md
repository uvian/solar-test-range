# SolarTestRange — AutoCAD 日照测试范围绘制插件

自动生成建筑南北两侧的日照测试扇形范围，适用于 AutoCAD 2020 / 中望CAD 2020。

## 功能

输入建筑轮廓闭合多段线 + 建筑高度，一键绘制：

- 八角射线（±35° 方向，北向 2×H，南向 200m）
- 闭合线 + 偏移线
- 四段圆弧
- 闭合填充边界

## 安装

1. 从 [Releases](https://github.com/uvian/solar-test-range/releases) 下载 `SolarTestRange.dll`
2. 拖入 AutoCAD 绘图区，即可加载
3. 输入命令 **`TESTRANGE`**

> 如需永久加载，输 `APPLOAD` → 内容 → 添加 `.dll` → 勾选"启动时加载"。
> 中望CAD 操作相同。

## 使用

1. 输入 `TESTRANGE` 回车
2. 选择建筑轮廓的闭合多段线
3. 输入建筑高度（米）
4. 自动生成：青色射线 + 黄色闭合线 + 品红圆弧 + 红色填充边界

## 开发

```bash
# 还原
dotnet restore

# 编译
dotnet build -c Release
```

NuGet 引用：
- `AutoCAD.NET.Core` 23.0.0（AutoCAD 2020 API）
- `AutoCAD.NET.Services` 23.0.0

## 兼容性

| 平台 | 兼容 |
|------|------|
| AutoCAD 2020 | ✅ 已验证 |
| 中望CAD 2020 | ✅ AutoCAD 兼容模式 |
| AutoCAD 2014 | ❌ 暂不支持（API 差异较大） |
| AutoCAD 2021+ | ✅ 理论上兼容 |

由 Claude 生成。
