# Fonts / 字体

界面里的数字和拉丁字母用 **Outfit**：几何无衬线，观感接近 osu!(lazer) 的 Torus，
授权是 **SIL OFL 1.1** —— 可以随程序一起分发，商用也不用另外买。

| 项 | 内容 |
|---|---|
| 设计 | Rodrigo Fuenzalida（fragTYPE）— <https://github.com/Outfitio/Outfit-Fonts> |
| 授权 | SIL Open Font License 1.1，全文见 `OFL-Outfit.txt` |
| 文件 | `Outfit-Regular.ttf` / `Outfit-SemiBold.ttf` / `Outfit-Bold.ttf`（各 ~55KB） |
| 家族名 | `Outfit`（typographic family = name id 16） |
| 来源 | `google/fonts` 的 `ofl/outfit/Outfit[wght].ttf`（可变字体，110KB） |

## 为什么要切成静态字面

`google/fonts` 只提供可变字体（variable font）。**WPF 不支持可变轴**，
加载 `Outfit[wght].ttf` 只会拿到默认实例（`wght=100`，也就是 Thin），
界面会变成很细的字。所以仓库里放的是切好的三个静态实例：

```python
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

for weight, style in ((400, "Regular"), (600, "SemiBold"), (700, "Bold")):
    font = TTFont("Outfit[wght].ttf")
    # 注意 inplace=True：默认 inplace=False 会返回新对象，改原对象等于什么都没做
    instantiateVariableFont(font, {"wght": weight}, inplace=True, updateFontNames=False)
    # 再把 name 表写成 id16="Outfit" / id17=style，WPF 才能把三个字面归到同一家族
    font.save(f"Outfit-{style}.ttf")
```

`name` 表的关键点：

- id 16（typographic family）三个文件都写 `Outfit`，id 17 写 `Regular` / `SemiBold` / `Bold`；
- id 1（legacy family）按 RIBBI 规则：Regular 和 Bold 写 `Outfit`，SemiBold 写 `Outfit SemiBold`。
  WPF 按 id16 + id17 分组，`FontWeight="SemiBold"` 才能命中 600 那个字面（而不是被拉到 700）。

## 中文

Outfit 没有汉字字形。WPF 会逐字符回退到系统字体 `Microsoft YaHei UI`，
所以中文照旧正常显示（和 osu!lazer 用 Noto 补 CJK 是一个思路）。
不要把它换成"去掉回退"的裁剪版本，否则中文会变方框。

## 一个 WPF 的坑（和字体文件无关，但会让人以为字体没生效）

`<FontFamily>pack://application:,,,/Fonts/#Outfit</FontFamily>` 这种写法**解析不出字形面**：
字符串建出来的 `FontFamily` 的 `BaseUri` 是空的，`Typeface.TryGetGlyphTypeface` 一律返回 `false`
（pack URI、`/Fonts/#Outfit`、`./Fonts/#Outfit`、file URI 等 9 种写法全试过，全部失败；
而同样目录的 `Fonts.GetFontFamilies(uri)` 却能正常列出 `./#Outfit`）。
所以字体家族必须由 `Services/Typography.cs` 在运行时用 `Fonts.GetFontFamilies` 取，
`App.xaml` 里的 `UiFont` 只是个占位（默认系统字体），启动时会被替换掉。
回归测试 `testUiFontIsDynamicResource` 会拦住有人把它写回字面量。
