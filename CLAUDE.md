# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

мой форк приложения mahou - автоматическое переключение раскладки https://github.com/romannazarenko/mahou-mod
работаем над улучшением функционала
собираем в github actions

## Project

Fork of BladeMight/Mahou (GPL-2.0), a Windows keyboard-layout switcher written in C# WinForms (.NET Framework 4.8). The fork's main change replaces the whole-word `AS_dict.txt` auto-switch lookup with per-language character-trigram models (`ngram.bin`) for EN/RU/UK.

Read `docs/DEV_NOTES.md` (current state, known dead ends, debugging recipe) and `docs/NGRAM_AUTOSWITCH.md` (model design, tuning, adding a language) before changing auto-switch behaviour. `README.md` is upstream's user docs and changelog and says nothing about the fork.

## Build

The dev machine is Linux and has no .NET/MSBuild, so **all builds run in GitHub Actions** (`.github/workflows/build.yml`, `windows-latest`). They run on push to master/main, on PRs, and on manual dispatch:

1. `dotnet run --project Tools/ModelGen -c Release -- Dictionaries-origin Mahou/ngram.bin` trains the trigram models and validates them on held-out words. It **exits non-zero (failing the build) if detection drops to ≤95% or false switches reach ≥1%** for any cross-script pair.
2. `msbuild Mahou/Mahou.sln /p:Configuration=Release "/p:Platform=Any CPU"` builds `Mahou/bin/Release_x86_x64/Mahou.exe`.
3. The artifact `Mahou-Release` contains the exe, DLLs and `ngram.bin`.

If a .NET 8 SDK is available, step 1 also runs locally and is the only automated test there is. There are no unit tests. To verify app behaviour you have to run the exe on Windows.

`ngram.bin`, `bin/` and `obj/` are git-ignored. `Mahou.csproj` copies `ngram.bin` to the output only if it exists. The upstream `*.cmd` / `.vbs` build scripts in `Mahou/` target the old local Windows workflow and are not used by CI.

## Architecture

- **Entry** — `Mahou/Program.cs`: a single-instance mutex. A second launch talks to the running instance through registered window messages (`ao` = show window, `re` = restart, `gm` = set enabled state from `-on`/`-off` args).
- **`MahouUI.cs`** (~7k lines): the main form and also the global settings hub. Most feature flags are public static fields on `MahouUI` that are read everywhere, and `MahouUI.ENABLED` gates all input processing. `MahouUI.Designer.cs` is the generated WinForms layout.
- **Input pipeline**:
  - Keystrokes arrive via Raw Input (`Classes/RawInputForm.cs` → `KMHook.ListenKeyboard`). Raw Input can only observe keys.
  - `Classes/LLHook.cs` (low-level hook) is the only place that can suppress a key, by returning `(IntPtr)1`.
  - `Classes/jklXHidServ.cs` integrates the JKL helper (native C in `JKL/`).
- **`Classes/KMHook.cs`** (~4.7k lines): the core logic.
  - It tracks the current word (`c_snip`) and handles conversion (`ConvertSelection`, `StartConvertWord`), snippets and auto-switch.
  - Auto-switch runs as follows: the trigger block on Space/Enter calls `CheckAutoSwitch`, which tries the `AS_dict.txt` rules first and falls back to `NgramScorer`. `DoAutoSwitch` then performs the action. The models load in `ReInitSnippets`, and only when AutoSwitch is enabled.
- **`Classes/NgramScorer.cs`**: pure C# with no WinForms. `Tools/ModelGen` compiles this same file, so the trainer and the runtime share one on-disk format. Keep it free of WinForms and Mahou-UI dependencies.
- **Layouts**: `Classes/Locales.cs` enumerates them. `WordGuessLayout` only considers `MAIN_LAYOUT1/2` when "switch between layouts" is on.
- **Config**: `Classes/Configs.cs` (INI wrapper). `Mahou.ini` lives next to the exe, or in `%AppData%\Mahou` when the exe folder isn't writable. `MahouUI.nPath` is that user-data location.

## Fork conventions and pitfalls

- Resolve **shipped** files (such as `ngram.bin`) from `AppDomain.CurrentDomain.BaseDirectory`. Treat `nPath` only as the user-override location, since it can point to `%AppData%`. Getting this wrong once broke n-gram loading silently.
- **Self-update is disabled on purpose**, because it would overwrite the fork with stock Mahou. Only the `Controls.Add` lines and the startup check were removed, and the updater methods were left in place to keep upstream merges clean. Don't re-add those lines when merging upstream.
- Keep diffs against upstream minimal in `MahouUI*.cs` and `KMHook.cs`, so that upstream merges stay manageable.
- **Browser-search Enter** (the page submits the word before auto-correction lands) is unsolved. Commit 9b0a1b3, which suppressed Enter in LLHook and re-emitted it, was reverted because it regressed layout selection. Don't reapply it. See DEV_NOTES first.
- To debug auto-switch, enable logging and read `<nPath>/Logs/<date>.txt`, looking for `[AS] > n-gram …` and `[Locales] Final layouts` lines.
- Tunables: `NgramScorer.Threshold` (1.0) and `MinLength` (3). To add a language, add a word list in `Dictionaries-origin/` and a row in the `Langs` table in `Tools/ModelGen/Program.cs` (primary LCID, file, physical key row).
