# 화계·어조(register) 기능 — 현황 분석과 제안

> 대상 버전: **v5.1.0-rc17** (`e5cc40b3e`) 기준, JAV_subs_Nakatashi 포크
> 작성일: 2026-07-28 · 관련 문서: [AI 기능 정리](ai-features.ko.md)

> **후속 진단 (2026-07-28):** 이 문서가 제안한 2단계 설계는 별도 프로젝트
> `srt-translator`에 **이미 구현되어 있습니다** — 프리스캔이 파일 1회 스캔으로
> 등장인물 관계·화자별 반말/존댓말·호칭 규칙을 뽑아 전 배치에 전역 주입합니다.
> 실제 번역이 그쪽에서 이뤄지므로 화계 작업의 무게중심도 그쪽입니다.
> → `srt-translator/docs/register-roadmap.ko.md`
> 이 문서는 Subtitle Edit 자체의 현황 분석으로 계속 유효합니다.

**질문:** AI 도우미의 네 버튼(오류 수정 / 읽기 속도 맞추기 / 더 격식 있게 / 더 편하게) 중에
등장인물의 **관계**(나이 차, 사제 관계, 연인 등)를 고려해 높임말·명령조 같은 말투를 맞춰 주는
기능이 있는가? 없다면 만들 수 있는가?

**답:** 넷 중에 없습니다. 만들 수 있고, 부품은 이미 대부분 존재합니다. 다만 **가장 효과가 큰
자리는 번역이 끝난 뒤가 아니라 번역하는 순간**입니다.

---

## 목차

- [1. 용어](#1-용어)
- [2. 현재 네 버튼이 하는 일](#2-현재-네-버튼이-하는-일)
- [3. 이미 있는 부품](#3-이미-있는-부품)
- [4. 설계 — 2단계(pass)](#4-설계--2단계pass)
- [5. 핵심 판단: 번역 시점이 진짜 자리](#5-핵심-판단-번역-시점이-진짜-자리)
- [6. 리스크](#6-리스크)
- [7. 단계별 제안](#7-단계별-제안)
- [8. 지금 당장 할 수 있는 실험](#8-지금-당장-할-수-있는-실험)
- [소스 위치](#소스-위치)

---

## 1. 용어

"어조"도 맞지만, 더 정확한 이름이 층위별로 따로 있습니다.

| 층위 | 용어 | 내용 |
|---|---|---|
| 문법 | **화계(話階) / 상대높임법** | 하십시오체 · 해요체 · 해체(반말) · 해라체. 한국어는 **이걸 고르지 않을 수가 없습니다** — 모든 종결어미가 관계를 강제로 드러냅니다 |
| 문체 | **어조 · 말투** (tone / register) | 명령조/청유조, 무뚝뚝함/부드러움, 거리감 |
| 번역학 | **역할어(役割語)** | 일본어가 관계를 싣는 장치 — `俺 / 僕 / 私 / あたし`, `お前 / 君 / あなた`, `~だぜ / ~のよ / ~かしら` |

앱 코드가 쓰는 단어는 **register**입니다. `MakeFormal` 프롬프트가 문자 그대로
`"in a more formal register"`라고 씁니다.

> 일본어 → 한국어 자막에서 이것은 부수적 품질 문제가 아니라 **작업의 본질**입니다.
> 일본어 역할어를 한국어 화계로 옮기는 일 그 자체이기 때문입니다.

---

## 2. 현재 네 버튼이 하는 일

`AiAssistantViewModel.cs:235-256`의 실제 프롬프트입니다.

| 버튼 | 프롬프트가 실제로 지시하는 것 | 어조 처리 |
|---|---|:--:|
| **오류 수정** | `"Fix only spelling, grammar and punctuation errors… Do not rephrase, do not change meaning, tone or style."` | ❌ **명시적으로 거부** |
| **읽기 속도 맞추기** | 최대 줄 길이 / CPS에 맞춰 축약. `"keeping the exact meaning"` | ❌ 무관 |
| **더 격식 있게** | `"Rewrite… in a more formal register, keeping the meaning."` | △ |
| **더 편하게** | `"Rewrite… in a more casual, colloquial register, keeping the meaning."` | △ |

뒤의 두 개가 근접하지만 네 가지 구조적 한계가 있습니다.

### ① 1차원 슬라이더

격식 ↔ 반말, 두 방향뿐입니다. *"40대 남성 교사가 20대 여성 학생에게 반말+명령조,
학생은 교사에게 존댓말"* 같은 **관계의 방향성**을 표현할 어휘가 프롬프트에 없습니다.

### ② 한 줄만

`MainViewModel.cs:5497` — 대상은 `SelectedSubtitle` 하나입니다.
1,000줄 자막이면 1,000번 눌러야 합니다.

### ③ 문맥은 ±3줄

`MainViewModel.cs:5505-5522`:

```csharp
// Give the model the neighbouring lines as read-only context so it can
// resolve pronouns, speaker, and tone without changing them.
var from = Math.Max(0, index - 3);
var to = Math.Min(Subtitles.Count - 1, index + 3);
```

주석은 "tone"을 언급하지만, 앞뒤 세 줄로는 **화자가 누구인지** 판단이 안 됩니다.

### ④ 기억이 없음

호출마다 완전히 독립적입니다. 40번 줄과 200번 줄이 서로를 모른 채 판정되고,
한국어에서 이는 **반말/존댓말이 파일 내내 널뛰는** 결과가 됩니다.
자막에서 가장 눈에 띄는 결함이 정확히 이것입니다.

> **앱 전체에서 어조 관련 파라미터는 단 하나** — DeepL의 `DeepLFormality`
> (`SeAutoTranslate.cs:63`)뿐입니다. 그런데 DeepL의 formality는 지원 언어가 한정되어
> 있고 **한국어는 그 목록에 없습니다.** 즉 실질적으로 한국어에는 아무 수단이 없습니다.

---

## 3. 이미 있는 부품

이 기능은 처음부터 만드는 게 아니라 **AI 검토를 프롬프트만 바꿔 재활용**하는 수준입니다.

| 부품 | 파일 | 그대로 쓸 수 있는 이유 |
|---|---|---|
| 문장 단위 배치 분할 | `AiReviewChunker.cs` | 한 문장이 여러 줄에 걸쳐도 쪼개지 않음(`MaxLinesPerUnit = 4`), 배치마다 앞뒤 **읽기 전용 2줄**(`ContextLines = 2`) |
| 통신 규약 | `AiReviewProtocol.cs` | 번호 매긴 JSON 입력 → `{"changes":[…]}` 출력. **환각/문맥 줄 자동 폐기**(`editableNumbers`), 태그 보존 검사(`TagsMatch`), 마크다운 펜스 관용 파싱 |
| 엔진 추상화 | `AiReviewClient.cs` | llama.cpp / Ollama / OpenAI 호환 3종 공용 |
| 배치 루프 + 1회 재시도 | `AiReviewViewModel.cs:323-350` | 규약 위반 응답이면 그 배치만 한 번 재요청 |
| 줄별 수락/거부 UI | `AiReviewViewModel.cs`, `ReviewSuggestionItem.cs` | 제안 목록을 그대로 재사용 |
| 사용자 편집 프롬프트 | `AiReviewPromptWindow.cs` | 저장 + 기본값 복원까지 완비 |
| **화자 필드** | `Paragraph.Actor` / 그리드 Actor 열 (`InitListViewAndEditBox.cs:474-481`) | **숨김 가능한 열로 이미 존재**. `vm.ShowColumnActor`로 켜고 끔 |

마지막 항목이 특히 중요합니다. 추정한 화자를 `Actor`에 써 넣으면

- 사용자가 **그리드에서 눈으로 확인하고 직접 고칠 수 있고**,
- ASSA로 내보낼 때 표준 필드로 나가고,
- TTS 다중 화자(캐스트) 기능이 같은 필드를 읽으므로 **공짜로 연동**됩니다.

새 자료구조가 필요 없습니다.

> 주의: `ActorVoiceDetector.cs:30-50`은 이름이 "Detector"지만 **추론을 하지 않습니다.**
> ASSA의 `Actor` 필드나 WebVTT의 `<v Name>` 태그를 **읽기만** 합니다.
> 화자 판별 기능은 앱에 존재하지 않습니다.

---

## 4. 설계 — 2단계(pass)

한 줄씩 고쳐서는 일관성이 나오지 않습니다.
**파일 단위 정책을 먼저 확정하고, 그 다음에 적용**해야 합니다.

### Pass 1 — 관계 분석 (파일당 1회)

자막 전체(또는 샘플)를 보내 **캐스트 시트**를 뽑고, **사용자가 표에서 고칩니다.**

```json
{
  "synopsis": "교사와 학생, 방과 후 교실",
  "speakers": [
    { "id": "A", "label": "여성", "age": "20대 초", "role": "학생" },
    { "id": "B", "label": "남성", "age": "40대",   "role": "교사" }
  ],
  "policy": [
    { "from": "A", "to": "B", "level": "해요체", "note": "존대. 감정이 격해지면 흔들려도 됨" },
    { "from": "B", "to": "A", "level": "해체",   "note": "반말. 명령조 섞임" }
  ]
}
```

**사용자 편집 단계는 생략할 수 없습니다.** LLM은 관계를 자주 틀리고,
영상을 실제로 본 사람은 사용자입니다.

### Pass 2 — 적용 (AI 검토와 동일한 루프)

캐스트 시트를 시스템 프롬프트에 고정하고, 배치마다 같은 `{"changes":[…]}` 규약으로 받습니다.
기존 검토 UI를 그대로 씁니다.

```
줄마다 화자(from → to)를 판정하고, policy가 지정한 화계로 종결어미를 맞춰라.
내용·의미·고유명사·형식 태그는 절대 바꾸지 마라. 종결어미와 호칭만 조정한다.
확신이 서지 않는 줄은 changes에서 빼라.
```

마지막 문장이 핵심입니다. **모르면 건드리지 않게** 만들어야 조용한 오염이 생기지 않습니다.

### 전체 흐름

```
자막 파일
    │
    ├─▶ Pass 1 ── LLM ──▶ 캐스트 시트(JSON)
    │                          │
    │                     ┌────▼────┐
    │                     │ 사용자   │  ← 표에서 관계·화계 수정
    │                     │  편집    │     (여기가 품질을 결정)
    │                     └────┬────┘
    │                          │
    └─▶ Pass 2 ◀───────────────┘
            │
            │  AiReviewChunker로 배치 분할
            │  배치마다 캐스트 시트 + 읽기 전용 문맥 2줄
            │
            ▼
     {"changes":[…]}  ──▶  줄별 수락/거부 UI  ──▶  자막 반영
                                                 (+ Actor 열에 화자 기록)
```

---

## 5. 핵심 판단: 번역 시점이 진짜 자리

이 분석에서 가장 중요한 결론입니다.

관계 정보는 **일본어 원문 안에 들어 있습니다** — `俺 / あたし`, `お前 / 先生`,
`~だぜ / ~ですね`. 그런데 한국어 SRT가 만들어지는 순간 그 정보는 **소실됩니다.**

이미 번역이 끝난 한국어 텍스트(예: `…deepseek-v4-flash.ko.srt`)만 놓고 어조를 복원하는 것은
**뭉개진 정보를 추측으로 되살리는 일**입니다. 되긴 되지만 정확도에 상한이 있습니다.

**번역 프롬프트에 캐스트 시트를 넣으면 훨씬 정확하고 훨씬 쌉니다.**
LLM이 원문의 역할어를 보면서 한국어 화계를 고르기 때문입니다.

그리고 이것을 넣을 자리는 이미 있습니다.

> **정정 (2026-07-28).** 이 문서는 원래 *"`AutoTranslateWindow.cs`에는 번역 프롬프트를
> 편집하는 UI가 없다"*고 적었습니다. **틀렸습니다.** `AutoTranslateWindow.cs` 하나만
> 검색해서 내린 결론이었는데, 편집기는 형제 창인
> **`TranslateSettingsWindow`**(뷰모델 `TranslateSettingsViewModel`)에 있고
> `AutoTranslateViewModel.OpenSettings`(`:754`)가 엽니다.
>
> 이미 갖춰진 것: 13개 엔진별 프롬프트 로드/저장, `{0}`·`{1}` 필수 검증, 중괄호 오용 검사,
> 프롬프트를 안 받는 엔진에서는 입력란 자체를 숨김(`PromptIsVisible`).
> **따라서 아래 A단계는 "만들 것"이 아니라 "이미 있는 것"입니다.**
>
> 실제로 빠져 있던 것은 하나뿐이었고, 그건 채웠습니다 — **기본값 복원 버튼**.
> `LoadValues`는 저장값이 *공백일 때만* 기본값으로 떨어지므로, 프롬프트를 한번
> 망가뜨리면 영어 원문을 외우거나 `Settings.json`을 손으로 고치는 것 외에 되돌릴
> 방법이 없었습니다. 프롬프트 실험이 A단계의 목적인 이상 이건 필수입니다.
> (커밋 `f2faf8348`)

기본 프롬프트는 전 엔진이 사실상 동일합니다 (`SeAutoTranslate.cs:114-203`):

```
Translate from {0} to {1}, keep punctuation as input,
do not censor the translation, give only the output without comments:
```

관계에 대한 언급이 한 글자도 없습니다.

---

## 6. 리스크

| 리스크 | 실상 | 완화책 |
|---|---|---|
| **화자 판별** | 자막 파일에 화자 정보가 없음. 앱에도 추론 기능 없음(§3 주의) | Pass 1에서 LLM이 추정 → `Actor` 열에 기록 → 사용자가 육안 수정. **완벽할 필요 없음** |
| **로컬 소형 모델의 한계** | 기본 엔진은 llama.cpp + 소형 GGUF. 1,000줄 화계 일관성은 난도가 높음 | 이 기능만은 **큰 모델 권장**. OpenAI 호환 엔드포인트로 DeepSeek / Gemini / Claude 연결을 기본 안내로 |
| **컨텍스트 예산** | 1,000줄 ≈ 30–60k 토큰. 8k 컨텍스트 로컬 모델에는 안 들어감 | Pass 1은 샘플링 (앞 100줄 + N줄 간격) |
| **"영상 줄거리에서 힌트"** | 앱은 영상을 볼 수 없음 | **사용자가 한두 줄 시놉시스를 직접 적는 입력란**이 자동 추론보다 정확하고 압도적으로 쌈. 이건 타협이 아니라 정답 |
| **조용한 오염** | 잘못된 화계가 대량으로 적용되면 되돌리기 어려움 | 프롬프트에 "확신 없으면 건드리지 마라" 명시 + 기존 **줄별 수락/거부 UI** 필수 경유 |
| **메뉴 인벤토리 테스트** | 새 명령이 늘면 `MainMenu_HasNoCommandsMissingFromBaseline`이 실패 | 의도된 동작(상류 추가 감지용). `./tools/build.ps1 menu-baseline`으로 갱신하고 **diff에서 삭제된 줄이 없는지 확인** |

---

## 7. 단계별 제안

효과 대비 비용 순입니다.

| 단계 | 내용 | 비용 | 효과 |
|:--:|---|:--:|---|
| **A** | ~~번역 프롬프트 편집 창 추가~~ — **이미 있음**(`TranslateSettingsWindow`). 여기 캐스트 시트를 직접 기입하면 된다 | — | **가장 큼.** 번역 시점에 어조를 심으므로 사후 복원이 불필요해짐 |
| **B** | AI 도우미에 **화계 드롭다운**(하십시오체/해요체/해체/해라체) + 관계 메모 입력란(설정에 저장). 격식/반말 두 버튼을 대체 | 1일 | 중간 — 여전히 한 줄씩이지만 정밀 지정 가능 |
| **C** | **화계 정리 도구** — §4의 2단계 전체. AI 검토 부품 재활용 | 3–5일 | 큼 — 이미 번역된 파일을 구제하는 유일한 방법 |
| **D** | 화자 자동 판별 결과를 `Actor` 열에 기록 | C에 흡수 | TTS 다중 화자 기능과 공유 |

**권장 순서: A → C → B.**

- A는 **이미 끝난 상태**입니다(기본값 복원 버튼까지 포함). 바로 써보실 수 있고,
  다른 모든 단계의 전제가 됩니다.
- B는 C를 만들면 상당 부분 흡수되므로 뒤로 미룹니다.

---

## 8. 지금 당장 할 수 있는 실험

**코드 수정 없이, 앱 안에서** A의 가치를 검증할 수 있습니다.

1. **번역 → 자동 번역**을 열고 엔진을 고릅니다.
2. **설정** 버튼(`AutoTranslateViewModel.OpenSettings`)을 눌러 **프롬프트** 입력란을 엽니다.
3. 값을 다음과 같이 교체합니다 — `{0}`/`{1}`은 원본/대상 언어로 치환되므로 **반드시 유지**합니다.
   (창이 `{0}`·`{1}` 유무를 확인해 주고, 마음에 안 들면 **기본값으로 초기화**로 되돌아옵니다.)

```
Translate from {0} to {1}. This is dialogue between two speakers:
a man in his 40s (teacher) and a woman in her early 20s (student).
The man speaks to her in casual, sometimes commanding Korean (해체/반말).
The woman speaks to him in polite Korean (해요체).
Keep this register consistent across every line.
Keep punctuation as input, do not censor the translation,
give only the output without comments:
```

4. 같은 파일을 다시 번역해 기존 결과와 비교합니다.

차이가 뚜렷하면 C를 만들 가치가 확정됩니다.

> **굳이 `Settings.json`을 직접 고치실 거라면:** UTF-8 BOM이며 8만 자가 넘습니다.
> PowerShell의 `ConvertFrom-Json | ConvertTo-Json`은 기본 깊이가 2라 **파일을 파괴합니다.**
> 텍스트 편집기로 직접 고치거나 문자열 치환만 쓰고, 백업은 필수입니다.
> 위의 UI 경로를 쓰면 이 위험이 전혀 없습니다.

---

## 소스 위치

| 대상 | 경로 |
|---|---|
| AI 도우미 — 네 버튼의 프롬프트 | `src/ui/Features/Main/AiAssistant/AiAssistantViewModel.cs:235-256` |
| AI 도우미 — 호출부, ±3줄 문맥 | `src/ui/Features/Main/MainViewModel.cs:5489-5540` |
| AI 검토 — 배치 분할 | `src/ui/Features/Tools/AiReview/AiReviewChunker.cs` |
| AI 검토 — JSON 규약 | `src/ui/Features/Tools/AiReview/AiReviewProtocol.cs` |
| AI 검토 — 배치 루프 | `src/ui/Features/Tools/AiReview/AiReviewViewModel.cs:317-360` |
| AI 검토 — 프롬프트 편집 창 | `src/ui/Features/Tools/AiReview/AiReviewPromptWindow.cs`, `…PromptViewModel.cs` |
| **번역 — 프롬프트 편집 창 (이미 존재)** | `src/ui/Features/Translate/TranslateSettingsWindow.cs`, `…SettingsViewModel.cs` |
| 번역 — 편집 창을 여는 지점 | `src/ui/Features/Translate/AutoTranslateViewModel.cs:754` |
| AI 검토 — 기본 프롬프트 / 배치 크기 | `src/ui/Logic/Config/SeAiReview.cs:19-34` |
| 번역 — 엔진별 프롬프트 기본값 | `src/ui/Logic/Config/SeAutoTranslate.cs:114-203` |
| 번역 — DeepL formality (유일한 기존 어조 파라미터) | `src/ui/Logic/Config/SeAutoTranslate.cs:63` |
| 그리드 — Actor 열 | `src/ui/Features/Main/Layout/InitListViewAndEditBox.cs:474-481` |
| 화자 — 읽기 전용 탐지기 | `src/ui/Features/Video/TextToSpeech/ActorVoices/ActorVoiceDetector.cs:30-50` |

> 이 문서는 rc17 기준 분석입니다. 상류를 rebase해 AI 도우미의 버튼 구성이나
> 프롬프트가 바뀌면 §2를 다시 확인해야 합니다.
