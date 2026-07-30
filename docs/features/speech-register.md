# Match Speech Level

Rewrites the **endings** of the selected lines to one Korean speech level (화계) — 하십시오체, 해요체, 해체 (반말) or 해라체 — leaving the wording alone. Useful when a translation has one character switching politeness at random, which the source language marked but the target language lost.

- **Where:** right-click the subtitle grid (or the text box) → Match speech level..., or the fifth button in the **AI assistant** window
- **Not in the main menu** — it acts on a selection, and the natural moment is the right-click you already made on those lines

<!-- Screenshot: Match speech level window -->

## Why it needs a selection

Speech level is a property of *who is speaking to whom*, not of the file. One film usually contains several relationships at once, so a whole-file pass would flatten them all to the same level. Select the lines belonging to one speaker-and-listener pair, run the pass, then do the next pair.

## Who speaks how in this film

The **who to whom (optional)** box is what the model is told about the relationships. Write one direction per line with a level:

```
아내→상층부 해요체
상층부→아내 해체, 명령조
```

Prose descriptions like "shy way of speaking" are not a speech level and the model cannot act on them.

The box is **filled in for you** when something better than nothing is available, and the window says which source it used:

| Source shown | Where it came from |
|---|---|
| Note I saved | A note you edited and saved for this film — always wins |
| Translator guidebook | The relationship notes the sibling translator's pre-scan produced |
| Video tags | The film's own original-language title, genres, cast and synopsis |
| Shared catalogue | The same fields from the shared catalogue, when the file has no tags |

The last two are not per-speaker rules, so the block they produce is introduced with *"No per-speaker rules were recorded for this film. Work them out from its own description:"* — the model reads it as evidence rather than as instructions. No extra model call is made to build any of this.

Editing the note and saving stores it **against this film's release code**, so it no longer leaks into the next film you open. Only an edit is stored: automatically-filled text is never saved, because a machine's guess written to the pinned slot would outlive every later attempt to improve it.

## Running it

Pick the level, press **Match selected lines**. The selection is sent in batches, and suggestions appear while it runs.

Two things the batching gets right that a whole-file chunker would not:

- **Context comes from the whole subtitle, not from the selection.** The read-only lines shown around each batch are the real neighbours — that is where the evidence for who is talking to whom lives.
- **A selection with gaps is split into contiguous runs first.** Otherwise lines 11 and 400 could land in the same "sentence" and unrelated dialogue would share a batch.

Each suggestion is a before/after pair you can check or uncheck. **A suggestion that changed more than the ending is flagged with a warning** — the pass is supposed to touch endings only, so anything else is worth a look before you accept it.

## The prompt

**Edit prompt...** opens the instructions. `{language}` becomes the subtitle language, `{level}` the level you picked, and `{notes}` the relationship box above. **Reset to default** restores the shipped instructions.
