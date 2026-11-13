# Recall

A native Windows flashcard app. Decks, spaced repetition, eight question types, and a
PDF importer that turns a document into cards. No account, no sync, no AI — one JSON
file on your disk.

![Windows](https://img.shields.io/badge/platform-Windows%2011-7C3AED)
![.NET](https://img.shields.io/badge/.NET-10-7C3AED)

## What it does

- **Deck library** with due counts, mastery, a 12-week review heatmap, and streak.
- **Spaced repetition** using SM-2: ease factor, interval, lapses, and a due date per card.
- **Quiz mode** and **study mode** over a whole deck, a filtered set, or everything due.
- **Session summary** with accuracy, time, the cards you missed, and a re-run button.
- **PDF import** that extracts definition-shaped pairs, shows a review list, and only then
  writes cards.
- **Four palettes** — Clay, Deep Ocean, Sandstone, Forest Night — switchable in Settings and
  applied live.

## Question types

| Type | Shape | Grading |
| --- | --- | --- |
| Q&A | Type the answer | Whole answer, key term, or key term as whole words |
| Multiple choice | Four options, one correct | Single pick |
| Multi-select | Four options, one or more correct | Exact set |
| Fill the blanks | `{{blank}}` in the prompt | Same matcher as Q&A |
| Order items | Reorder a shuffled list | Exact sequence |
| Match pairs | Left column to right column | Every pair |
| True or false | Statement plus a boolean | Single pick |
| Image question | Prompt plus a local image | Same matcher as Q&A |

Answers can list alternatives with `|`. The first alternative is the key term, so
`Mitochondria, the site of aerobic respiration` accepts `Mitochondria` alone but not
`Mitochondrion`. Typed answers are compared exactly — no fuzzy matching, no partial credit.

## Build

Requires the .NET 10 SDK on Windows.

```bash
dotnet build
dotnet run --project src/Recall.App
```

Tests:

```bash
dotnet test
```

Self-contained single-file build:

```bash
dotnet publish src/Recall.App/Recall.App.csproj -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Where your data lives

```
%LOCALAPPDATA%\Recall\data.json
```

Decks, cards, review history, and the chosen palette. The file is written to a temporary
name and moved into place, so an interrupted save leaves the previous file intact. Set
`RECALL_DATA_DIR` to point at a different folder — that is how the test runs stay away from
a real profile.

Settings shows the exact path and can copy it.

## Layout

```
src/Recall.Core     models, store, SM-2, answer matching, quiz building, PDF import
src/Recall.App      WPF shell, views, view models, themes
tests/Recall.Tests  xunit tests over the core
```

Themes are one resource dictionary per palette in `src/Recall.App/Themes/Palette`. Every
brush reference in the views is a `DynamicResource`, so swapping the palette dictionary
repaints the running window.

## Keyboard

| Key | Action |
| --- | --- |
| `A`–`D`, `1`–`4` | Pick an option |
| `Enter` | Submit or advance |
| `Escape` | Leave the quiz |

## License

MIT. See [LICENSE](LICENSE).
