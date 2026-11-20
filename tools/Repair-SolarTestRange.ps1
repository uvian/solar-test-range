<#
.SYNOPSIS
    Repair an already-copied SolarTestRange bundle in place.

.DESCRIPTION
    Fixes the three things that actually break autoload on an end user machine:

      1. Mark-of-the-Web on PackageContents.xml / the DLL  -> Unblock-File
      2. wrong folder layout (missing .bundle suffix, one extra nesting level)
      3. leftover HKCU\...\Applications\SolarTestRange entry pinning an old path

    It does NOT touch SECURELOAD / APPAUTOLOAD / TRUSTEDPATHS because those are
    AutoCAD system variables; the script prints the exact commands to run inside
    AutoCAD instead of guessing.

    Requires an elevated PowerShell when fixing the machine-wide
    C:\ProgramData\Autodesk\ApplicationPlugins folder.

.PARAMETER InstallRoot
    ApplicationPlugins folder to repair. Defaults to the machine-wide one.

.PARAMETER RemoveStaleRegistration
    Delete HKCU\Software\Autodesk\AutoCAD\*\*\Applications\SolarTestRange.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Repair-SolarTestRange.ps1 -RemoveStaleRegistration
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:ProgramData 'Autodesk\ApplicationPlugins'),
    [switch]$RemoveStaleRegistration
)

$ErrorActionPreference = 'Continue'
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

Write-Host "== SolarTestRange 修复 ==" -ForegroundColor Cyan
Write-Host "目标目录: $InstallRoot"
Write-Host "管理员  : $isAdmin"

if (-not (Test-Path -LiteralPath $InstallRoot)) {
    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    Write-Host "已创建目录: $InstallRoot" -ForegroundColor Yellow
}

$bundle = Join-Path $InstallRoot 'SolarTestRange.bundle'

# --- 1. 结构纠正 -------------------------------------------------------------
if (Test-Path -LiteralPath $bundle) {
    if (-not (Test-Path -LiteralPath (Join-Path $bundle 'PackageContents.xml'))) {
        $inner = @(Get-ChildItem -LiteralPath $bundle -Filter 'PackageContents.xml' -Recurse -ErrorAction SilentlyContinue)
        if ($inner.Count -eq 1) {
            $src = $inner[0].Directory
            Write-Host "检测到多套了一层目录，正在把 $($src.FullName) 的内容提升到 bundle 根 ..." -ForegroundColor Yellow
            foreach ($item in Get-ChildItem -LiteralPath $src.FullName -Force) {
                Move-Item -LiteralPath $item.FullName -Destination $bundle -Force
            }
            Get-ChildItem -LiteralPath $bundle -Directory |
                Where-Object { -not (Get-ChildItem -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue) } |
                Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "结构已纠正" -ForegroundColor Green
        }
        else {
            Write-Host "无法自动纠正结构：找到 $($inner.Count) 个 PackageContents.xml" -ForegroundColor Red
        }
    }
    else {
        Write-Host "结构正常：PackageContents.xml 在 bundle 根目录" -ForegroundColor Green
    }
}
else {
    Write-Host "没有找到 $bundle，请先把 SolarTestRange.bundle 文件夹拷贝过来。" -ForegroundColor Red
}

# --- 2. 解除 Mark-of-the-Web -------------------------------------------------
$unblocked = 0
if (Test-Path -LiteralPath $bundle) {
    Get-ChildItem -LiteralPath $bundle -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
        $z = Get-Item -LiteralPath $_.FullName -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue
        if ($z) {
            try {
                Unblock-File -LiteralPath $_.FullName -ErrorAction Stop
                $unblocked++
                Write-Host "已解除阻止: $($_.Name)" -ForegroundColor Yellow
            }
            catch { Write-Host "解除失败: $($_.FullName) -> $($_.Exception.Message)" -ForegroundColor Red }
        }
    }
}
Write-Host "共解除 $unblocked 个文件的网络阻止标记" -ForegroundColor Green

# --- 3. 校验清单 -------------------------------------------------------------
$manifest = Join-Path $bundle 'PackageContents.xml'
if (Test-Path -LiteralPath $manifest) {
    $doc = New-Object System.Xml.XmlDocument
    try {
        $doc.Load($manifest)
        Write-Host "PackageContents.xml 解析正常" -ForegroundColor Green
        $entry = $doc.SelectSingleNode('//ComponentEntry')
        if ($entry -and $entry.GetAttribute('LoadOnAutoCADStartup') -ne 'True') {
            Write-Host "注意：这份清单没有 LoadOnAutoCADStartup=True，升级到 1.1.2+ 的发布包。" -ForegroundColor Yellow
        }
        $mod = $entry.GetAttribute('ModuleName')
        if ($mod) {
            $dll = Join-Path $bundle ($mod -replace '^\./', '' -replace '/', '\')
            if (Test-Path -LiteralPath $dll) { Write-Host "模块存在: $dll" -ForegroundColor Green }
            else { Write-Host "模块缺失: $dll" -ForegroundColor Red }
        }
    }
    catch {
        Write-Host "PackageContents.xml 是坏的：$($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  -> 请用 v1.1.2 或更新版本的发布包替换它，不要手工编辑。" -ForegroundColor Red
    }
}

# --- 4. 清理过期注册 ---------------------------------------------------------
if ($RemoveStaleRegistration) {
    $removed = 0
    foreach ($base in 'HKCU:\Software\Autodesk\AutoCAD') {
        if (-not (Test-Path $base)) { continue }
        foreach ($rel in Get-ChildItem $base -ErrorAction SilentlyContinue) {
            foreach ($prof in Get-ChildItem $rel.PSPath -ErrorAction SilentlyContinue) {
                $appKey = Join-Path $prof.PSPath 'Applications\SolarTestRange'
                if (Test-Path $appKey) {
                    Remove-Item -LiteralPath $appKey -Recurse -Force -ErrorAction SilentlyContinue
                    if (-not (Test-Path $appKey)) { $removed++; Write-Host "已删除注册项: $appKey" -ForegroundColor Yellow }
                }
            }
        }
    }
    Write-Host "共删除 $removed 个 SolarTestRange 注册项" -ForegroundColor Green
}

# --- 5. 剩余人工步骤 ---------------------------------------------------------
Write-Host ''
Write-Host '== 还需要在 AutoCAD 命令行里做（脚本改不了这些系统变量）==' -ForegroundColor Cyan
Write-Host '  1) SECURELOAD      -> 默认 1。可从 选项 → 文件 → 受信任的位置 添加：'
Write-Host "       $InstallRoot"
Write-Host '  2) APPAUTOLOAD     -> 必须为 1（为 0 时完全不扫描 ApplicationPlugins）'
Write-Host '  3) 完全退出 AutoCAD（检查任务管理器里没有 acad.exe）后重新启动'
Write-Host '  4) 启动后直接输入 HUATU，不需要 NETLOAD'
Write-Host ''
Write-Host ('安装（管理员）：powershell -ExecutionPolicy Bypass -File {0} -RemoveStaleRegistration' -f $PSCommandPath)
