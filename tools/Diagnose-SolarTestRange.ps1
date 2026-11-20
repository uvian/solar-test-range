<#
.SYNOPSIS
    Diagnose why the SolarTestRange bundle does not autoload in AutoCAD.

.DESCRIPTION
    Run this ON THE MACHINE THAT FAILS, in a normal (non-elevated) PowerShell,
    while AutoCAD is closed. It inspects everything that decides whether a
    .bundle in ApplicationPlugins gets loaded automatically:

      * which AutoCAD-family product is installed
      * every ApplicationPlugins folder, and whether the folder name really
        ends with .bundle
      * accidental nesting (ApplicationPlugins\X.bundle\X.bundle\...)
      * PackageContents.xml: encoding, XML validity, SeriesMin/SeriesMax match,
        LoadOnAutoCADStartup
      * the DLL referenced by ModuleName: exists, blocked by Mark-of-the-Web,
        signed or not
      * .NET Framework 4.7.2 availability
      * leftovers under HKCU\...\Applications that pin an old LOADER path
      * UAC VirtualStore redirection of %ProgramData%\Autodesk\ApplicationPlugins
      * read ACL for the current user
      * antivirus products present

.PARAMETER ReportPath
    Where to write the text report. Defaults to the Desktop.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Diagnose-SolarTestRange.ps1
#>
[CmdletBinding()]
param(
    [string]$ReportPath = (Join-Path ([Environment]::GetFolderPath('Desktop')) ("SolarTestRange-Diagnose-{0}.txt" -f $env:COMPUTERNAME))
)

$ErrorActionPreference = 'Continue'
$lines = New-Object System.Collections.Generic.List[string]
$problems = New-Object System.Collections.Generic.List[string]
$notes = New-Object System.Collections.Generic.List[string]

function W([string]$text) { $lines.Add($text) }
function H([string]$text) { $lines.Add(''); $lines.Add('== ' + $text + ' ' + ('=' * [Math]::Max(0, 66 - $text.Length))) }
function Bad([string]$text) { $problems.Add($text); $lines.Add('  [问题] ' + $text) }
function Good([string]$text) { $lines.Add('  [正常] ' + $text) }
function Info([string]$text) { $lines.Add('  [信息] ' + $text) }
function Note([string]$text) { $notes.Add($text); $lines.Add('  [提示] ' + $text) }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

W 'SolarTestRange 自动加载诊断报告'
W ('生成时间 : ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
W ('计算机   : ' + $env:COMPUTERNAME)
W ('用户     : ' + $env:USERNAME)
W ('管理员   : ' + $isAdmin)
W ('PowerShell: ' + $PSVersionTable.PSVersion)

# ---------------------------------------------------------------- 1. 产品识别
H '1. 已安装的 CAD 产品'

$acadRoot = 'HKLM:\SOFTWARE\Autodesk\AutoCAD'
$acadFound = @()
if (Test-Path $acadRoot) {
    foreach ($rel in Get-ChildItem $acadRoot -ErrorAction SilentlyContinue) {
        foreach ($prod in Get-ChildItem $rel.PSPath -ErrorAction SilentlyContinue) {
            $p = Get-ItemProperty $prod.PSPath -ErrorAction SilentlyContinue
            if ($p.ProductName) {
                $acadFound += [pscustomobject]@{ Release = $rel.PSChildName; Product = $p.ProductName; Location = $p.AcadLocation }
                Info ("{0}  {1}   [{2}]" -f $rel.PSChildName, $p.ProductName, $p.AcadLocation)
            }
        }
    }
}
if ($acadFound.Count -eq 0) {
    Bad '未在注册表找到 AutoCAD。若实际使用的是中望/浩辰，见下面第 8 节。'
}

foreach ($zw in 'HKLM:\SOFTWARE\ZWSOFT\ZWCAD', 'HKLM:\SOFTWARE\WOW6432Node\ZWSOFT\ZWCAD') {
    if (Test-Path $zw) { foreach ($v in Get-ChildItem $zw) { Bad ("检测到中望 CAD：{0}。PackageContents.xml 里 Platform=`"AutoCAD`" 的包中望不会从 C:\ProgramData\Autodesk\ApplicationPlugins 自动加载。" -f $v.PSChildName) } }
}
foreach ($gs in 'HKLM:\SOFTWARE\Gstarsoft\GstarCAD', 'HKLM:\SOFTWARE\WOW6432Node\Gstarsoft\GstarCAD') {
    if (Test-Path $gs) { foreach ($v in Get-ChildItem $gs) { Bad ("检测到浩辰 CAD：{0}。同上，Autodesk 的 ApplicationPlugins 对它无效。" -f $v.PSChildName) } }
}

# ------------------------------------------------------- 2. ApplicationPlugins
H '2. ApplicationPlugins 目录与 bundle 结构'

$pluginDirs = @(
    (Join-Path $env:ProgramData 'Autodesk\ApplicationPlugins'),
    (Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'),
    (Join-Path ${env:ProgramFiles} 'Autodesk\ApplicationPlugins'),
    (Join-Path ${env:ProgramFiles(x86)} 'Autodesk\ApplicationPlugins')
) | Where-Object { $_ -and $_ -notlike '*x86*' -or $true } | Select-Object -Unique

$bundleManifests = @()

foreach ($dir in $pluginDirs) {
    if (-not (Test-Path -LiteralPath $dir)) { Info "不存在: $dir"; continue }
    Info "目录: $dir"

    foreach ($child in Get-ChildItem -LiteralPath $dir -ErrorAction SilentlyContinue) {
        $isDir = $child.PSIsContainer
        if (-not $isDir) { continue }

        if ($child.Name -notlike '*.bundle') {
            if (Test-Path (Join-Path $child.FullName 'PackageContents.xml')) {
                Bad ("'{0}' 里有 PackageContents.xml，但文件夹名不以 .bundle 结尾 —— AutoCAD 会直接忽略。请重命名为 '{0}.bundle'。" -f $child.Name)
            }
            continue
        }

        $manifest = Join-Path $child.FullName 'PackageContents.xml'
        if (-not (Test-Path -LiteralPath $manifest)) {
            # 典型的双层嵌套
            $inner = @(Get-ChildItem -LiteralPath $child.FullName -Filter 'PackageContents.xml' -Recurse -ErrorAction SilentlyContinue)
            if ($inner.Count -gt 0) {
                Bad ("'{0}' 根目录没有 PackageContents.xml，真正的清单在更深一层：{1}。这是解压时多套了一层目录。" -f $child.Name, $inner[0].FullName)
                $bundleManifests += $inner[0]
            }
            else {
                Bad ("'{0}' 里没有 PackageContents.xml，这个 bundle 是空的/不完整。" -f $child.Name)
            }
            continue
        }
        $bundleManifests += (Get-Item -LiteralPath $manifest)
    }
}

if ($bundleManifests.Count -eq 0) {
    Bad '所有 ApplicationPlugins 目录里都没有找到有效的 SolarTestRange.bundle\PackageContents.xml。'
}

# ------------------------------------------------------------ 3. 清单内容校验
H '3. PackageContents.xml 内容校验'

foreach ($m in $bundleManifests) {
    Info "清单: $($m.FullName)"

    if ($m.FullName -notmatch '[\\/]SolarTestRange\.bundle[\\/]') { continue }   # 只深查本插件

    $bytes = [System.IO.File]::ReadAllBytes($m.FullName)
    $nonAscii = 0
    $firstBad = -1
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -ge 0x80) { $nonAscii++; if ($firstBad -lt 0) { $firstBad = $i } }
    }
    if ($nonAscii -gt 0) {
        Note ("清单含 {0} 个非 ASCII 字节，首个在偏移 0x{1:x}。v1.1.1 的发布包就是在这里坏的（中文被存成 '?'，属性引号丢失）。" -f $nonAscii, $firstBad)
    }

    $doc = New-Object System.Xml.XmlDocument
    $parsed = $false
    try { $doc.Load($m.FullName); $parsed = $true }
    catch {
        Bad ("PackageContents.xml 无法解析：{0}。AutoCAD 会静默跳过整个 bundle —— 这正是你们看到的『拷贝了但不自动加载』。" -f $_.Exception.Message)
    }
    if (-not $parsed) { continue }

    $series = $doc.SelectSingleNode('//RuntimeRequirements')
    if ($series) {
        $min = $series.GetAttribute('SeriesMin'); $max = $series.GetAttribute('SeriesMax')
        Info "SeriesMin=$min  SeriesMax=$max"
        foreach ($a in $acadFound) {
            if ($a.Release -and ($a.Release -lt $min -or $a.Release -gt $max)) {
                Bad ("本机是 {0}，但清单只声明 {1}~{2}，版本不匹配就不会自动加载。" -f $a.Release, $min, $max)
            }
        }
    }

    $entries = $doc.SelectNodes('//ComponentEntry')
    if ($entries.Count -eq 0) { Bad '清单里没有 <ComponentEntry>。' }
    foreach ($e in $entries) {
        $startup = $e.GetAttribute('LoadOnAutoCADStartup')
        if ($startup -ne 'True') { Bad ("ComponentEntry 的 LoadOnAutoCADStartup='{0}'，不会随 CAD 启动加载。" -f $startup) }
        else { Good 'LoadOnAutoCADStartup=True' }

        $module = $e.GetAttribute('ModuleName')
        if ($module) {
            $rel = $module -replace '^\./', '' -replace '/', [IO.Path]::DirectorySeparatorChar
            $dll = Join-Path $m.DirectoryName $rel
            if (Test-Path -LiteralPath $dll) {
                $fi = Get-Item -LiteralPath $dll
                Good ("模块存在: {0}  ({1:N0} 字节, 修改于 {2})" -f $rel, $fi.Length, $fi.LastWriteTime)
                $script:targetDll = $fi

                # Mark of the Web
                $zid = Get-Item -LiteralPath $fi.FullName -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue
                if ($zid) {
                    Bad 'DLL 带 Zone.Identifier（来自网络/压缩包的"已阻止"标记）。SECURELOAD=1 时 AutoCAD 会拒绝自动加载，但 NETLOAD 会弹框让你确认后加载成功 —— 与症状完全吻合。解除：Unblock-File。'
                }
                else { Good 'DLL 没有 Mark-of-the-Web 阻止标记' }

                $sig = Get-AuthenticodeSignature -LiteralPath $fi.FullName
                Info ("Authenticode: {0}" -f $sig.Status)
                if ($sig.Status -ne 'Valid') { Note 'DLL 未签名。部分企业安全软件会静默隔离 ProgramData 下的未签名 .NET DLL。' }
            }
            else {
                Bad ("ModuleName 指向的文件不存在: {0}" -f $dll)
            }
        }
    }
}

# ------------------------------------------------------------ 4. .NET Framework
H '4. .NET Framework 运行时'

$ndp = 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
if (Test-Path $ndp) {
    $rel = (Get-ItemProperty $ndp -ErrorAction SilentlyContinue).Release
    Info ("NDP\v4\Full Release = {0}" -f $rel)
    if ($rel -lt 461808) {
        Bad ("需要 .NET Framework 4.7.2 (Release >= 461808)，当前 {0}。程序集加载会失败。" -f $rel)
    }
    else { Good '.NET Framework >= 4.7.2' }
}
else { Bad '未检测到 .NET Framework 4.x' }

# ------------------------------------------------ 5. 注册表 Applications 残留
H '5. HKCU 里残留的插件注册（导致 CAD 记住失败的路径）'

$hitRegistry = $false
foreach ($base in 'HKCU:\Software\Autodesk\AutoCAD') {
    if (-not (Test-Path $base)) { continue }
    foreach ($rel in Get-ChildItem $base -ErrorAction SilentlyContinue) {
        foreach ($prof in Get-ChildItem $rel.PSPath -ErrorAction SilentlyContinue) {
            $appKey = Join-Path $prof.PSPath 'Applications\SolarTestRange'
            if (Test-Path $appKey) {
                $hitRegistry = $true
                $p = Get-ItemProperty $appKey -ErrorAction SilentlyContinue
                Bad ("存在注册项 {0}\SolarTestRange：LOADER='{1}' LOADCTRLS='{2}' MANAGED='{3}'。如果 LOADER 指向旧路径或 LOADCTRLS=0，CAD 会一直跳过它。" -f $prof.PSPath, $p.LOADER, $p.LOADCTRLS, $p.MANAGED)
            }
        }
    }
}
if (-not $hitRegistry) { Good '没有残留的 SolarTestRange 注册项' }

# ---------------------------------------------------------- 6. VirtualStore
H '6. UAC 虚拟化重定向检查'

$vs = Join-Path $env:LOCALAPPDATA 'VirtualStore\ProgramData\Autodesk\ApplicationPlugins'
if (Test-Path $vs) {
    Bad ("发现 {0}。说明之前往 C:\ProgramData 拷贝时被 UAC 重定向了，CAD 读的是 ProgramData，看不到这里的文件（或反过来）。请用管理员身份重新拷贝。" -f $vs)
}
else { Good '没有 VirtualStore 重定向副本' }

# ------------------------------------------------------------------ 7. ACL
H '7. 当前用户对插件目录的读取权限'

foreach ($dir in $pluginDirs) {
    if (-not (Test-Path -LiteralPath $dir)) { continue }
    try {
        $probe = Join-Path $dir 'SolarTestRange.bundle\PackageContents.xml'
        if (Test-Path -LiteralPath $probe) {
            [void][System.IO.File]::ReadAllBytes($probe)
            Good ("可读: {0}" -f $probe)
        }
        else {
            [void](Get-ChildItem -LiteralPath $dir -ErrorAction Stop | Select-Object -First 1)
            Good ("可列出: {0}" -f $dir)
        }
    }
    catch {
        Bad ("无权限访问 {0}：{1}" -f $dir, $_.Exception.Message)
    }
}

# -------------------------------------------------------------- 8. 安全软件
H '8. 安全软件（可能静默隔离未签名 DLL）'

try {
    $avs = Get-CimInstance -Namespace 'root/SecurityCenter2' -ClassName AntiVirusProduct -ErrorAction Stop
    foreach ($a in $avs) { Info ("{0}  [{1}]" -f $a.displayName, ('0x{0:x}' -f $a.productState)) }
    if (-not $avs) { Info '未报告任何杀毒产品' }
}
catch { Info '无法查询 SecurityCenter2' }

# -------------------------------------------------------------- 9. 人工核对
H '9. 必须在 AutoCAD 命令行里核对的三项'

Note 'SECURELOAD —— 输入 SECURELOAD，应为 1（默认）。为 1 时只从"受信任的位置"加载，ProgramData\Autodesk\ApplicationPlugins 默认不在其中。'
Note 'APPAUTOLOAD —— 输入 APPAUTOLOAD，必须为 1。为 0 时完全不看 ApplicationPlugins 目录。'
Note 'TRUSTEDPATHS —— 输入 TRUSTEDPATHS，确认包含 C:\ProgramData\Autodesk\ApplicationPlugins；没有就用 选项 → 文件 → 受信任的位置 添加。'
Note 'CAD 里输入 (findfile "SolarTestRange.dll") 应返回 nil（bundle 内的 dll 不在搜索路径里，这是正常的）。'
Note '若曾加载失败过，CAD 会记住：删除第 5 节点出的注册项，或重置配置后重启。'

# ------------------------------------------------------------------- 输出
H '结论'

if ($problems.Count -eq 0) {
    W '  未发现本地配置问题。请把本报告和 CAD 的 SECURELOAD/APPAUTOLOAD/TRUSTEDPATHS 取值一并反馈。'
}
else {
    W ("  发现 {0} 个问题，按出现顺序：" -f $problems.Count)
    for ($i = 0; $i -lt $problems.Count; $i++) { W ("  {0}. {1}" -f ($i + 1), $problems[$i]) }
}

$lines | Set-Content -LiteralPath $ReportPath -Encoding UTF8
Write-Host ''
Write-Host ('报告已保存: ' + $ReportPath) -ForegroundColor Cyan
Write-Host ''
$lines | ForEach-Object { Write-Host $_ }
