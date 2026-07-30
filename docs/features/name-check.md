# Check Names

Finds people whose name the subtitle spells more than one way, or spells by translating what its characters *mean* instead of how it *sounds* — 鈴の家りん written as "린의 집 인" rather than "스즈노야 린". Nothing is changed until you apply the suggestions you agree with, and an accepted spelling can also be remembered for the rest of the series.

- **Menu:** Tools → Check names...
- **Shortcut:** none by default

<!-- Screenshot: Check names window -->

## What makes it different from AI review

AI review asks the model for corrected *lines*. This pass asks for a **name table** instead — the original form, the spelling to use, and the other spellings found in the text. Three consequences, and they are the reason for the design:

- The pair *(original → chosen spelling)* is what the shared glossary needs. Rewritten lines do not carry it, so a rewrite could never teach anything.
- Subtitle Edit performs the substitution itself against the real text, so every before/after is an exact swap. The model cannot reword a line while claiming to fix a name, and a spelling it imagined simply matches nothing and disappears.
- The whole file goes in one request. A second spelling can only be recognised next to the first, so chunking would hide exactly what is being looked for.

Repeated lines are sent once, in first-occurrence order. That is not a size trick — the question is which spellings the file contains, and a line repeated twenty times answers it just as well the first time. It also keeps long files inside the 40,000-character budget: on a 6,582-line subtitle the un-deduplicated text hit the cap around line 3,000, so a name introduced late could not be found at all.

## Engines

The engine, URL, model and API key are **shared with AI review** — set them in either window. llama.cpp (managed local server), Ollama, or any OpenAI-compatible endpoint.

## What the model is told about the film

When the subtitle has a video open and the shared catalogues are available, two extra blocks are added to the instructions:

- **People credited on this film, in the original language** — read from the video's own tags, or from the shared catalogue when the tags are missing. Names that already contain Hangul are dropped as a group, because that tag came from a machine-translation pass and a wrong cast list is worse than none.
- **Spellings this series has already settled** — the address forms the shared glossary holds for this series, so the model prefers a spelling the series has been using rather than inventing a new one.

The transliteration rule ("a personal name is transliterated, never translated") is appended after your instructions and **cannot be removed by editing the prompt**. It is the one rule of this pass; a prompt edit that dropped it would turn the tool into the thing it exists to undo.

## Safety rails

Suggestions are filtered before you ever see them:

- The chosen spelling is removed from its own "wrong" list — otherwise you would be asked to approve a change that changes nothing.
- Anything **contained in** the chosen spelling is dropped. Replacing a string with something that contains it feeds itself: a model that proposed "사카미치 미루" as canonical with "미루" among the wrong spellings produced `사카미치 사카미치 미루 사카미치 미루`. A shorter form of the same name is an abbreviation, not a misspelling.
- **The form of address has to survive the swap.** The substitution is word for word, so 미르짱 → 사카미치 미루 leaves the particle attached to the wrong ending (`사카미치 미루은, 반드시`). 씨 for 군 is rejected too, and rightly — which honorific a speaker uses is the relationship, not a typo. The single exception is **상 → 씨**: 상 is さん left half-transliterated, so correcting it *is* the fix.
- Suggestions that would add or remove formatting tags are discarded.

## The original language

If the film's own subtitle in its original language is next to the video and its timecodes line up, a
second, small request goes out after the first: the names found, each with the lines that mention it
in both languages. It asks three things — how the original writes the name, whether it is a person at
all, and whether the chosen spelling is a faithful reading of that original.

This is what makes remembering possible: the first pass only ever sees the translation, so it usually
cannot know the original form. It is also what keeps remembering honest — the answers are read off the
original word rather than guessed from a transliteration.

The original is used only when it can be trusted, and is refused otherwise:

- **The name of the file is not taken as proof of its language.** Measured over 400 files named
  `*.ja.srt`: 257 were Japanese, 124 Chinese, 16 romanised, 3 Korean. The content decides.
- **Timecodes must line up within 100 ms.** Translation preserves them, so a real match is exact; a
  near miss is a different line. If fewer than half the lines match, the file is a different cut and
  is discarded whole rather than quoted from.
- **One file is used, not all of them.** Several original-language subtitles almost always means
  alternative rips of the same film rather than parts of it.

Nothing here is required. With no usable original the pass behaves exactly as it did before — it just
cannot teach the glossary as often.

## Remembering a spelling

**Remember these spellings for this series** writes accepted fixes to the shared glossary, so the sibling translator uses them for the next film in the series. It is deliberately narrow:

- Only suggestions you **accepted** are written, and only **once per name**. Pinning a rejected suggestion would carve a spelling you just refused into data the translator then trusts.
- Only names whose **original form is known** can be saved. When it is not known, the row says *"original form unknown — fixes the file only"* rather than quietly not saving.
- A source form that is already in Hangul is refused: it went through a machine translation, so it is not the original spelling of anything.
- **A name the original says is not a person is not saved** — the line is still fixed, and the row says *"not a person in the original"*.
- **When the original contradicts the chosen spelling, the suggestion is turned round.** Measured: the pass offered 히노코리 씨 as correct with 히노보리 씨 as the mistake, while the original said ひのぼり — so the fix is applied the other way instead, and the row says *"reversed: the original supports the other spelling"*. When the reversal is not usable, the row is shown, explained and left **unchecked** rather than applied.

- **The series' own glossary is asked first, and it is free.** When the spellings in the file disagree and the glossary already records one of them for this series, that wins — no model involved. A spelling you pinned wins outright; otherwise the reading with more rows behind it does, and a tie counts as no opinion. The row says *"reversed: this series already uses the other spelling"*.

> **A limit worth knowing.** The original-language subtitle is itself usually machine-transcribed, so it can be wrong too. On one measured film it spelled the same person both `宅本` and `タキモス` — and the real name turned out to be neither, but `滝本`, which only the glossary had. That is why the glossary is asked first. Where the glossary has no opinion, read a reversal as a strong hint rather than a verdict.
- Pinned rows are protected on the translator side — its re-harvest and its clean-up pass both skip them, so a machine cannot overwrite a spelling a person chose.

## How often it finds something

Measured over eight real Korean subtitles (876–2,555 cues) against a hosted model: five files produced nothing, and the rest produced a handful of findings between them, of which the rails above removed about a third. Treat an empty result as the normal case — this is a check, not a rewrite pass.

The number that actually gets pinned swings between runs, because the first pass is not deterministic: the same eight films gave 3 pinnable names one run and 1 the next. Reading the original language did not reliably raise that number; what it did was stop wrong spellings from being remembered.
