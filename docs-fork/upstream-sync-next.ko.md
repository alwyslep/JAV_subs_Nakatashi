# 다음 upstream 동기화 — 사전 조사 (2026-07-30)

> 상태: **조사 완료, 실행 안 함.** rebase 를 실제로 시작해 첫 충돌까지 진단한 뒤 되돌렸습니다
> (`main` 무손상, 백업 태그 `backup/pre-upstream-sync-20260730`).
> 이 문서의 목적은 다음 사람이 **판단부터 다시 하지 않게** 하는 것입니다.

## 1. 규모

| 항목 | 값 |
|---|---|
| 현재 기준 | `93f828633` (rc18) |
| upstream 이 앞선 커밋 | **116** |
| 포크가 건드린 파일 | 117 |
| upstream 이 건드린 파일 | 444 |
| **겹치는 파일** | **19** |
| 재생될 포크 커밋 (rebase 시) | **34** |

지난 rc17 → rc18 동기화는 겹침 **6개**였고 충돌 0이었습니다. 겹침이 3배가 된 것은 upstream 이
난폭해진 탓이 아니라 **포크가 커졌기 때문**입니다(67 → 117 파일).

## 2. 겹치는 파일 19개 — 아픈 순서

| 파일 | 포크 | upstream | 비고 |
|---|---|---|---|
| `Features/Main/MainViewModel.cs` | +73/−1 | **+247/−53** | 가장 위험. 양쪽 다 큼 |
| `Features/Options/Settings/SettingsViewModel.cs` | +13/−1 | +122/−0 | |
| `Logic/Config/Se.cs` | +5/−1 | **+14/−60** | 삭제 구간에 포크 줄이 있다 — 아래 4장 |
| `Features/Main/Layout/InitMenu.cs` | **+324/−205** | +16/−0 | 포크의 최심부. upstream 이 처음으로 건드렸다 |
| `Features/Main/Layout/InitListViewAndEditBox.cs` | +102/−5 | +6/−0 | |
| `UI.csproj` | +20/−0 | +26/−5 | |
| `packages.lock.json` | +47/−0 | +47/−47 | 충돌 확정. 재생성할 것(함정 참조) |
| `Assets/Languages/Korean.json` | +49/−7 | +1/−1 | |
| `Features/Translate/TranslateSettingsViewModel.cs` | +40/−0 | +1/−1 | |
| `Features/Shared/MediaInfoView/MediaInfoViewViewModel.cs` | +43/−0 | +1/−0 | |
| `Features/Main/AiAssistant/AiAssistantViewModel.cs` | +14/−0 | +1/−1 | |
| `Features/Tools/FixCommonErrors/FixCommonErrorsWindow.cs` | +9/−2 | +16/−0 | |
| `Logic/Config/SeGeneral.cs` | +9/−1 | +9/−0 | |
| `Features/SpellCheck/SpellCheckViewModel.cs` | +9/−0 | +1/−0 | |
| `Features/Ocr/OcrViewModel.cs` | +7/−3 | +3/−2 | 지난 동기화에서도 겹쳤다 |
| `Logic/Config/SeTools.cs` | +2/−0 | +8/−0 | |
| `DependencyInjectionExtensions.cs` | +2/−0 | +5/−0 | |
| `Logic/Config/Language/Tools/LanguageTools.cs` | +2/−0 | +1/−0 | |
| `Features/Main/Layout/InitNativeMacMenu.cs` | +9/−0 | +3/−0 | mac 미러 — 포크 정책상 갱신 안 함 |

## 3. 전략 판단 — rebase 인가 merge 인가

`CLAUDE.md` 는 rebase 를 전제로 씌어 있고, 그 근거는 *"divergence 를 작게 유지하면 rebase 가 계속
작동한다"* 였습니다. **그 전제가 변했습니다** — 34 커밋 × 19 겹침이면 충돌이 커밋마다 되풀이됩니다
(실제로 5/34 에서 첫 충돌). merge 는 **한 번의 해소 패스**로 끝나고 포크의 커밋 메시지(이 포크의
사실상 설계 문서)가 그대로 남습니다. 대가는 `main` 에 병합 커밋이 생기고 이후 rebase 가 더
어려워지는 것입니다.

**이것은 워크플로에 대한 결정이라 사용자 확인 없이 바꾸지 않았습니다.** 선택지는 셋:

1. **rebase 계속** — 정본 이력 유지, 충돌 라운드 다수. 시간이 가장 많이 든다.
2. **merge 로 전환** — 한 패스. `CLAUDE.md` 의 *Fork policy* 1·2번을 갱신해야 한다.
3. **포크 커밋을 squash 한 뒤 rebase** — 34 → 몇 개로 줄여 rebase 를 싸게 만든다. 이력의
   설명력을 잃는 대가로 정본성을 지킨다.

## 4. 첫 충돌은 이미 진단됐다 (`Se.cs`)

`b20c28cd2`(Phase 1 테마)가 넣은 줄과 upstream 의 삭제가 부딪힙니다.

```
Configuration.Settings.General.UseDarkTheme = Settings.Appearance.Theme == "Dark"
    || Theming.Nakatashi.NakatashiTheme.IsNakatashiThemeName(Settings.Appearance.Theme);
```

**해소: upstream 의 삭제를 수용하고 이 줄을 버린다.** 근거 셋을 확인했습니다.

- upstream `3acb82e0c` *"Trim libse GeneralSettings/ToolsSettings to what the library reads"* 가
  `GeneralSettings.cs` 에서 **715줄을 삭제**하며 `UseDarkTheme` 속성 자체를 없앴다.
- `git grep UseDarkTheme` 결과, 포크에서 이 값을 **읽는 곳이 하나도 없다**(정의 1곳 + 초기화 1곳
  + 이 대입 1곳). upstream 이 지운 이유가 바로 그것이다.
- Phase 1 이 걱정한 "플러그인이 밝게 렌더된다"는 `PluginThemeColorsFactory.cs` 가 따로 처리한다.
  그쪽은 이 대입과 무관하다.

## 5. 동기화 중 반드시 확인할 것

- **메뉴 인벤토리 안전망**이 upstream 의 새 명령을 잡아낼 것이다(`InitMenu.cs` theirs +16). 그게
  이 그물의 존재 이유다 — 새 명령에 자리를 준 뒤 베이스라인을 갱신하고, `git diff` 에서
  **삭제된 줄이 없는지** 확인한다. 현재 146.
- **`packages.lock.json`**: `git checkout --` 로 되돌리지 말 것(정당한 변경까지 날아간다).
  `checkout` → `dotnet restore` → **LF 변환** 순서. NuGet 이 CRLF 로 재작성한다.
- **`77244c5ec chore/language-version-5.1.0`** 이 언어 버전을 건드렸다. 이 포크의
  `Languages\version.txt` 함정과 직접 관련이 있으니 동기화 후 언어 파일이 실제로 재전개되는지
  확인할 것 — 그리고 포크가 추가한 `nameCheck`·`speechRegister` 절이 살아 있는지 대조할 것
  (JSON 키 ↔ C# 속성명, 현재 16/16 · 22/22).
- **BOM·줄끝**: 이 레포는 LF 전용, BOM 없음. 대량 해소 후 변경 파일 전수 확인.
- **알려진 실패 1건**(VobSub)은 그대로일 것으로 예상. 늘어나면 동기화가 원인이다.
- `InitNativeMacMenu.cs` 는 **의도적으로 갱신하지 않는다**(포크 정책).
