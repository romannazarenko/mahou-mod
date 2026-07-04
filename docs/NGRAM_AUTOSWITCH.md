# N-gram auto-switch

Last Updated: 2026-07-04 · v0.1

Replaces the full-word `AS_dict.txt` lookup with per-language **character trigram
models**. Instead of "is this exact word in the dictionary?", the detector asks
"do these keystrokes read like a real word in the other layout's language?". This
generalises to inflected forms, slang and neologisms the word list never contained.

## Why

`AS_dict.txt` is ~150k whole-word pairs, scanned linearly on every space press, and
misses every form not explicitly listed (`собака` yes, `собаками` no). The trigram
models are ~200 KB total, score in O(word length), and cover unseen forms.

## How it works

For a typed word, `WordGuessLayout` already produces its reading in the other layout
(`sncl`) and the layout it was typed in (`snl`). The detector then compares:

```
score = dstModel.Score(sncl) − srcModel.Score(typed)
switch if score > Threshold
```

`Score` is the average per-character log-probability under an add-0.1-smoothed
trigram model with `^` word boundaries. Characters that cannot be typed in a
layout get a heavy penalty, so a genuine word in the current layout scores far
higher there than its cross-layout reading.

Models are keyed by primary language id (`layoutId & 0xFFFF & 0x3FF`), so any
installed layout of a trained language is covered.

## Priority

1. `AS_dict.txt` (if present) — explicit user rules and exceptions win.
2. Trigram models — statistical fallback.

If `ngram.bin` is missing the app silently behaves exactly as upstream (dict only).

## Tuning (`NgramScorer`)

| Field | Default | Effect |
|-------|---------|--------|
| `Threshold` | `1.0` | Log-likelihood advantage required to switch. Lower = more eager. |
| `MinLength` | `3` | Words shorter than this are left to the dictionary. |

Measured on 5 000 held-out words per language (add trigrams never trained on):

| Pair | Detection | False switch of real words |
|------|-----------|----------------------------|
| EN typed in RU | 99.2 % | 0.02 % |
| RU typed in EN | 99.2 % | 0.00 % |
| UK ⇄ EN | ~98.8 % | 0.00 % |

## Regenerating models

Models are built in CI from the word lists in `Dictionaries-origin/`:

```
dotnet run --project Tools/ModelGen -- Dictionaries-origin Mahou/ngram.bin
```

The tool validates detection/false-switch and exits non-zero on regression, so a
bad model fails the build. `ngram.bin` is generated (git-ignored) and bundled next
to `Mahou.exe`.

To add a language: add its word list, a row in `Tools/ModelGen/Program.cs` `Langs`
(primary LCID, file, key row), and the matching physical key row.

## Files

- `Mahou/Classes/NgramScorer.cs` — `NgramModel` (train/score/serialize) and
  `NgramScorer` (load, `ShouldSwitch`, persistence).
- `Tools/ModelGen/` — offline trainer/validator.
- `Mahou/Classes/KMHook.cs` — `CheckAutoSwitch` (decision) and `DoAutoSwitch`
  (shared switch action), model load in `ReInitSnippets`.
