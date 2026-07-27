# Subtitle Edit — AI 기능 정리

> 대상 버전: **v5.1.0-rc17** (`e5cc40b3e`) 기준, JAV_subs_Nakatashi 포크
> 작성일: 2026-07-27 · 메뉴 경로는 이 포크의 [Phase 4 메뉴 재편](../CLAUDE.md) 기준입니다.

앱 안에서 **AI가 실제로 개입하는 기능은 8가지**입니다. 대부분 로컬 모델과
클라우드 API를 골라 쓸 수 있고, 엔진 목록은 실행 중인 OS와 설치 상태에 따라
달라집니다.

---

## 목차

- [한눈에 보기](#한눈에-보기)
- [1. AI 검토](#1-ai-검토)
- [2. AI 도우미](#2-ai-도우미)
- [3. 자동 번역](#3-자동-번역)
- [4. 복사/붙여넣기로 번역](#4-복사붙여넣기로-번역)
- [5. 음성을 텍스트로 변환](#5-음성을-텍스트로-변환-asr)
- [6. 텍스트 음성 변환](#6-텍스트-음성-변환-tts)
- [7. 자막이 입혀진 비디오 OCR](#7-자막이-입혀진-비디오-ocr)
- [8. OCR — 이미지 기반 자막](#8-ocr--이미지-기반-자막)
- [AI로 오해하기 쉬운 비-AI 기능](#ai로-오해하기-쉬운-비-ai-기능)
- [로컬 LLM 3종 — 공통 축](#로컬-llm-3종--공통-축)
- [추천 워크플로](#추천-워크플로-일본어-음성--한국어-자막)
- [소스 위치](#소스-위치)

---

## 한눈에 보기

| # | 기능 | 위치 | 하는 일 | 엔진 수 |
|:--:|---|---|---|:--:|
| 1 | **AI 검토** | 도구(T) > AI 검토... | LLM이 자막 전체를 교정 | 3 |
| 2 | **AI 도우미** | 텍스트 상자 🤖 버튼 / 우클릭 | 현재 한 줄을 문맥과 함께 상담 | 3 |
| 3 | **자동 번역** | 번역(A) > 자동 번역... | 자막을 다른 언어로 번역 | 25 |
| 4 | **복사/붙여넣기로 번역** | 번역(A) > 복사/붙여넣기로 번역 | 외부 LLM에 수동으로 전달 | — |
| 5 | **음성을 텍스트로 변환** | 비디오(V) > 음성을 텍스트로 변환... | 오디오에서 자막 생성 (ASR) | 10 |
| 6 | **텍스트 음성 변환** | 비디오(V) > 텍스트 음성 변환... | 자막을 음성으로 합성 (TTS) | 17 |
| 7 | **비디오 OCR** | 비디오(V) > 자막이 입혀진 비디오 OCR... | 하드섭을 읽어 자막 파일 생성 | 5 |
| 8 | **OCR** | *메뉴 없음* — 이미지 자막을 열면 자동 | 이미지 자막을 텍스트로 변환 | 16 |

> 일괄 변환(**도구 > 일괄 변환**)에서도 **자동 번역**을 처리 단계 중 하나로 쓸 수 있습니다.

---

## 1. AI 검토

**도구(T) > AI 검토...**

LLM에게 자막 전체를 교정시킵니다. 자동으로 반영되지 않고, 제안 목록을
체크박스로 골라 **"N개의 수정사항 적용"** 을 눌러야 반영됩니다.

**동작 방식**

- 자막을 **15줄씩** 묶어서 전송 (`MaxLinesPerBatch`, 조정 가능)
- 번호가 매겨진 JSON으로 보내고, 모델은 **변경된 줄만** 담은 유효한 JSON으로 응답
- 규약을 어긴 응답은 재시도 후 건너뜀 — 그래서 작은 모델에서도 파손 없이 굴러갑니다
- 프롬프트는 편집 가능 (`{language}`가 자막 언어로 치환됨)

**기본 프롬프트 (요지)**

> 자막 교정자다. 오타·맞춤법·문법·구두점을 고쳐라.
> 문장을 다시 쓰지 말고, 의미·어조·문체를 바꾸지 마라. 이름·속어·의도된 방언은 그대로 둬라.
> `<i>`, `{\an8}` 같은 서식 태그와 줄바꿈은 정확히 유지하라. 실제 오류만 고쳐라.

**엔진**

| 엔진 | 실행 | 기본 주소 | 비고 |
|---|---|---|---|
| **llama.cpp** | 로컬 | — | 기본값. 앱이 직접 다운로드 (콤보의 점 = 설치 상태) |
| **Ollama** | 로컬 | `http://localhost:11434/v1/chat/completions` | 사용자가 별도 실행 |
| **OpenAI 호환** | 로컬/클라우드 | `http://localhost:1234/v1/chat/completions` | LM Studio 기본 포트. API 키 입력란 있음 |

> 엔진 콤보의 점 색: 회색=미설치, 주황=업데이트 있음, 초록=최신.
> llama.cpp만 앱이 설치를 관리하므로 나머지 둘은 점이 없습니다.

---

## 2. AI 도우미

**자막 텍스트 상자의 🤖 버튼**, 또는 **텍스트 상자 우클릭 > AI 도우미**

> 버튼이 안 보이면: 옵션(O) > 설정 > **"텍스트 상자: AI 도우미 버튼 표시"**

**현재 한 줄**만 다루되 **앞뒤 줄을 문맥으로** 함께 보냅니다. AI 검토가 전체
일괄 교정이라면, 이쪽은 한 줄을 붙잡고 상담하는 용도입니다.

**빠른 동작 4가지**

| 버튼 | 용도 |
|---|---|
| 오류 수정 | 그 줄의 오타·문법 교정 |
| 읽기 속도 맞추기 | CPS 한도에 맞게 줄이기 |
| 더 격식 있게 | 문체를 격식체로 |
| 더 편하게 | 문체를 구어체로 |

여기에 더해 **자유 질문** 입력란이 있습니다 ("이 줄에 대해 질문하거나 변경을
요청하세요..."). "모델의 추론 과정 표시"를 켜면 reasoning 모델의 사고 과정도
볼 수 있습니다.

결과는 **"줄에 적용"** 을 눌러야 반영됩니다 — 자동 적용은 없습니다.

**엔진**: AI 검토와 **동일하며 설정을 공유**합니다. 창 안에서 바로 엔진·모델을
바꿀 수 있습니다.

> **한계:** 네 버튼 중 등장인물의 **관계**(나이 차, 사제, 연인 등)를 반영해
> 높임말·명령조를 맞춰 주는 것은 없습니다. "더 격식 있게 / 더 편하게"는 1차원이고,
> 한 줄씩이며, 호출 사이에 기억이 없어 파일 전체의 화계 일관성을 보장하지 못합니다.
> 분석과 개선 제안은 → [화계·어조(register) 기능 제안](ai-register-proposal.ko.md)

---

## 3. 자동 번역

**번역(A) > 자동 번역...**

등록된 엔진 **25종**. 크게 LLM 기반과 전용 번역기로 나뉩니다.

### LLM 기반 (13종)

| 엔진 | 실행 | API 키 |
|---|---|:--:|
| ChatGPT | 클라우드 | 필요 |
| Anthropic | 클라우드 | 필요 |
| Gemini | 클라우드 | 필요 |
| DeepSeek | 클라우드 | 필요 |
| Mistral | 클라우드 | 필요 |
| Groq | 클라우드 | 필요 |
| OpenRouter | 클라우드 | 필요 |
| Perplexity | 클라우드 | 필요 |
| NVIDIA | 클라우드 | 필요 |
| **Ollama** | 로컬 | 불필요 |
| **llama.cpp** | 로컬 | 불필요 |
| **LM Studio** | 로컬 | 불필요 |
| **OpenAI 호환** | 로컬/클라우드 | 선택 |

### 전용 번역기 (12종)

| 엔진 | 실행 | API 키 |
|---|---|:--:|
| Google Translate v1 | 클라우드 | 불필요 |
| Google Translate v2 | 클라우드 | 필요 |
| Microsoft Translator | 클라우드 | 필요 |
| DeepL | 클라우드 | 필요 |
| DeepLX | 클라우드/셀프호스팅 | — |
| Lara | 클라우드 | 필요 |
| Papago | 클라우드 | 필요 |
| Baidu | 클라우드 | 필요 |
| MyMemory | 클라우드 | 불필요 |
| **LibreTranslate** | 로컬/셀프호스팅 | 선택 |
| **NLLB serve / NLLB API** | 로컬 / 클라우드 | — |
| **Crisp ASR MADLAD** | 로컬 | 불필요 |

> **완전 오프라인으로 돌릴 수 있는 것**: Ollama, llama.cpp, LM Studio,
> LibreTranslate, NLLB serve, Crisp ASR MADLAD, OpenAI 호환 서버.

---

## 4. 복사/붙여넣기로 번역

**번역(A) > 복사/붙여넣기로 번역**

앱 내 엔진을 쓰지 않습니다. 자막을 적당한 덩어리로 잘라 클립보드에 넣어 주면,
사용자가 ChatGPT·Claude·웹 번역기 같은 **외부 도구에 붙여넣고 결과를 다시
붙여넣는** 수동 워크플로입니다.

API 키가 없거나, 앱이 지원하지 않는 모델을 쓰고 싶을 때의 탈출구입니다.

---

## 5. 음성을 텍스트로 변환 (ASR)

**비디오(V) > 음성을 텍스트로 변환...**

Windows 기준 **10종**. (macOS에서는 MLX Whisper가 추가되고 Const-me 등이 빠지는 식으로
플랫폼마다 목록이 다릅니다.)

| 엔진 | 실행 | 비고 |
|---|---|---|
| **Whisper CPP** | 로컬 | 기본 선택 |
| Purfview Faster Whisper XXL | 로컬 | Windows / Linux x64 |
| Whisper Const-me | 로컬 | Windows 전용, GPU |
| Whisper CTranslate2 | 로컬 | |
| Qwen3 ASR CPP | 로컬 | |
| **Crisp ASR** | 로컬 | 백엔드 16종 묶음 — 아래 참조 |
| Whisper OpenAI | 클라우드 | API 키 |
| OpenAI 호환 서버 | 로컬/클라우드 | |
| OpenRouter | 클라우드 | API 키 |
| Alibaba Qwen3-ASR | 클라우드 | API 키 (DashScope) |

### Crisp ASR 백엔드 16종

`Qwen3` · `Parakeet` · `SenseVoice` · `Canary` · `Granite` · `Kyutai` ·
`Fire Red` · `Fun-ASR Nano` · `Fun-ASR MLT Nano` · `GLM` · `Omni` · `Mega` ·
`ARK` · `Cohere` · `MADLAD` · **`MOSS Diarize`**(화자 분리)

> 음성 인식 결과에는 **규칙 기반 후처리**(마침표 추가, 줄 병합, 대소문자 교정,
> 짧은 길이 보정, 줄 분할)가 붙습니다. 이 후처리 자체는 AI가 아닙니다.

---

## 6. 텍스트 음성 변환 (TTS)

**비디오(V) > 텍스트 음성 변환...**

등록된 엔진 **17종**. 자막을 음성으로 합성하고, ASSA 배역/WebVTT 화자가 있으면
**배역별로 다른 목소리**를 배정할 수 있습니다.

### 로컬 (10종)

| 엔진 | 비고 |
|---|---|
| **Piper** | 가볍고 빠름 (macOS 제외) |
| Kokoro TTS | |
| OmniVoice TTS | |
| Chatterbox TTS | CrispASR 런타임 |
| Qwen3 TTS | CrispASR 런타임 |
| IndexTTS | CrispASR 런타임 |
| VoxCPM2 | CrispASR 런타임 |
| MOSS-TTS | CrispASR 런타임 |
| Zonos TTS | CrispASR 런타임 |
| CosyVoice3 | CrispASR 런타임 |

### 클라우드 / 서버 (7종)

| 엔진 | API 키 |
|---|:--:|
| EdgeTts | 불필요 |
| GoogleSpeech | 불필요 |
| AllTalk | 불필요 (셀프호스팅 서버) |
| ElevenLabs | **필요** |
| Azure Speech | **필요** |
| Mistral Speech | **필요** |
| Murf | **필요** |

> 소스에는 VibeVoice, F5-TTS도 있지만 현재 **주석 처리**되어 목록에 뜨지 않습니다.

---

## 7. 자막이 입혀진 비디오 OCR

**비디오(V) > 자막이 입혀진 비디오 OCR...**

화면에 박혀 있는 자막(하드섭)을 읽어 자막 파일로 만듭니다.

| 엔진 | 설명 |
|---|---|
| **Paddle OCR Standalone** | 로컬. 자동 다운로드, 빠르고 정확 |
| Paddle OCR Python | 로컬. `pip install paddleocr` 필요 |
| Ollama vision | 로컬 비전 모델 (예: glm-ocr) |
| llama.cpp | 로컬 비전 모델. 자동 다운로드 |
| GLM API | Z.ai / bigmodel.cn. API 키 필요 |

---

## 8. OCR — 이미지 기반 자막

**메뉴에 없습니다.** Blu-ray `.sup`, VobSub `.sub/.idx`, DVD, MKV/MP4의 이미지
자막 트랙, BDN XML, DivX 등을 열면 OCR 창이 **자동으로** 뜹니다.

| 분류 | 엔진 |
|---|---|
| **비전 LLM** | Ollama vision, llama.cpp, GLM API, Mistral, CrispEmbed |
| **클라우드 OCR** | Google Vision, Google Lens (2종), Azure Vision, Amazon Rekognition |
| **전통 OCR** | Paddle OCR Standalone, Paddle OCR Python, Tesseract |
| **비-AI** | nOCR(글자 패턴 매칭), 이진 이미지 비교 |

> OCR 결과에는 **OCR 오류 수정 엔진**이 따로 붙습니다. 사전 + 정규식 치환
> 목록 기반이며 AI가 아닙니다.

---

## AI로 오해하기 쉬운 비-AI 기능

| 기능 | 실제 동작 |
|---|---|
| 장면 전환 생성/가져오기 | ffmpeg `scene` 필터 — 프레임 차이 임계값 |
| 맞춤법 검사 | Hunspell 사전 |
| OCR 오류 수정 엔진 | 사전 + 정규식 치환 목록 |
| 음성 인식 후처리 | 규칙 기반 (마침표·줄 병합·대소문자·길이) |
| nOCR / 이진 이미지 비교 | 글자 모양 패턴 매칭 |
| 파형 타임코드 추측 | 음량 임계값 기반 무음 구간 검출 |
| 배역/화자 감지 | ASSA 배역명·WebVTT `<v>` 태그 파싱 |
| 일반 오류 수정 | 규칙 목록 |

---

## 로컬 LLM 3종 — 공통 축

**llama.cpp · Ollama · OpenAI 호환 서버**, 이 셋이 네 군데에 공통으로 쓰입니다.

```
                    ┌─ AI 검토      (도구)
llama.cpp ──┐       ├─ AI 도우미    (텍스트 상자)
Ollama    ──┼──────>┤
OpenAI호환 ─┘       ├─ 자동 번역    (번역)
                    └─ OCR / 비디오 OCR  (비전 모델)
```

| | 설치 | 앱이 관리? |
|---|---|:--:|
| **llama.cpp** | 앱 내에서 다운로드 | ✅ |
| **Ollama** | 사용자가 설치·실행 (`ollama serve`) | ❌ |
| **OpenAI 호환** | LM Studio 등 사용자가 실행 | ❌ |

**AI 검토와 AI 도우미는 설정을 공유**하므로, 한쪽에서 모델을 바꾸면 다른 쪽에도
적용됩니다. 자동 번역과 OCR은 각자 별도 설정입니다.

---

## 추천 워크플로 (일본어 음성 → 한국어 자막)

| 단계 | 기능 | 권장 |
|:--:|---|---|
| 1 | 음성을 텍스트로 변환 | Whisper CPP 또는 Crisp ASR (일본어) |
| 2 | 타이밍 정리 | 길이 제한 적용 / 최소 간격 적용 *(비-AI)* |
| 3 | 자동 번역 | 로컬이면 Ollama·llama.cpp / 품질 우선이면 DeepL·Claude·GPT |
| 4 | AI 검토 | 번역 후 한국어 교정 — 15줄씩, 제안을 골라 적용 |
| 5 | 개별 손질 | AI 도우미로 문제 줄만 — 특히 "읽기 속도 맞추기" |

3단계에서 **LLM 번역기를 쓰면 4단계 AI 검토의 효용이 줄어듭니다**(이미 문맥을
보고 번역했으므로). 반대로 DeepL·Google 같은 전용 번역기를 썼다면 4단계가
자연스러움을 크게 끌어올립니다.

---

## 소스 위치

| 기능 | 경로 |
|---|---|
| AI 검토 | `src/ui/Features/Tools/AiReview/` · 설정 `src/ui/Logic/Config/SeAiReview.cs` |
| AI 도우미 | `src/ui/Features/Main/AiAssistant/` |
| 자동 번역 | `src/libse/AutoTranslate/` · 목록 `src/ui/Features/Translate/AutoTranslateViewModel.cs` |
| 음성 인식 | `src/ui/Features/Video/SpeechToText/Engines/` · 목록 `SpeechToTextViewModel.cs` |
| 음성 합성 | `src/ui/Features/Video/TextToSpeech/Engines/` · 목록 `TextToSpeechViewModel.cs` |
| OCR | `src/ui/Features/Ocr/Engines/` · 종류 `OcrEngineType.cs` |
| 비디오 OCR | `src/ui/Features/Video/VideoOcr/` · 목록 `VideoOcrEngineItem.GetEngines()` |
| 엔진 다운로드 | `src/ui/Logic/Download/` |

> 엔진 목록은 생성자에서 `if (OperatingSystem.Is...)` 로 분기하므로, 정확한
> 목록은 위 ViewModel의 생성자를 보는 것이 가장 확실합니다.
