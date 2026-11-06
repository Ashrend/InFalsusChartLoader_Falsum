# 加载速度优化 — 进度台账

> 最后更新：2026-11-06。写这份台账是因为**当前有更重要的事**，工程需要暂停。
> 这份文件可以独立阅读 —— 不用翻对话记录就能接着做。

---

## 一句话现状

**代码全部写完并过审，实测把启动的钩子阶段从 39.8 s 降到 10.5 s，分层改造预期再降到 ~5 s —— 但分层这一版还没在游戏里验证过。**

**计划③ 的 Task 4 也接上了**（`3779bb8`）：后台导入 + 分帧校验现在真的在跑，
并补了一轮修复（整支复核提出的两个 Important）。**计划③ 和计划④ 都已在游戏里验证过。**

---

## 数字（除标注外都是实测，不是估算）

| | 起点 | 现在（2026-11-06 实测）|
|---|---|---|
| **钩子装载** | **39.8 s**（基准 v0.4.0 的 Release 日志实测）| 启动 **3,971 ms**（第一层 10 个）+ 标题画面 **4,576 ms**（推迟层 14 个）= **8.5 s** |
| **逐目标解析** | 1500–3000 ms | **0 ms** —— `address cache: restored=29 resolved=0` |
| **启动时的导入** | 6.73 s（冷）/ 523 ms（热）| **0** —— 读文件 + 解压 `.falsum` 在后台 worker 做 |
| **第一次进选歌** | 6.73 s（冷）+ 39.8 s 钩子 | 读文件 **3 ms**（用快照）+ 注册 5,091 ms |
| **interop 索引** | 4.8 s | 4.8 s —— **还没动过** |

关键事实：**每次原生钩子装载固定约 320 ms**（启动实测 313–340 ms），所以那 8.5 秒是 24 × 320 ms。

**⚠️ 钩子的价格取决于游戏在干什么。** 同一个 attach 调用：在启动时 **320 ms**，
在**选歌场景加载的 Awake 里** 1,900 ms（贵 6 倍）。计划④ 原本把 14 个钩子装在选歌的 Awake 里，
实测 **26,944 ms** —— 选歌界面**冻住 34.8 秒**（日志里一帧都没有）。
现在改成**第一帧（标题画面）**装：4,576 ms，每个 320 ms，**选歌冻 7.4 s**。

**注意**：2.8 s / 6.73 s 那个导入数字是**冷缓存**（第一次解压 `.falsum`，60 MB）。
缓存热了以后导入只要 523 ms —— 差了 13 倍。

---

## 代码在哪

- 分支：`feature/load-time-optimization` → **已快进合回 `main`**（`main` = `c8bf81d`，29 个 commit）
- 工作区：`.superpowers/wt/load-perf` —— **已删**（SDD 记录和 7 个冻结 DLL 搬到了主仓库的 `.superpowers\sdd\`）
- 测试：`dotnet run --project Tests/PackRulesTests/PackRulesTests.csproj` → **181 条全过**
- 构建：`build.bat` 四个配置全过，Release 产物字面量 **257（MelonLoader）/ 258（BepInEx）**
- 版本号：`0.5.0`（csproj、`ModCore.cs`、`AGENTS.md` 三处一致）

---

## 四份计划的状态

| 计划 | 内容 | 代码 | 游戏内验证 |
|---|---|---|---|
| ① 地址缓存 | RVA 缓存，消掉按名找地址 | ✅ 完成 + 整支复核 + 修复轮 | ✅ **已验证**（29/29 命中，解析 0 ms）|
| ② 启动瘦身 | 导入/注册推迟到选歌提交点 | ✅ 完成 + 整支复核 + 修复轮 | ✅ **已验证**（启动无导入，`charts loaded: 13`）|
| ③ 准备分离 | 后台导入 + 分帧校验 | ✅ Task 1–4 完成 + 逐个复核 + 修复轮 | ✅ **已验证**（选歌导入 3 ms，校验 253 ms）|
| ④ 分层装载 | 只装选歌需要的钩子 | ✅ 完成 + 过审 + **推迟层改装到第一帧** | ✅ **已验证**（启动 10,915 → 3,971 ms）|

**四个计划全部在游戏里验证过了。** 计划④ 验证时发现推迟层装错了位置（选歌的 `Awake` 里 attach
贵 6 倍），**改完又验了一次**。

---

## 状态：收尾完成（2026-11-06）

- **Release 构建的最终验收通过**，日志 `26-11-6_12-41-24.log`：

```
12:41:29.178  InFalsusChartLoader_falsum loading...
12:41:37.250  7/7 hooks installed            ← 第一层，不是 14/14
12:42:03.955  packs: 3 imported, songs: 14 imported
12:42:03.958  offsets asked=43 unresolved=0
12:42:06.554  packs: 3 registered (2 custom, 1 IFCL), songs: 14 registered
12:42:06.562  charts loaded: 14 song(s)
```

  零异常、零未解析（唯一的 WARNING 是 Il2CppInterop 自己的），`IFCL.sav` 在 12:45:01 被写过 ——
  **成绩落盘了**。Release 没有计时行和探针，所以这一版看不到耗时，只能看功能与正确性。

- **`feature/load-time-optimization` 已快进合回 `main`**（`main` = `c8bf81d`，29 个 commit）。
- **工作区已删**。SDD 记录（四个计划的简报、报告、台账、复核包）和 7 个冻结 DLL 都搬到了
  主仓库的 `.superpowers\sdd\` 下（那个目录是 gitignore 的，不进版本库）。

### 接下来可以做的（都不是必须）

1. **interop 索引 4.8 s** —— `indexed 7843 interop types by name`。缓存 29/29 全命中时它
   **可能根本不必建**，改成「第一次真要按名找时才建」可能再省 4.8 s。**这个还没验证过。**
2. **选歌剩下的 5,091 ms** —— 全在 `ModCore.Register`（名字准入 + 音频 + **建图** + 注入）。
   曲绘是里面最贵的部分，而计划③ 按 brief Step 5 选了「`ValidateSlice` 不做图」，所以建图仍然
   全压在选歌那一刻。**这是现在最大的单块开销。**
3. **计划③ 修复轮的几个 Minor**：`chart loading failed: FolderChangedWhileReading` 的措辞；
   校验在有快照期间每帧都跑（靠游戏流程保证不会跑到歌里，代码本身没强制）。

---

## 计划③ 的三个回归（2026-11-06 已跑）

会话 `26-11-6_12-27-52.log`。改动的谱面全部**逐字节还原**了。

| 回归 | 怎么做的 | 日志 | 结果 |
|---|---|---|---|
| 热加载 | 给一个 `.falsum` 旁边的 `if` 加 `"name"`，**在第一次提交之后**写 | `charts reloaded: +0 ~12 -0`；`localisation +0x30: entry 97 set to RELOAD TEST/…` | ✓ 改动一路改到游戏的本地化表 |
| 无效谱面 | 把一个根级歌的 `.falsum` 的 zip 魔数改坏 | `chart folder 'hypnotize' skipped: falsum could not be read: InvalidDataException: A local file header is corrupt.` | ✓ 那首歌没了，其余照常 |
| 整包无效 | 把只有一个歌的曲包换成 stub `if` + 一个内容是垃圾的谱面 | `prepared: 1 song(s) failed the check` → `packs: 2 registered (1 custom, 1 IFCL)` | ✓ **整个曲包消失，不是空曲包** |

**第三条走的是计划③ 新增的路径**：stub 能读，所以导入时曲包**在**（`prepared: 13 song(s) in 3 pack(s)`）；
是校验之后 `Prepare.Prune` 把歌剔掉、再把空掉的曲包剔掉 —— 日志里 3 个曲包 → 2 个，顺序看得见。

**一个记录在案的偏差**：本来打算用**独占文件锁**让谱面「读不到」（这样解码器完全碰不到垃圾数据），
但持锁的进程在游戏启动前就死了，解码器读到了那 16 个零字节并拒绝它们。
**结果一样（都是拒绝），解码器也没崩 —— 但这不是能推广的保证**：喂给解码器任意字节可能在原生层
崩，.NET 接不住。**要测这条路径，坏一个 zip 就够了，不要让解码器看垃圾。**

---

## 计划④ 验证时踩到的坑（**已修**）

计划④ 原本把推迟层装在 `Reload.Commit()` 里面 —— 那是在**选歌场景的 `Awake`** 里。
实测：

| | 装在选歌 Awake 里（`0ff9e5a`）| 装在第一帧（`c8bf81d`）|
|---|---|---|
| 每个 attach | **1,900 ms** | **320 ms** |
| 推迟层总计 | 26,944 ms | **4,576 ms** |
| 选歌界面冻结 | **34.8 秒** | **7.4 秒** |

**同一个 attach 调用，贵 6 倍。** 原因不是「游戏在跑」（标题画面时游戏也在跑，attach 照样 320 ms），
是**选歌的 Awake 正是游戏在加载场景的时候** —— 读资源、起 job 线程、最忙。

日志证据：Debug 的探针每 600 帧打一行，`song select awake` 和下一行之间**一帧都没有**，34.8 秒。

**教训**：钩子的价格不是常数，取决于装它的时候游戏在干什么。计划里「挪到那个本来就要等的界面上」
这个前提是错的 —— 选歌只等 7.6 s，装不下 27 s。

---

## 读日志的脚本

```
pwsh -File .superpowers\sdd\read-results.ps1
```

自动找最新日志，打印版本、钩子耗时、每个目标命中/未命中、导入发生在哪、`prepared:`（计划③）、
`deferred hooks`（计划④）、`address cache` 那行、异常，以及带间隔的时间线。

**看冻结**：`probe: counters` 每 600 帧打一行 —— **两行之间的时间差就是冻结**。

---

## 其他冻结的 DLL（想复现之前的测量时用）

| 文件 | 内容 | 大小 |
|---|---|---|
| `PLAN1-TASK6-Debug-8e9c7e1.dll` | 只有计划① | 243,200 |
| `PLAN2-TASK5-Debug-16dbd48.dll` | 计划①② | 244,224 |
| `PLAN1-TASK6B-Debug-161777c.dll` | 计划① + 修好 `Ms()` 单位 bug + 加了 `address cache` 那行 | 250,880 |
| `PLAN1-TASK6C-Debug-77ea45e.dll` | 上面那个 + 装载分段计时（`attach=`）| 252,928 |
| `PLAN4-TASK4-Debug-0ff9e5a.dll` | 计划①②④，**推迟层装在选歌 Awake 里（错的位置）** | 253,440 |
| `PLAN3-TASK4-Debug-3779bb8.dll` | 计划①②③④，推迟层同上 | 253,952 |
| `PLAN4-DEFER-EARLY-Debug-c8bf81d.dll` | **验证用的那一版**，计划①②③④，推迟层改装到第一帧 | 253,952 |

现在都在**主仓库**的 `.superpowers\sdd\` 下（工作区已删）。

---

## 已知未做的事（按价值排序）

1. **interop 索引 4.8 s 没动**（`indexed 7843 interop types by name`）。缓存 29/29 全命中时它**可能根本不必建** —— 改成「第一次真要按名找时才建」可能再省 4.8 s。**这个还没验证过。**
2. **选歌剩下的 5,091 ms** —— 全在 `ModCore.Register`（名字准入 + 音频 + **建图** + 注入）。曲绘是这里最贵的部分，而计划③ 按 brief Step 5 选了「`ValidateSlice` 不做图」，所以建图仍然全压在选歌那一刻。**这是现在最大的单块开销。**
3. **计划④ 的四个 Minor**（都不影响行为）：
   - `Reload.cs` 的 `catch` 按 `committed` 判断，`Diagnostics.Load` 抛异常会回滚一个已完成的提交（**早就存在**，理论上才会触发）
   - 推迟层装载抛异常会把 `Faulted` 置位，**连带停掉已经装好的第一层** —— 概率低
   - `JacketHook.cs:90-92` 的措辞说那三个钩子「全程为 0」，**是真的**但那个 0 是无效证据（那次会话没打歌），和已修正的 `Hooks.cs` 注释语气不一致
   - `if (!defer) return;` 的命名可读性
4. **计划③ 修复轮留下的几个 Minor**（都不影响行为）：
   - Release 日志里 `chart loading failed: FolderChangedWhileReading` 读起来像失败，其实是「读的时候文件夹变了」—— 措辞待改
   - 校验在「有快照」期间每帧都跑；靠游戏流程（选歌一定先于开歌）保证不会跑到歌里，**代码本身没有强制**

---

## 踩过的坑（别重犯）

- **`Ms()` 收的是「一段 tick 差」，不是时间戳。** 曾经写成 `Ms(started)`，结果日志里所有耗时都是「开机以来的毫秒数」（显示 3.3 小时），害得一次测量完全读不出来。正确写法 `Ms(Now - started)`。
- **`Diagnostics.Warn` 是 `[Conditional("DEBUG")]`，Release 里根本不存在。** 需要玩家看见的警告必须用 `WarnRelease`，并且加进 `release-allowlist.txt`。
- **`Diagnostics.Describe` 在 Release 里只返回异常类型名**，不返回 Message。所以给 Release 看的错误信息里，`new SomeExceptionType()` 的类型名就是唯一会显示的内容。
- **`const string` 拼接会在编译期折叠**，所以 `"a" + ConstB` 产生的仍然是完整字面量 —— 改 allowlist 时要按折叠后的结果写。
- **Release 产物的字符串检查是单向的**（只报「产物里有但清单里没有」），清单里多一条不会失败。
- **提交之后再开一轮准备，是白干。** 提交用的指纹就是文件夹当前的指纹；重开一轮读到的还是它。
  下次进选歌要么「没变、什么都不用做」，要么「变了、快照对不上」—— 两种都用不上这份快照。
  （计划③ 的简报里写了这一步，是简报本身错了。）
- **代码挪到帧循环里之后，「会抛异常的调用」必须当场兜住。** 解码器是游戏自己的代码，
  坏谱面能把它弄抛。以前这个抛异常只发生一次（在 commit 的 try 里），
  挪到帧循环之后就是**每帧抛一次，永远抛**。
- **钩子的价格不是常数 —— 它取决于装它的时候游戏在干什么。** 同一个 attach 调用：
  启动时 320 ms，**选歌场景加载的 Awake 里 1,900 ms**。所以「把钩子挪到游戏闲着的时候装」
  不是废话，是**必须**的 —— 而且「游戏在跑」不等于「游戏在忙」：标题画面时游戏也在跑，
  attach 照样 320 ms；真正贵的是**场景加载**那一刻。
- **量「卡了多久」要看帧，不是看日志行。** Debug 的 `probe: counters` 每 600 帧打一行，
  **两行之间的时间差就是冻结**。日志里的功能行（`charts loaded` 等）在冻结期间照样按时打，
  只看它们会以为一切正常。
- **冷热缓存能差 13 倍。** 导入 6.73 s 是**第一次**解压 `.falsum`（60 MB）的数字，缓存热了只要 523 ms。
  引用「导入要 2.8 s / 6.7 s」之前，先说清楚是冷是热。
- **PowerShell 里带 `[ ]` 的路径是雷。** `Test-Path` / `Join-Path` 会把 `[Pack]` 当**通配符**，
  于是「存在」变成 false、删除被跳过 —— 而且不报错，只是**什么都没做**。
  用 `-LiteralPath` 或直接用 .NET 的 API，而且**别硬编码目录名**：用形状找
  （「只有一个歌的曲包」这种），就不会手打错字母。
- 有些文件带 **UTF-8 BOM**，编辑工具可能把它删掉，导致 diff 变成整个文件重写。

---

## 有用的命令

```powershell
# 看当前状态
cd .superpowers\wt\load-perf
git log --oneline main..HEAD
git status --short

# 跑测试
dotnet run --project Tests/PackRulesTests/PackRulesTests.csproj

# 完整构建（四个配置都要）
$env:CI='1'; cmd /c build.bat Release --MelonLoader
cmd /c build.bat --BepInEx
cmd /c build.bat Debug
cmd /c build.bat --BepInEx Debug

# 读游戏日志
pwsh -File .superpowers\sdd\read-results.ps1
```

SDD 的过程记录（每个任务的简报、报告、复核包、台账）都在
`.superpowers\wt\load-perf\.superpowers\sdd\<计划名>\` 下，按计划分目录。
