# Upstream v0.3.0 Merge Plan

> Status: not merged. The attempted merge produced architectural conflicts and was stopped.
> Local project is based on upstream v0.2.2; upstream v0.3.0 adds the HotReload/single-custom-pack architecture.

**Goal:** Preserve the local multi-pack implementation while selectively taking upstream v0.2.2 → v0.3.0 fixes and, if desired, adapting the HotReload work.

**Architecture:** The local fork and upstream diverged in the core pack model. Upstream v0.3.0 has one custom pack plus a reload path; local has multiple custom packs, categories, ownership, and per-pack visual rows. Do not resolve this as a plain text merge.

**Tech Stack:** .NET 6, C# unsafe, MelonLoader/BepInEx, IL2CPP interop, Git.

---

## Current Facts

- Local directory: `C:\Users\Ashrend\Desktop\INF\InFalsusChartLoader-MelonLoader-Falsum`
- Local Git state: no `.git` directory.
- Local source baseline: upstream `v0.2.2` (`e0e3686` / tag `v0.2.2`).
- Upstream: `https://github.com/Its-MagicNeko/InFalsusChartLoader`
- Fork: `https://github.com/Ashrend/InFalsusChartLoader_Falsum`
- Upstream `main`: `6ca7a92` / `v0.3.0`
- Fork `main`: `84f3ffd`, which already contains upstream `v0.3.0` plus README commits.
- The upstream `v0.2.2..v0.3.0` diff touches 31 files:
  - new HotReload files: `Source/Charts/Reload.cs`,
    `Source/Interop/Hooking/SongSelectRefresh.cs`,
    `Source/Interop/Hooking/PackScreenRebuild.cs`,
    `PackListHook`, `PackSelectHook`, `HubHook`, `ChartStartHook`
  - changed core: `SongCatalog`, `ChartLibrary`, `ModCore`, `CustomResults`,
    `StreamingAssets`, `FieldResolver`, `Hooks`, `PackVisualHook`
- The local fork already has the multi-pack implementation and should not be reduced to
  upstream's single `CustomPackId`.

## Attempted Merge

A temporary 3-way merge was performed in:

`C:\Users\Ashrend\AppData\Local\Temp\opencode\InFalsus-upstream-sync`

The merge has 21 conflicted files. After normalizing the namespace difference, the remaining
architectural conflicts are:

- `Source/Charts/ChartLibrary.cs`
  - local: `Import(...) -> List<ChartPack>`
  - upstream: `Import(...) -> List<ChartInfo>` with a `skipped` list for reload
- `Source/Charts/SongCatalog.cs`
  - local: multiple custom packs, pack insert/append, categories, ownership, visual rows
  - upstream: one `CustomPackId`, `SetupEmptyPack`, `SetPackSongs`, reload/refresh helpers
- `Source/Mod/ModCore.cs`
  - local: import packs, inject packs/songs, extend visuals
  - upstream: reload and hot-reload flow
- `Source/Interop/Hooking/Detours/PackVisualHook.cs`
  - local: pack row accessor, visual row extension, difficulty hooks
  - upstream: newer row/hook integration
- `build.bat`
  - local: external `release-allowlist.txt` support and additional strings
  - upstream: expanded built-in allowlist and hook/test strings

Do not commit this merge until one of the strategies below is chosen.

## Strategy Decision

### Strategy A — Keep local multi-pack architecture (recommended for now)

- Treat local as source of truth.
- Port only upstream commits that do not require the single-pack/reload architecture.
- Defer HotReload until the multi-pack wrapper is designed.
- Smallest risk to existing custom pack, category, ownership, and `.falsum` behavior.

### Strategy B — Rebase local features onto upstream v0.3.0

- Start from upstream v0.3.0.
- Reimplement multi-pack, category, ownership, and visual-row support on top of the upstream
  HotReload/refresh model.
- Larger change, but resulting architecture follows future upstream work more closely.

### Strategy C — Documentation/build-only sync

- Merge only README/build/test infrastructure changes.
- Leave all C# source untouched.
- Not recommended if upstream runtime fixes are required.

**Recommended immediate action:** push the current local state to a fork branch, then execute
Strategy A in a dedicated branch. Revisit Strategy B only if HotReload is a required feature.

## Publishing the Current Local State

Use a new branch, not `main`, until the user confirms the desired final branch.

- Branch: `local-multipack`
- Source: current local directory
- Base: fork `main` (or a commit based on `v0.2.2`)
- Do not commit `bin/`, `obj/`, or generated `Managed` binaries.
- Include `UPSTREAM_MERGE_PLAN.md`, `Plan.md`, source, tests, and docs.

Suggested commands after Git initialization/checkout:

```powershell
git checkout -B local-multipack
git add -A
git commit -m "feat: add multi-pack loader, categories, ownership and row handling"
git push -u origin local-multipack
```

## Strategy A Task List

### Task 1: Freeze and publish local source

- Create `local-multipack` branch from the fork.
- Commit the current local state.
- Push the branch.
- Tag/record the exact baseline commit.

### Task 2: Build a feature diff matrix

For every upstream changed file, classify:

- local-only feature
- upstream-only fix
- both changed
- safe to auto-merge

Store the matrix in this plan or a separate `docs/upstream-0.3.0-matrix.md`.

### Task 3: Port non-architectural upstream fixes

Candidate order:

1. `VideoBackground`, `BackgroundCatalog`, `Selection` — take upstream changes unless local
   behavior differs.
2. `FieldResolver`, `MethodResolver`, `InteropTypeIndex` — take upstream lookup improvements.
3. `AudioCatalog`, `CustomResults`, `StreamingAssets` — merge local custom-song paths with
   upstream reset/reload behavior.
4. `Hooks`, `ChartStartHook`, `SceneProbeHook`, `SongSelectHook` — add upstream hook fixes
   without replacing local pack hooks.
5. `build.bat` — combine the local external allowlist with the upstream literal list.

Do not touch `SongCatalog`, `ChartLibrary`, `ModCore`, or `PackVisualHook` until Task 4.

### Task 4: Define the multi-pack reload adapter

Design before editing:

- `ChartLibrary.Import` must keep returning `List<ChartPack>` for the local loader.
- If HotReload is ported, add a separate chart-list extraction path:
  - keep pack ownership/name/category metadata in `ChartPack`;
  - expose `List<ChartInfo>` to upstream reload code;
  - map refreshed songs back to their owning `ChartPack`.
- `SongCatalog` must keep:
  - `PacksById`
  - `CustomPackIds`
  - multiple pack rows
  - `ApplyCategories`
  - ownership `_UkA(id,true)`
  - append-only pack insertion and visual-row allocation
- Adapt upstream `Reload`/`SongSelectRefresh` to call the multi-pack adapter instead of a
  single `CustomPackId`.

### Task 5: Resolve core conflicts one file at a time

Order:

1. `ChartLibrary`
2. `SongCatalog`
3. `ModCore`
4. `PackVisualHook`
5. remaining hook files

For each:

- keep local multi-pack behavior;
- add upstream feature behind a small adapter;
- build after each file;
- do not bundle unrelated refactors.

### Task 6: Verification

- `dotnet build ... -c Debug`
- `dotnet run --project Tests/PackRulesTests/PackRulesTests.csproj`
- `build.bat` Release artifact check with combined allowlist
- Unity game smoke test:
  - native DLC1/DLC2 name, preview, selected state
  - multiple custom packs
  - story/song categories
  - ownership
  - `.falsum` and `if`
  - HotReload only if ported
- Confirm no visual-row collision and no native pack list shifting.

## Non-Goals for This Merge

- Do not overwrite the fork `main` with a conflicted or unverified tree.
- Do not silently drop local multi-pack code.
- Do not claim full upstream compatibility until the game smoke test passes.
