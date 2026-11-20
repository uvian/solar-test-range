# SolarTestRange — AutoCAD 日照测试范围绘制插件

自动生成建筑南北两侧的日照测试扇形范围，适用于 AutoCAD 2020 / 中望CAD 2020。

## 功能

输入建筑轮廓闭合多段线 + 建筑高度，一键绘制：

- **三种绘制范围可选**（框选轮廓后按提示选择）：
  - `N` 北向 2H + 南向 200m（**直接回车即选此项**）
  - `B` 仅绘制北向 2H
  - `S` 仅绘制南向 200m（此模式无需输入建筑高度）
- 八角射线（±35° 方向，北向 2×H，南向 200m）
- 闭合线 + 偏移线，分图层编组
- 四段圆弧

## 命令流程

```
HUATU
  → 选择建筑轮廓 (闭合多段线, 可多选)
  → 选择绘制范围 [北向2H+南向200(N)/仅北向2H(B)/仅南向200(S)] <N>
       直接回车 = N（南北两侧全画），输 B 仅北向，输 S 仅南向
  → 输入建筑高度 (米)          ← 仅当模式包含北向时询问
  → 自动生成对应范围
```

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

## 装上了但不自动加载？

现象：`SolarTestRange.bundle` 已经拷到 `C:\ProgramData\Autodesk\ApplicationPlugins\`，
重启 CAD 后输 `HUATU` 提示未知命令，只能 `NETLOAD` 手动选 `Contents\SolarTestRange.dll` 才能用；
同一个 CAD 版本，别的同事却正常。

按命中率从高到低排查：

### 1. 发布包里的清单坏了（v1.1.1 全体中招，已修复）

`PackageContents.xml` 里混进了非 ASCII 字节（中文被存成 `?`），把属性的闭合引号吃掉了：

```xml
<Components Description="主程?>     <!-- 引号没闭合，XML 非法 -->
```

AutoCAD 解析失败后**不报错、不弹窗**，直接跳过整个 bundle。而 `Contents\SolarTestRange.dll`
本身是好的，所以 `NETLOAD` 照样能加载 —— 症状完全对得上。

换 v1.1.2+ 的包即可。自检：

```powershell
powershell -ExecutionPolicy Bypass -File tools\Test-Bundle.ps1 -Path "C:\ProgramData\Autodesk\ApplicationPlugins\SolarTestRange.bundle"
```

### 2. 解压后目录结构不对

`ApplicationPlugins` 下面必须正好是 `SolarTestRange.bundle\PackageContents.xml`：

```
❌ ApplicationPlugins\SolarTestRange.bundle\SolarTestRange.bundle\PackageContents.xml   多套了一层
❌ ApplicationPlugins\SolarTestRange\PackageContents.xml                                名字少了 .bundle
✅ ApplicationPlugins\SolarTestRange.bundle\PackageContents.xml
```

v1.1.1 及更早的 zip 用反斜杠作路径分隔符，Windows 资源管理器解压会解坏，7-Zip/WinRAR 正常
—— 这是"有人能用有人不能"的直接来源。v1.1.2 起改用 `tar` 打包，已修正。

### 3. 文件被 Windows 标记为"来自网络"

从 zip 解出来的文件带 `Zone.Identifier`。`SECURELOAD=1` 时 AutoCAD 拒绝自动加载，
但 `NETLOAD` 会弹框让你确认，点"加载"就成功。一条命令解除：

```powershell
Get-ChildItem "C:\ProgramData\Autodesk\ApplicationPlugins\SolarTestRange.bundle" -Recurse -File | Unblock-File
```

### 4. AutoCAD 的三个开关

| 变量 | 期望值 | 说明 |
|------|--------|------|
| `APPAUTOLOAD` | `1` | 为 0 时完全不扫描 ApplicationPlugins，常见于"CAD 优化/精简"脚本 |
| `SECURELOAD` | `1` | 为 1 时只从受信任位置加载 |
| `TRUSTEDPATHS` | 含插件目录 | 把 `C:\ProgramData\Autodesk\ApplicationPlugins` 加进 `选项 → 文件 → 受信任的位置` |

### 5. CAD 记住了上一次失败

首次加载失败后，`HKCU\Software\Autodesk\AutoCAD\R23.1\ACAD-*\Applications\SolarTestRange`
会留下旧的 `LOADER` 路径，之后启动直接跳过。删掉该键再重启（管理员）：

```powershell
powershell -ExecutionPolicy Bypass -File tools\Repair-SolarTestRange.ps1 -RemoveStaleRegistration
```

### 6. 其它

- `SeriesMin/Max="R23.1"` 只认 AutoCAD 2020。装的是 2021+ / 中望 / 浩辰，需按对应 API 重新编译，或改用方式二。
- 未签名 DLL 在 `ProgramData` 下可能被安全软件静默隔离，确认 `Contents\SolarTestRange.dll` 还在。
- 需要 .NET Framework 4.7.2+（Win10 1803 起自带）。

一键体检（在出问题的机器上跑，报告输出到桌面）：

```powershell
powershell -ExecutionPolicy Bypass -File tools\Diagnose-SolarTestRange.ps1
```

## 打包校验

发布前必须过一遍，CI 已强制：

```bash
pwsh -File tools/Test-Bundle.ps1 -Path dist/SolarTestRange.bundle.zip
```

它会拦住五类问题：清单非纯 ASCII、清单解析失败、缺 `LoadOnAutoCADStartup="True"`、
`ModuleName` 指向的文件不存在、zip 用反斜杠分隔路径。
