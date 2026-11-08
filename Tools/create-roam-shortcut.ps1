# 在桌面放一个「osu! Roam (dev)」快捷方式，指向仓库里的 roam.vbs：
# 双击 = 有改动就增量编译，然后启动（不闪黑框），编译失败会打开日志。
#
# 用法：create-shortcut.cmd      （在仓库根双击即可）

$root = Split-Path -Parent $PSScriptRoot
$vbs = Join-Path $root "roam.vbs"
$icon = Join-Path $root "Assets\OsuRoam.ico"
$wscript = Join-Path $env:WINDIR "System32\wscript.exe"

if (-not (Test-Path $vbs)) { throw "找不到 roam.vbs：$vbs" }

$desktop = [Environment]::GetFolderPath("Desktop")
$target = Join-Path $desktop "osu! Roam (dev).lnk"

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($target)
$shortcut.TargetPath = $wscript
$shortcut.Arguments = '"' + $vbs + '"'
$shortcut.WorkingDirectory = $root
if (Test-Path $icon) { $shortcut.IconLocation = "$icon,0" }
$shortcut.Description = "osu! Roam (dev): incremental build, then launch"
$shortcut.Save()

Write-Host "桌面快捷方式已创建：$target"
Write-Host "双击它就会「编译（如有改动）→ 启动」，再双击会先关掉旧的再开一个。"
