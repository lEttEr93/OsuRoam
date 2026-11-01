# Fonts / 字体

`Torus-Regular.otf`、`Torus-Bold.otf` 是 osu!(lazer) 的默认界面字体 **Torus**，
界面里的数字、拉丁字母用它渲染。

| 项 | 内容 |
|---|---|
| 设计 | Paulo Goode — <http://paulogoode.com>，Copyright © 2017 |
| 文件来源 | [osumania_map_analyser](https://github.com/LeoBlackMT/osumania_map_analyser) 的 `styles/fonts/`（该仓库 MIT，但字体本身不归 MIT 覆盖） |
| 家族名 | `Torus`（`pack://application:,,,/Fonts/#Torus`） |
| 字形数 | 363，只有拉丁字母 / 数字 / 常用标点 |

## 授权

osu! 官方在 `osu-resources/Fonts/Torus/LICENCE` 里写明：

> This font requires a commercial licence for distribution.
> Please see <https://www.myfonts.com/fonts/paulo-goode/torus/> for more details.

也就是说 **osu! 自己是有商业授权的，我们只是把字形嵌进界面用**。
本项目的发行版带上了这两个文件：如果将来要做商业分发，需要自己购买 Torus 授权，
或者删掉 `Fonts/` 并在设置里取消勾选「使用 osu! 字体（Torus）」——
那时代码会自动回退到系统字体 `Microsoft YaHei UI`。
仓库里留着它们是因为 osu! 的界面观感离不开这套字形。

## 中文

Torus 没有汉字字形。WPF 会逐字符回退到系统字体，所以中文照旧由
`Microsoft YaHei UI` 渲染（和 osu!lazer 用 Noto 补 CJK 是同一个做法）。
`Fonts` 里的文件不要替换成「去掉回退」的裁剪版本，否则中文会变方框。
