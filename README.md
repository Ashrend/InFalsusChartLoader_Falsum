# InFalsusChartLoader_falsum

In Falsus 的自制谱加载 Mod，支持 [MelonLoader](https://github.com/LavaGang/MelonLoader) 与 [BepInEx](https://github.com/BepInEx/BepInEx) 6（IL2CPP）。

---

## 构建

需要 [.NET SDK](https://dotnet.microsoft.com/download)（目标框架 `net6.0`）。

```cmd
build.bat                  :: MelonLoader Release（默认）
build.bat Debug            :: MelonLoader Debug
build.bat --BepInEx        :: BepInEx Release
build.bat --BepInEx Debug  :: BepInEx Debug
```

构建产物位于 `bin/`（BepInEx 为 `bin/BepInExRelease`、`bin/BepInExDebug`）。Release 构建会执行额外的产物字符串检查。

曲包规则的 C# 测试：

```cmd
dotnet run --project Tests/PackRulesTests/PackRulesTests.csproj
```

---

## 安装

### MelonLoader

1. 安装 MelonLoader。
2. 将 `bin/Release/InFalsusChartLoader_falsum.dll` 放进游戏目录下的 `Mods` 文件夹。
3. 从 Steam 启动游戏。

### BepInEx

1. 安装 BepInEx 6（IL2CPP，x64）。
2. 将 `bin/BepInExRelease/InFalsusChartLoader_falsum.dll` 放进游戏目录下的 `BepInEx\plugins` 文件夹。
3. 从 Steam 启动游戏。

两种加载器都把 `Charts` 放在游戏根目录（与 `infalsus.exe` 同级）。

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
    abc/
      abc.falsum
```

规则：

- `[Pack]` 后面是逻辑曲包 ID，例如 `test1`。
- `pack` 是无扩展名 JSON 文件，当前支持 `name` 的五个语言键：`en`、`ja`、`ko`、`zh-Hant`、`zh-Hans`。
- 缺失语言会按 `当前语言 → en → ja → ko → zh-Hant → zh-Hans` 回退。
- `pack`（或 `category`、`type`）指定曲包栏：`story`（默认）或 `song`，值不区分大小写。
- 曲包内每个子文件夹是一首歌，继续支持 `if` 和 `.falsum`。
- 旧格式歌曲目录和曲包内歌曲目录规则相同：同一歌曲目录同时有 `.falsum` 和 `if` 时，`.falsum` 提供谱面与音频，`if` 只覆盖展示信息；没有曲绘的歌曲会被跳过。
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

游戏启动后，加载器日志会报告导入和注册数量，例如：

```text
packs: 3 imported, songs: 6 imported
packs: 3 registered (2 custom, 1 IFCL), songs: 6 registered
```

Debug 构建会额外输出曲包、曲绘、背景和字段解析细节。

## 致谢

- [MelonLoader](https://github.com/LavaGang/MelonLoader)
- [BepInEx](https://github.com/BepInEx/BepInEx)
- [源仓库](https://github.com/Its-MagicNeko/InFalsusChartLoader)

## 免责声明

本仓库为非官方第三方项目，与 In Falsus、lowiro limited 及其关联公司、子公司、许可方或其他相关实体均无隶属、合作、赞助、认可或授权关系。
