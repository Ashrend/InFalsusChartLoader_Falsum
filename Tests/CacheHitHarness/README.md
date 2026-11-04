# The cache-hit harness

A developer tool, not a test, and not part of any build. It proves the **hit path** of
`Source/Interop/AddressCache.cs` end to end, outside the game: the cache writes a file, and the
next run answers out of that file without asking the resolver again.

It is **Windows-only**: it copies its own executable into place as `GameAssembly.dll` and loads it
with `LoadLibrary`. Nothing else in the project needs a module to load, which is why this is a
thing a developer runs by hand and not a case in `Tests/PackRulesTests`.

## What it does

The cache only works when there is a game module to record addresses against: `GameAssembly.Identity`,
`GameAssembly.SizeOfImage` and `GameAssembly.Base` all answer about the loaded `GameAssembly.dll`.
So the harness loads one:

* it copies its own executable to `bin/<config>/net6.0/fake/GameAssembly.dll` (once — the copy's
  size and last write time are what the cache keys on, so it must not change between runs),
* it loads that file with `LoadLibrary`, which makes `GetModuleHandleW("GameAssembly.dll")` find it,
* and then the real `GameAssembly` code reads a real PE header out of it.

The module is loaded **after** the first `Code()` call, so the harness also covers the case where
the cache is asked while the module is still missing: it has to stay inert and pick the module up
as soon as it is there.

Two sessions run in separate processes:

| session | what it expects | why |
|---|---|---|
| `write` | the target is resolved live (`asked=1`) and the cache file is written | nothing was cached yet |
| `hit` | the target comes out of the file (`asked=0`) | the key, the identity, the RVA and the 16-byte prologue all matched |

The target sits at RVA `0x100` — inside the module's PE headers, whose bytes are the same in
every run, so the 16-byte fingerprint matches.

## How to run it

From the repository root (the worktree this folder lives in):

```cmd
dotnet run --project Tests\CacheHitHarness\CacheHitHarness.csproj -c Debug
```

The built assembly can also be started directly with the `dotnet` host, which skips the apphost:

```cmd
dotnet build Tests\CacheHitHarness\CacheHitHarness.csproj -c Debug
dotnet Tests\CacheHitHarness\bin\Debug\net6.0\CacheHitHarness.dll
```

It prints both sessions, the cache file after each of them, and a final line:

```
PASS: the cache was written, and the next run answered out of it.
```

A non-zero exit code means a session did not behave as expected.

**It touches the mod's real cache file**, `%LOCALAPPDATA%\InFalsusChartLoader_falsum\addresses.v1`,
because that is the file `AddressCache` itself reads and writes; the harness does not redirect it.
It leaves that file exactly as it found it: saved before the run, and put back afterwards — deleted
if it did not exist before, rewritten byte for byte if it did.

## Why it is not part of `Tests/PackRulesTests`

* It needs a module to be copied and loaded with `LoadLibrary`: **Windows-only**, and it puts a
  file called `GameAssembly.dll` on the disk, which is not something a portable rule-test suite
  should do behind the player's back.
* `Tests/PackRulesTests` is the project's documented test entry point. A test that copies and
  loads a module is exactly the kind that breaks on somebody else's machine, and a test that
  breaks there is worse than no test at all.
* The rule-test suite can still pin the part that matters there: **without the game module the
  cache is inert** — it asks the caller, it never writes a file, and it never retries a target
  it could not resolve. That is `CacheIdleTests()` in `Tests/PackRulesTests/Program.cs`.

## What it does not do

It does not test the game. The real module, the real addresses and the real 40-second startup
are verified in the game, by hand.
