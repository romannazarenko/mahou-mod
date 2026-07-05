# Dev notes — resuming later

Last Updated: 2026-07-05

Personal continuation notes for the n-gram auto-switch fork of Mahou.

## Where things stand

Working state = current `master` HEAD. Fork of BladeMight/Mahou (GPL-2.0) that
replaces the full-word `AS_dict.txt` lookup with per-language character trigram
models for the auto-switch decision.

**Works:**
- N-gram layout detection (EN/RU/UK), ~99% detection, ≤0.02% false switches.
  Handles inflected forms and neologisms the word list never had.
- Auto-switch triggers on **Space** and on **Enter** in normal text fields
  (editors, most inputs).
- `AS_dict.txt` still wins as an explicit user-rules/exceptions layer; n-gram is
  the fallback. If `ngram.bin` is missing, behaves exactly like upstream.

**Not solved:**
- Enter in a **browser search box** does not auto-correct: the page submits the
  wrong-layout word before the correction lands. Root cause is architectural —
  see below.

## Game mode (exe arg)

The `-game` flag (also `--game`, `/game`, `-g`, `/g`) explicitly **sets** whether Mahou
intercepts input — it is not a toggle:

- **With `-game`** → interception OFF (game mode), Mahou never touches keystrokes.
- **Without any flag** → interception ON (normal operation).

Behaviour depends on whether Mahou is already running (single-instance mutex):

- **Not running** → a `-game` launch boots in the disabled state (`Program.cs` detects
  the flag via `IsGameModeArg` after `mahou`/`rif` are constructed and calls
  `MahouUI.ToggleMahou()`); a plain launch boots enabled as usual.
- **Already running** (the usual autostart case) → the second launch does NOT start a
  new process; it broadcasts the registered window message `ToggleGameModeMahou!`
  (`MMain.gm`, mirrors `ao`/`re`) with `WParam` = desired ENABLED state (0 = disable,
  1 = enable) and exits. The live instance handles it in `WndProc`: if the current
  state differs it calls `ToggleMahou()`, so the message is idempotent (re-sending the
  same state is a no-op). A plain launch also posts enable=1 before the usual
  show-window (`ao`). Net effect: run `Mahou.exe -game` before a game, `Mahou.exe`
  after — deterministic, no restart, no guessing the current state.

Note this makes a plain no-flag re-launch always re-enable interception (even if it was
manually disabled via tray/hotkey) — intended per the explicit set semantics.

`ToggleMahou()` unregisters the hooks + raw-input devices, stops timers, and marks the
tray `[Disabled]`. Why disabling is enough: every hook entry (`LLHook.cs`,
`RawInputForm.cs`, `jklXHidServ.cs`) early-returns on `!MahouUI.ENABLED`, and raw-input
devices are unregistered, so keystrokes pass straight through.

## The browser-Enter problem (dead end so far)

Mahou reads the keyboard via **Raw Input** (`RawInputForm.cs` → `KMHook.ListenKeyboard`).
Raw Input only *notifies*; it cannot suppress a key. So the physical Enter reaches
the app (submits the form) before the async correction retypes the word. In a text
editor this is invisible (Enter just makes a newline); in a search box it submits
the wrong word.

Only the **low-level hook** (`LLHook.Callback`, returns `(IntPtr)1`) can suppress.

**Attempt that was reverted (commit 9b0a1b3, reverted by 01f75c6):** suppress the
Enter keydown in `LLHook`, let `ListenKeyboard` correct the word, then re-emit Enter
via `DoSelf` (which detaches raw input + LL hook so the synthetic key reaches the app
without re-triggering). It compiled but in the browser it did **not** work correctly,
and it also caused a "corrected word inserted in the wrong layout" regression. Rolled
back. If retrying: first find out *why* it misbehaves in-browser before re-adding —
likely the synthetic Enter fires before the corrected text is actually committed to
the field, or focus/IME interference. Consider gating on a confirmation that the
field text was replaced, or a per-app strategy, rather than a blind re-emit.

Do NOT just reapply the reverted commit — it regressed layout selection.

## Key files

- `Mahou/Classes/NgramScorer.cs` — `NgramModel` (train/score/serialize) and
  `NgramScorer` (load, `ShouldSwitch`, persistence). Pure C#, no WinForms — links
  into the test/tool project unchanged. Tunables: `NgramScorer.Threshold` (1.0),
  `MinLength` (3).
- `Tools/ModelGen/` — offline trainer/validator. Reads `Dictionaries-origin/`,
  validates detection/false-switch on held-out words, writes `ngram.bin`. Exits
  non-zero on regression.
- `Mahou/Classes/KMHook.cs` — integration:
  - `CheckAutoSwitch` — decision (AS_dict loop first, then n-gram fallback).
  - `DoAutoSwitch` — shared switch action (backspace + StartConvertWord +
    ExpandSnippet + ChangeToLayout). Used by both paths.
  - Trigger block (~line 665) — Space/Enter gate; `asEnterSnip` snapshots the word
    on Enter because Enter's `ClearWord` wipes `c_snip` first.
  - Model load in `ReInitSnippets` when AutoSwitch is enabled.
- `docs/NGRAM_AUTOSWITCH.md` — design, tuning table, how to regenerate models,
  how to add a language.

## Build

No local .NET on this machine (Alpine). Build runs in GitHub Actions
(`.github/workflows/build.yml`, windows-latest): trains + validates models, then
MSBuild of `Mahou/Mahou.sln`, artifact `Mahou-Release` (contains exe + `ngram.bin`).
Fork retargeted .NET Framework v4.0 → v4.8 (runners lack net40 reference assemblies).
`ngram.bin` is git-ignored and produced by CI.

To build/test the models locally if a .NET SDK is available:

```
dotnet run --project Tools/ModelGen -- Dictionaries-origin Mahou/ngram.bin
```

## Possible next steps

- Browser-Enter: investigate the timing/commit issue above; likely needs a
  confirmation-based or per-app approach.
- Expose `Threshold` / `MinLength` in the UI settings.
- Add more languages (word list + a row in `Tools/ModelGen/Program.cs` `Langs` +
  its physical key row).
- Short-word reliability (len < `MinLength`) is left to the dictionary; could add a
  small curated exception list.
