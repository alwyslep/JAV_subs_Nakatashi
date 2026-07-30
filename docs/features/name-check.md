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

## Remembering a spelling

**Remember these spellings for this series** writes accepted fixes to the shared glossary, so the sibling translator uses them for the next film in the series. It is deliberately narrow:

- Only suggestions you **accepted** are written, and only **once per name**. Pinning a rejected suggestion would carve a spelling you just refused into data the translator then trusts.
- Only names whose **original form is known** can be saved. When the model could not say what the original was, the row says *"original form unknown — fixes the file only"* rather than quietly not saving.
- A source form that is already in Hangul is refused: it went through a machine translation, so it is not the original spelling of anything.
- Pinned rows are protected on the translator side — its re-harvest and its clean-up pass both skip them, so a machine cannot overwrite a spelling a person chose.

## How often it finds something

Measured over eight real Korean subtitles (876–2,555 cues) against a hosted model: five files produced nothing, three produced four findings between them, and two of those four could be pinned. Two of six raw entries were removed by the rails above. Treat an empty result as the normal case — this is a check, not a rewrite pass.
