# Recall — working notes for agents

Read this before touching the code. It carries the decisions that are not obvious from
reading a single file.

## What this is

A native Windows flashcard app. WPF on .NET 10, no web layer, no AI at runtime, no account,
no sync. One JSON file on disk holds everything.

## Commands

```powershell
dotnet build src\Recall.App\Recall.App.csproj          # app
dotnet test tests\Recall.Tests\Recall.Tests.csproj     # 113 tests, all pass
dotnet run --project src\Recall.App                    # launch from source
```

Publish (this is what the GitHub release ships):

```powershell
dotnet publish src\Recall.App\Recall.App.csproj -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

If `src\Recall.App\bin\Debug\net10.0-windows\Recall.exe` is locked, the app is running.
Kill it, or build to a temp `-o` folder instead of deleting the output.

## Layout

```
src/Recall.Core     models, store, SRS, answer matching, quiz building, PDF import
src/Recall.App      WPF shell, views, view models, themes
tests/Recall.Tests  xunit over the core
```

`Recall.Core` targets `net10.0` and has one dependency: PdfPig 0.1.16. It must not gain a
WPF reference — the tests run headless.

## Data and persistence

- One file: `%LOCALAPPDATA%\Recall\data.json`.
- `RECALL_DATA_DIR` overrides the folder. The tests set it so they never touch a real profile.
- Saves write to a temp name then move into place, so an interrupted save leaves the previous
  file intact. Do not replace this with a plain write.
- `Store.Instance` is the single in-memory root (`AppData`). Everything mutates it and calls
  `Save()`.
- Demo decks are seeded only when `Data.Decks` is empty. A seeded profile is a normal profile.

## Domain model

`Deck` holds `List<Card>`. `Card.Kind` is a `CardKind` enum. `MultipleChoice` was appended
last on purpose so stored enum values keep their meaning — never reorder that enum.

Per-kind payload on `Card`: `Choices` + `CorrectChoices` (multi-select and multiple choice),
`Items` (order), `Pairs` (match), `ImagePath` (image), `Front`/`Back` for the rest.

`Card.Accepted` splits `Back` on `|` and `;`. The first entry is the key term.

## Grading — `TextMatch`

This is the rule that has to hold, and it is easy to break:

- Normalise both sides: lowercase, strip diacritics, non-alphanumerics become spaces, collapse
  runs, drop the articles `a an the of to`.
- Accept an exact match of the whole answer.
- Accept the **key term alone**. The key term is everything before the first separator
  (`, ; : ( ) [ ] — – - /`).
- Accept the key term **plus the reader's own words** — whole-word containment only.
- Reject a **broader** term: `cell` must not pass for `cell membrane`. That is why containment
  is word-bounded and why needles under 4 characters never match.
- Reject **misspellings**. There is no fuzzy distance. A near miss is a miss.

`TextMatch.KeyTerm` is also used to keep a multiple-choice list from offering `mitochondria`
next to `Mitochondrion`.

## Spaced repetition — `Srs`

SM-2 variant. `Grade` is `Again/Hard/Good/Easy`.

- `Again` sets a 10-minute due date, resets reps, drops ease by 0.2, floors at 1.3.
- Ease clamps to `[1.3, 3.2]`. Hard −0.15, Easy +0.15.
- Interval: rep 1 → 1/1/1/3 (Hard/Good/Easy), rep 2 → 2/3/6, then
  `round(max(interval,1) * ease * multiplier)` with Hard 0.85, Easy 1.35.
- Interval clamps to `[1, 365]`.
- A card that lapses `Card.SrsLeechThreshold` (8) times is suspended and leaves rotation.

`PreviewInterval` must stay in step with `Apply`. If you change one, change both.

## PDF import

`PdfLayout` turns a PDF into `PdfLine(Page, Text, Size, BoldRatio, Left, Right, ListItem)`.
`PdfImport` runs pages through `CardFactory`. `CardFactory` is 42 KB and holds every pass —
read it before adding one.

Passes, in the order they matter:

| Pass | Shape it catches | Notes |
| --- | --- | --- |
| Heading + body | bold or larger line, then a paragraph | score 1.0 baseline |
| `term: definition` | colon-separated line | |
| Bullet run | heading then `ListItem` lines | score 1.45, `MaxListBack = 400`, needs 3+ items, skips lead-ins ending in `:` |
| `lhs = rhs` | assignment definitions | score 1.25, asks `What does <lhs> represent?` |
| Cloze | a definition sentence | rejected when the answer is still visible |

Guards that must not be removed:

- **Cloze leak guard**: reject when `IndexOfWord(blanked, answer) >= 0`. A card whose answer is
  printed in its own prompt is worthless.
- **Underscore reject**: a term containing `_` is an identifier (`arguments_list`), not a term.
- **Variable reject**: two `=` signs, a numeric RHS, or `= { } < > | + *` in the RHS.
- **List min**: fewer than 3 items is not a list.
- **Front dedupe**: `PdfImport` dedupes repeated fronts across pages, keeping the higher score.
  `variable` cards bypass `Dedupe` because their fronts are templated and would collide.

Cloze rejects, in the order `TryCloze` applies them. Each one exists because a real card from
one of the sample handouts was unanswerable:

| Guard | Rejects |
| --- | --- |
| `ExampleLeadIns` | "For example, …", "the following", "as shown" — text about the sample, not the subject |
| `ProcedureOpener` | "Go to…", "Find the…", "Press…", "Step 2" — an exercise step |
| `ConnectiveOpener` | "Then…", "However…", "Thus…" — a sentence with no subject of its own |
| `SampleIdentifier` | "object p1", "array a2" — a value that only exists inside a listing |
| `PronounOpener` | a blank at index > 0 after "It/This/They" — the antecedent is on the previous line |
| spaced ` – ` / ` — ` | two clauses stapled together by a dash |

A list answer joins its items with `'\n'`, not `' '`. `TextMatch.Normalize` collapses
whitespace, so a newline-joined answer still grades correctly.

`PrepareDocument` recomputes `PdfLine.ListItem` from the bullet glyph in the text, discarding
whatever `PdfLayout` set. A test that wants a list must put a literal `•` in the line.

Geometry: `PdfLayout` rejoins a wrapped line only when the previous line reached the page's
right margin. Rejoining on any short line merges unrelated paragraphs.

Two sample documents behave differently and both are covered by tests:
`ComprogExample.pdf` reports every font size as `1.0`, so bold ratio is the only heading
signal. `DataDigitalExample.pdf` has real sizes (body 11.0).

## UI system

Tokens live in `Themes/Tokens.xaml`, control styles in `Themes/Controls.xaml`, palettes in
`Themes/Palette/*.xaml`. `ThemeManager` swaps merged dictionary index 0 at runtime, so every
brush reference in a view must be `DynamicResource`. A `StaticResource` brush will not repaint
on a palette switch.

The system, as of the design pass:

- **One radius scale**: `R.Sm` 4, `R.Md` 6, `R.Lg` 8, `R.Xl` 12. Do not introduce a literal
  `CornerRadius` in a view; use a token.
- **Hairlines**: surfaces are separated by a 1 px border and a step in value. No 2 px or 3 px
  borders, no `CornerRadius="999"` pills.
- **Shadows belong to floating layers only** — the combo popup and the confirm dialog. A card
  in the page flow has no `DropShadowEffect`.
- **Hover is a tint, not a transform.** Buttons use a `Wash` overlay inside the template that
  goes to 0.06 on hover and 0.13 on press. Nothing scales under the cursor.
- **One primary action per screen.** Everything else is `Btn` (neutral) or `BtnGhost`.
- **Type**: `T.Display` 32, `T.H1` 22, `T.H2` 17, `T.Body` 15/23, `T.Small` 13.5/20,
  `T.Label` 11.5 semibold, `T.Metric` 30.

Palettes: `clay` (displayed as **Graphite**, the default — neutral paper and one blue),
`deepocean`, `sandstone`, `forest`. The id stays `clay` so profiles saved before the rename
still resolve; `ThemeManager.FileName` maps it to `Clay.xaml`. Keep the swatch hexes in
`ThemeManager.All` in step with the palette files or the picker shows a colour the app does
not wear.

Never introduce violet or a lavender background. That combination is what made the old
default read as generated.

## Testing

`tests/Recall.Tests` is xunit and covers the core only. `PdfImportQualityTests` is the
regression suite for extraction — it builds cards through `CardFactory.FromLines` +
`PrepareDocument`, not `FromText`, because `Clean` collapses single newlines into spaces and
multi-line structure cannot survive it. Add a test for every new extraction pass and for every
leak you fix.

## Gotchas

- Never round-trip C# source through PowerShell `Get-Content -Raw` / `Set-Content`. It mangles
  UTF-8 and the files contain `·`, `—`, `→`, `‹`.
- .NET regex cannot express astral glyphs in a character class. Use an alternation:
  `[\u2190-\u21FF\u2200-\u22FF]|\uD835[\uDC00-\uDFFF]`.
- `Enumerable.Intersect` is case-sensitive by default. Pass `StringComparer.OrdinalIgnoreCase`
  when comparing answer text.
- The machine runs at 200% DPI. Screenshots are physical pixels; a 1200×820 window captures
  as 2378×1629.
- UIA helpers take an explicit `-TargetPid`; the focused window is not the target.
- `gh` is not installed. GitHub API calls need the token from `git credential fill`.

## Release

`v1.0.0` carries `Recall-win-x64.zip` (exe + README + LICENSE). Rebuild the zip after any
README change or the shipped README goes stale. Upload with `curl --data-binary`, not
`Invoke-RestMethod -InFile` — that produced a wrong-byte asset once. Always download the asset
back and compare SHA256 before calling it done.
