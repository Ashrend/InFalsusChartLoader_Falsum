# InFalsusChartLoader_falsum

In Falsus 的自制谱加载 Mod，使用 [MelonLoader](https://github.com/LavaGang/MelonLoader) 加载。

---

## 构建

需要 [.NET SDK](https://dotnet.microsoft.com/download)（目标框架 `net6.0`）。

```cmd
build.bat                  :: MelonLoader Release（默认）
build.bat Debug            :: MelonLoader Debug
```

构建产物位于 `bin/`。Release 构建会执行额外的产物字符串检查。

曲包规则的 C# 测试：

```cmd
dotnet run --project Tests/PackRulesTests/PackRulesTests.csproj
```

---

## 安装

### MelonLoader

1. 安装 MelonLoader。
2. 将 `InFalsusChartLoader_falsum.dll` 放进游戏目录下的 `Mods` 文件夹。
3. 从 Steam 启动游戏。

---

## 谱面目录

旧格式仍兼容：`Charts` 下每个直接子文件夹是一首歌，统一进入 `IFCL` 曲包。

```text
Charts/
  legacy_song/
    if
```

新格式支持自定义曲包：

```text
Charts/
  [Pack]test1/
    pack
    pack_cover.png
    abc/
      abc.falsum
```

规则：

- `[Pack]` 后面是逻辑曲包 ID，例如 `test1`。
- `pack` 是无扩展名 JSON 文件，当前支持 `name` 的五个语言键：`en`、`ja`、`ko`、`zh-Hant`、`zh-Hans`。
- 缺失语言会按 `当前语言 → en → ja → ko → zh-Hant → zh-Hans` 回退。
- `pack_cover.png` 可选；缺失或无效时回退到游戏已有曲包视觉。
- 曲包内每个子文件夹是一首歌，继续支持 `if` 和 `.falsum`。
- 曲包按文件夹名排序，旧的 `IFCL` 位于自定义曲包之后。
- `pack` 缺失、JSON 无效或没有任何有效名称时，整个曲包跳过。

示例：

```json
{
  "name": {
    "en": "Test Pack",
    "ja": "テストパック",
    "zh-Hans": "测试曲包"
  }
}
```

完整示例见 [PACK.EXAMPLE.md](./docs/PACK.EXAMPLE.md)。

单曲 `if` 格式见 [IF.EXAMPLE.md](./docs/IF.EXAMPLE.md)。

---

## 日志

游戏启动后，MelonLoader 日志会报告导入和注册数量，例如：

```text
packs: 3 imported, songs: 6 imported
packs: 3 registered (2 custom, 1 IFCL), songs: 6 registered
```

Debug 构建会额外输出曲包、封面和字段解析细节。

## 致谢

- [MelonLoader](https://github.com/LavaGang/MelonLoader)
- [源仓库](https://github.com/Its-MagicNeko/InFalsusChartLoader)

## 免责声明

本仓库为非官方第三方项目，与 In Falsus、lowiro limited 及其关联公司、子公司、许可方或其他相关实体均无隶属、合作、赞助、认可或授权关系。
