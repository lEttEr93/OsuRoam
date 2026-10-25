# osu! Roam v1.1.1

osu! Roam 是 osu! 谱面漫游推荐工具。它通过 osu! Web API 获取玩家资料和谱面候选，通过 tosu 读取游玩状态，并按星级、BPM、时长、谱师偏好、探索度和 tag 偏好生成推荐池。

## 快速使用

1. 解压 `osu-roam-v1.1.1-portable.zip`。
2. 双击 `OsuRoam.exe`。
3. 填写 osu! OAuth Client ID、Client Secret 和玩家名。
4. 按需设置游戏模式、mania 轨道数、星级范围和筛选条件。
5. 点击“开始漫游”。

便携包已包含自包含 .NET 运行环境，不需要另行安装 .NET。使用自动下载/导入时需要 osu!lazer 和 tosu 正常运行。

## 星级

星级范围默认使用手动设置。点击“按玩家水平计算一次星级”才会根据玩家成绩样本更新一次范围；开始漫游不会覆盖手动设置。

## Tag 筛选

Tag 池包含预设曲风和 mania 常见特征，也会从 API 搜索结果中持续收集新 tag，并永久保存到本地配置。

- 推荐 tag：软偏好，只提高命中谱面的排序分数，不会排除未命中的谱面。
- 排除 tag：绝对排除，命中任意排除 tag 的谱面不会推荐。
- 在 tag 搜索框中搜索，列表支持多选。
- 已选 tag 显示为可点击标签，点击即可取消。

## 配置和日志

配置文件位于 `%LOCALAPPDATA%\\OsuRoam\\settings.json`，下载缓存位于 `%LOCALAPPDATA%\\OsuRoam\\Beatmaps`。不要把包含 OAuth Secret 的配置文件分享给他人。

## 构建

需要 .NET 8 SDK：

```powershell
$env:PATH="C:\\Users\\<user>\\.dotnet;" + $env:PATH
dotnet publish OsuRoam.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o bin\\v1.1.1
```
