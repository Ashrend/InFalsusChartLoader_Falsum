# InFalsusChartLoader

In Falsus的自制谱加载Mod，使用[MelonLoader](https://github.com/LavaGang/MelonLoader)

---

## 构建

需要 [.NET SDK](https://dotnet.microsoft.com/download)（目标框架 `net6.0`）

```cmd
build.bat                  :: MelonLoader Release（默认）
build.bat Debug            :: MelonLoader Debug
```

构建产物位于 `bin/`

---

## 安装

### MelonLoader

1. 安装 [MelonLoader](https://github.com/LavaGang/MelonLoader)
2. 获取 `InFalsusChartLoader.dll`
3. 放进游戏目录下的 `Mods` 文件夹
4. 启动游戏

自制谱放在游戏根目录的 `Charts` 文件夹（即 `GAME_PATH/Charts`，若不存在则会自动创建）

---

## 使用

`Charts` 下**每个子文件夹即为一首歌**，mod会严格按照引导文件 `if` 进行加载，详见[if文件示例文档](./IF.EXAMPLE.md)

对于.falsum格式的谱面，Mod会自行读取其中的song.json，并自动将.txt格式的谱面以游戏支持的格式加载

加载后，选曲界面最上方会出现合集 **IFCL**

如果你想自定义曲包，请在Charts下新建一个前缀为'[Pack]'，名称为英文或数字的文件夹，随后把你想加入乐曲包的乐曲的子文件夹放入该文件夹，加载器会自动归类。

曲包元数据的读取依赖于'pack'文件，以JSON格式存储，详见[Pack示例例文档](./pack-example.md)

if文件只要有一条信息非法则不会导入该谱面

需要查看完整日志时请使用**Debug构建**

---

## 致谢

- [MelonLoader](https://github.com/LavaGang/MelonLoader) —— mod 加载器
- [BepInEx](https://github.com/BepInEx/BepInEx) —— mod 加载器
- 源仓库(https://github.com/Its-MagicNeko/InFalsusChartLoader)

## 免责声明

本仓库为非官方第三方项目，与 In Falsus、lowiro limited 及其关联公司、子公司、许可方或其他相关实体均无任何隶属、合作、赞助、认可、授权或其他关联关系，如有侵权请联系作者删除
