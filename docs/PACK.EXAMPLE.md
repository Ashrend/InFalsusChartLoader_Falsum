# 自定义曲包示例

## 目录

```text
Charts/
  [Pack]test1/
    pack
    abc/
      abc.falsum
```

- `[Pack]test1`：`test1` 是逻辑曲包 ID。
- `pack`：曲包元数据，无扩展名。
- `abc/`：曲包内的一首歌曲目录；歌曲 ID 仍以 `if.id` 或 `.falsum` 的 `songId` 为准。
- 包内歌曲目录可以包含 `if`，也可以包含 `.falsum`。

## `pack`

```json
{
  "name": {
    "en": "Test Pack",
    "ja": "テストパック",
    "ko": "테스트 팩",
    "zh-Hant": "測試曲包",
    "zh-Hans": "测试曲包"
  },
  "pack": "song"
}
```

### `name`

| 键 | 语言 |
|---|---|
| `en` | English |
| `ja` | Japanese |
| `ko` | Korean |
| `zh-Hant` | Traditional Chinese |
| `zh-Hans` | Simplified Chinese |

可以只提供其中一部分。缺失语言按以下顺序回退：

`当前语言 → en → ja → ko → zh-Hant → zh-Hans`

`name` 必须至少有一个非空名称；否则整个曲包跳过。

### 曲包类型

| 取值 | 曲包栏 |
|---|---|
| `story` | 剧情曲包 |
| `song` | DLC 曲包 |

省略时默认为 `story`。键名也可以写 `category` 或 `type`，三者任选其一；取值不区分大小写。

## 排序

- 多个 `[Pack]` 目录按文件夹名排序。
- 旧根目录歌曲归入 `IFCL`，排在所有自定义曲包之后。
- 没有旧根目录歌曲时不会创建空的 `IFCL`。

## 错误处理

- `[Pack]` 后没有 ID、`pack` 缺失、JSON 无效或没有有效名称：跳过整个曲包。
- 包内单首歌曲无效：跳过该歌曲。
- 一个曲包没有任何有效歌曲：不创建该曲包。
- 歌曲 ID 与已有歌曲重复：按现有规则跳过重复歌曲。
