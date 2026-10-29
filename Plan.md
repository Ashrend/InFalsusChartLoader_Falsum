# Custom Pack Cover Row Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a custom pack's `pack_cover.png` appear on the story-card, song/DLC-card, outline, and backing-image surfaces by filling only that custom pack's `SongSelectPackAssets` row.

**Architecture:** Keep the shipped-row-copy approach in `PackSetup`. A custom pack gets a copied `SongSelectPackAssets` row; `ApplyCover` replaces the material fields in that row with clone-of-original material instances using the custom cover texture. Do not mutate global `PackSelectSceneAssets` fields and do not add visual-member hooks for cover application. The game then reads the custom row normally.

**Tech Stack:** .NET 6, C# unsafe, MelonLoader, IL2CPP interop, game `Game.dll`/`Game.Data.dll`, existing `JacketFactory` and `BackgroundClone` helpers.

**Repository note:** `AGENTS.md` says not to commit unless the user explicitly asks. Do not run `git commit` as part of this plan.

---

## Current State

- Project root: `C:\Users\Ashrend\Desktop\INF\InFalsusChartLoader-MelonLoader-Falsum`
- Current version: `0.3.0`.
- Category support is already implemented:
  - `Source/Charts/PackManifest.cs`
  - `Source/Charts/PackReader.cs`
  - `Source/Charts/SongCatalog.cs` calls `PackSetup.ApplyCategories(...)`
  - `pack` / `category` / `type` accepts `story` and `song`.
- `PackRulesTests` currently has 21 passing tests.
- Current cover code:
  - `Source/Charts/PackSetup.cs` copies a shipped visual row and calls `ApplyCover`.
  - `ApplyCover` only covers `CoverMaterialFields`.
  - `Source/Interop/Hooking/Detours/PackVisualHook.cs` contains experimental cover hooks and a `CoversEnabled = false` switch. Current game is stable with covers disabled.
- Test pack:
  - `D:\SteamLibrary\steamapps\common\In Falsus\Charts\[Pack]TouhouProject\pack`
  - currently has `"pack": "song"`.
  - `pack_cover.png` is a green test cover; replace it only after verification.
- Game must be launched through Steam:
  - `steam://rungameid/3971950`
- Logs:
  - `D:\SteamLibrary\steamapps\common\In Falsus\MelonLoader\Logs`

## Evidence Already Established

The real per-pack visual resources come from `PackSelectSceneAssets.packToAssets[row]`, not from the initial `selectedVisual`/`backingVisual` fields and not from global `UnownedBacking`.

Verified from `26-10-28_21-3-36.log`:

- DLC-01 row:
  - `LargeNonCompleted`: `packvisual-songselect-dlc-01v1-default`
  - `BackingNonCompleted`: `backingimage-dlc-01v1`
  - `OutlineNonCompleted`: `outline-packvisual-DLC-01-VirtualSingers01`
  - `UnownedBacking`: `backingimage-dlc-01v1-notowned`
  - `UnownedHubDefault`: `dlc-bannervisual-hub-01v1-VirtualSingers01`
- DLC-02 row:
  - `LargeNonCompleted`: `packvisual-songselect-dlc-02-default`
  - `BackingNonCompleted`: `backingimage-dlc-02`
  - `OutlineNonCompleted`: `outline-packvisual-DLC-02-Chunithm`
  - `UnownedBacking`: `backingimage-dlc-02-notowned`
  - `UnownedHubDefault`: `dlc-bannervisual-hub-02-Chunithm`

`PackDLCVisualMemberSingle` was not called in the observed song/DLC screen. The active members are `PackVisualMemberLarge`, `PackVisualMemberSmall`, and `PackOutlineMember`.

The earlier failures happened because the code changed global defaults or initialization fields. The fix must be limited to the custom row.

---

## File Structure

- Modify: `Source/Charts/PackSetup.cs`
  - Add the missing per-row material fields.
  - Keep `ApplyCover` as the single place that writes cover materials.
- Modify: `Source/Interop/Hooking/Detours/PackVisualHook.cs`
  - Remove experimental cover hooks and global overrides.
  - Keep the row accessor and difficulty behavior.
- Modify: `PACK.EXAMPLE.md` and/or `README.md`
  - Document `pack`/`category` values and cover behavior.
- Test: `Tests/PackRulesTests/Program.cs`
  - Existing manifest/category tests remain; no native visual unit test is possible without the game runtime.
- Check: `release-allowlist.txt`
  - Add any new Release strings introduced by this work.

---

### Task 1: Add the missing row material fields

**Files:**
- Modify: `Source/Charts/PackSetup.cs:144-164` (`CoverMaterialFields`)
- Verify: `Source/Charts/PackSetup.cs:114-141` (`ApplyCover`)

- [ ] **Step 1: Replace the field list**

Change the array to include the row fields that were proven to hold the real DLC cover/backing/outline resources. Keep the existing fields.

```csharp
        private static readonly string[] CoverMaterialFields =
        {
            "SongSelectLargeNonCompleted",
            "SongSelectLargeNonCompletedHover",
            "SongSelectLargeCompleted",
            "SongSelectLargeCompletedHover",
            "PackSelectLargeNonCompleted",
            "PackSelectLargeNonCompletedHover",
            "PackSelectLargeCompleted",
            "PackSelectLargeCompletedHover",
            "PackSelectNonCompleted",
            "PackSelectNonCompletedHover",
            "PackSelectCompleted",
            "PackSelectCompletedHover",
            "BackingImageNonCompleted",
            "BackingImageCompleted",
            "OutlineNonCompleted",
            "OutlineCompleted",
            "UnownedBacking",
            "DLCSmallDefaultBacking",
            "DLCSmallHoverBacking",
            "UnownedSmallDefaultBannerVisual",
            "UnownedSmallHoverBannerVisual",
            "UnownedHubDefaultBannerVisual",
            "UnownedHubHoverBannerVisual",
        };
```

- [ ] **Step 2: Confirm `ApplyCover` writes only the target row**

Read `ApplyCover` and confirm it does this for every field:

```csharp
IntPtr source = Memory.Ptr(sourceRow + offset);
IntPtr material = Memory.LooksLikeObject(source)
    ? BackgroundClone.Build(source, cover, out reason)
    : cover;
Memory.WritePtr(targetRow + offset, material);
```

Do not change this to write `assets + offset` or any global field.

- [ ] **Step 3: Build Debug**

Run:

```powershell
dotnet build InFalsusChartLoader_falsum.csproj -c Debug --nologo -p:Loader=MelonLoader
```

Expected: `0 个错误` (0 errors). Warnings about the temporary diagnostic code are acceptable until Task 2.

- [ ] **Step 4: Confirm the field names exist in the current game**

Run:

```powershell
$dll = "D:\SteamLibrary\steamapps\common\In Falsus\MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL\cpp2il_out\Game.Data.dll"
& "C:\Users\Ashrend\.dotnet\tools\ilspycmd.exe" -t ifapp.Game.Data.PackSelectSceneAssets $dll |
  Select-String "BackingImageNonCompleted|BackingImageCompleted|OutlineNonCompleted|OutlineCompleted|UnownedBacking"
```

Expected: all five names appear as fields. If any is missing, do not invent an offset; report the missing field instead.

---

### Task 2: Remove experimental cover hooks and global overrides

**Files:**
- Modify: `Source/Interop/Hooking/Detours/PackVisualHook.cs`
- Modify: `Source/Interop/Hooking/Detours/PackProbeHook.cs` only if it still references removed symbols.

**Goal:** The only cover path is `PackSetup.ApplyCover` writing the custom row. `PackVisualHook` must not call `ApplyLargeCover`, `ApplySmallCover`, `ApplyOutlineCover`, `ApplyDlcCover`, `TryOverrideLargeBacking`, or `TryOverrideSmallBacking`.

- [ ] **Step 1: Remove the `CoversEnabled` switch**

Delete:

```csharp
        private const bool CoversEnabled = false;
```

- [ ] **Step 2: Keep the row accessor redirect behavior**

In `PackRowDetour`, retain this behavior, but remove the `CoversEnabled` condition:

```csharp
            if (SongCatalog.PacksById.TryGetValue((int)index, out ChartPack pack) &&
                !pack.HasCustomCover)
            {
                if (_rowSource < 0) _rowSource = ResolveSourceRow();
                if (_rowSource >= 0)
                {
                    index = (uint)_rowSource;
                }
            }
```

A pack with `HasCustomCover == true` must use its own patched `pack.VisualRow`. A pack without a cover uses the shipped source row.

- [ ] **Step 3: Remove visual-cover hook installation**

Edit `InstallPackVisual` so it does not call any of these:

```csharp
InstallPackLargeCover();
InstallPackHover();
InstallPackSmall();
InstallPackSmallHover();
InstallPackSmallState();
InstallPackOutline();
InstallPackDlc();
InstallPackDlcUpdate();
```

The remaining installation logic must keep:

```csharp
bool row = InstallPackRow();
```

and the existing `PackVisualMemberLarge._vc` difficulty hook. The expected final shape is:

```csharp
        private static bool InstallPackVisual()
        {
            bool row = InstallPackRow();

            IntPtr info = MethodResolver.MethodInfoByRuntime(
                "PackSongCardMember", "_pc", PackImage, PackNamespace);
            if (info == IntPtr.Zero)
            {
                Diagnostics.Warn("PackSongCardMember._pc could not be found; a difficulty change on the " +
                                 "pack screen will not reach the jackets until the screen is reloaded");
            }
            else
            {
                _buildCardInfo = info;
                _buildCard = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)(*(IntPtr*)info);
            }

            IntPtr target = MethodResolver.ByRuntime(
                "PackVisualMemberLarge", "_vc", PackImage, PackNamespace);
            if (target == IntPtr.Zero)
            {
                Diagnostics.Warn("PackVisualMemberLarge._vc could not be found; the jackets on the pack " +
                                 "screen will keep the difficulty they were first built with");
                return false;
            }

            byte[] prologue = Prologue(target);
            _packDifficulty = new NativeHook<PackDifficultyFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, byte, IntPtr, void>)
                    &PackDifficultyDetour,
            };
            _packDifficulty.Attach();
            _packDifficultyTramp = _packDifficulty.Trampoline;
            Diagnostics.Info("PackVisualMemberLarge._vc hooked");
            return Landed(Hook.PackVisual, target, prologue) & row;
        }
```

- [ ] **Step 4: Remove the no-longer-used cover hooks and helpers**

Remove these methods and their backing delegates/fields:

```text
InstallPackLargeCover
PackLargeEfDetour
TryOverrideLargeBacking
ApplyLargeCover
SetConstrainedMaterial
ApplyConstrainedMaterial
InstallPackHover
PackHoverDetour
InstallPackSmall
PackSmallDetour
TryOverrideSmallBacking
InstallPackSmallHover
PackSmallHoverDetour
InstallPackSmallState
PackSmallStateDetour
ApplySmallCover
ApplyConstrainedArray
InstallPackOutline
PackOutlineDetour
ApplyOutlineCover
InstallPackDlc
PackDlcDetour
InstallPackDlcUpdate
PackDlcUpdateDetour
ApplyDlcCover
```

Also remove the associated `NativeHook<T>` and trampoline fields. Keep `PackDifficultyFn`, `_packDifficulty`, and `_packDifficultyTramp`.

- [ ] **Step 5: Simplify detachment**

`DetachPackLargeCover` must no longer reference removed hooks. `DetachPackVisual` must only detach the row accessor and difficulty hook:

```csharp
        private static void DetachPackVisual()
        {
            _packRow?.Detach();
            _packRow = null;
            _packRowTramp = null;

            _packDifficulty?.Detach();
            _packDifficulty = null;
            _packDifficultyTramp = null;
            _buildCard = null;
            _buildCardInfo = IntPtr.Zero;
        }
```

- [ ] **Step 6: Build and check for unresolved symbols**

Run:

```powershell
dotnet build InFalsusChartLoader_falsum.csproj -c Debug --nologo -p:Loader=MelonLoader
```

Expected: `0 个错误`. Search the file for removed names:

```powershell
Select-String -Path Source\Interop\Hooking\Detours\PackVisualHook.cs -Pattern "CoversEnabled|ApplyLargeCover|ApplySmallCover|ApplyOutlineCover|ApplyDlcCover|UnownedBacking override|large default visual override"
```

Expected: no output.

---

### Task 3: Validate the row-level cover in game

**Files:**
- No source change expected.
- User installs the built DLL manually, per `AGENTS.md`.

- [ ] **Step 1: Build Debug**

```powershell
dotnet build InFalsusChartLoader_falsum.csproj -c Debug --nologo -p:Loader=MelonLoader
```

Expected: `0 个错误`.

- [ ] **Step 2: Ask the user to install and launch**

Ask the user to copy:

```text
bin\Debug\InFalsusChartLoader_falsum.dll
```

to:

```text
D:\SteamLibrary\steamapps\common\In Falsus\Mods\InFalsusChartLoader_falsum.dll
```

Then launch with Steam:

```text
steam://rungameid/3971950
```

- [ ] **Step 3: Check the story-column surface**

Ask the user to select the custom pack in the story column and stay on the pack screen.

Expected:

- The large selected card shows the custom cover.
- The backing image/character base behind the card no longer shows the shipped default.
- The right-side progress/outline card shows the custom cover.
- No crash and no green test cover in an unrelated region.

- [ ] **Step 4: Check the song/DLC-column surface**

Ask the user to change the test pack to `"pack": "song"` temporarily, restart, and select it in the song/DLC column.

Expected:

- The large song/DLC card shows the custom cover.
- The backing image is the custom cover, not `backingimage-*`.
- The small card shows the custom cover.
- The game remains stable when entering and leaving the page.

Restore the user's intended final category after the test.

- [ ] **Step 5: Inspect the startup log**

Run:

```powershell
$log = Get-ChildItem "D:\SteamLibrary\steamapps\common\In Falsus\MelonLoader\Logs" -Filter *.log |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1
Select-String -LiteralPath $log.FullName -Pattern "with custom cover|visual row|unresolved|exception|Hook"
```

Expected:

- `pack 'TouhouProject' visual row ... with custom cover`
- no hook unresolved errors
- no unhandled exception around the pack screen

If the backing image is still shipped, do not reintroduce global `UnownedBacking` mutation. Inspect the custom row's `BackingImageNonCompleted` value and compare it with the evidence row above.

---

### Task 4: Remove temporary diagnostics and finalize

**Files:**
- Modify: `Source/Interop/Hooking/Detours/PackVisualHook.cs`
- Modify: `README.md` / `PACK.EXAMPLE.md`
- Check: `release-allowlist.txt`
- Test: `Tests/PackRulesTests/Program.cs`

- [ ] **Step 1: Remove diagnostic-only resource probes**

Remove the DEBUG-only helpers added during investigation, including:

```text
ObjectName
ResourceName
VisualMaterial
ResourcePack
LogLargeResources
LogSmallResources
LogOutlineResources
LogDlcResources
LogRowMaterials
LogMaterialArray
```

Also remove their `_resLargeLogs`, `_resSmallLogs`, `_resOutlineLogs`, `_resDlcLogs`, `_objectGetName`, and `_objectGetNameResolved` fields if they are now unused.

Do not remove ordinary `Diagnostics.Info` messages that describe a real fallback or hook installation.

- [ ] **Step 2: Update user documentation**

In `PACK.EXAMPLE.md`, document:

```json
{
  "name": {
    "en": "Example Pack"
  },
  "pack": "song"
}
```

Add a sentence that `pack` accepts `story` or `song`, and that `pack_cover.png` is applied to the custom pack's visual row.

If `README.md` lists pack manifest keys, add the same information there.

- [ ] **Step 3: Remove the green test cover from the final package**

Replace `Charts\[Pack]TouhouProject\pack_cover.png` with the intended cover, or remove the test cover if the user does not want a custom image. Do not ship the green diagnostic image.

- [ ] **Step 4: Run the rule tests**

```powershell
dotnet run --project Tests/PackRulesTests/PackRulesTests.csproj
```

Expected: all 21 tests pass.

- [ ] **Step 5: Build Release and run the project's artifact check**

```powershell
build.bat
```

Expected:

- Release build succeeds.
- The generated artifact check passes.
- If it fails on new strings, add only the required diagnostic/UI strings to `release-allowlist.txt`, then rerun `build.bat`.

- [ ] **Step 6: Final game regression**

Ask the user to install the Release DLL and verify:

- story column custom pack cover;
- song/DLC column custom pack cover;
- story outline card;
- small card;
- category switch between story and song;
- entering/leaving the pack screen does not crash;
- an official pack still uses its shipped art.

---

## Out of Scope

- Do not modify the Python converter.
- Do not rewrite `PackDLCVisualMemberSingle`; evidence shows it is not used by the observed song/DLC screen.
- Do not alter global `PackSelectSceneAssets` fields.
- Do not change note/chart decoding, audio, or background-video behavior.
- Do not add commit/push steps without an explicit user request.
