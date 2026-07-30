# upstream 동기화 절차서

> 2026-07-30 의 rc18 → v5.1.0 동기화(116커밋)를 실제로 수행하며 만든 것입니다. 그 회차의 결과와
> 판단 근거는 `CLAUDE.md` 의 *Upstream sync rc18 → v5.1.0* 절에 있습니다. 이 문서는 **다음 회차에
> 그대로 따라 할 순서**입니다.

## 0. 먼저 규모를 재라 — 이 숫자가 전략을 정한다

```powershell
git fetch upstream
$base = git merge-base HEAD upstream/main
git rev-list --count HEAD..upstream/main              # upstream 이 앞선 커밋
$ours   = git diff --name-only $base HEAD
$theirs = git diff --name-only $base upstream/main
($ours | Where-Object { $theirs -contains $_ }).Count  # ★겹치는 파일 = 통증 지표
```

겹치는 파일별로 양쪽 diff 크기를 뽑아 두면 어디가 위험한지 미리 보입니다.

| 회차 | upstream 커밋 | 포크 파일 | upstream 파일 | 겹침 | 결과 |
|---|---|---|---|---|---|
| rc17 → rc18 | 66 | 67 | 107 | 6 | rebase, 충돌 0 |
| rc18 → v5.1.0 | 116 | 117 | 444 | **19** | **merge, 충돌 2** (rebase 는 5/34 에서 멈춤) |

## 1. 백업하고 별도 브랜치에서 한다

```powershell
git tag -f backup/pre-upstream-sync-<날짜> main
git checkout -b sync/upstream-<날짜>
git merge upstream/main --no-ff
```

`main` 은 끝까지 손대지 않습니다. 도중에 그만두어도 잃는 것이 없어야 합니다.

**merge 를 쓰는 이유는 `CLAUDE.md` *Fork policy* 5번**에 있습니다. rebase 를 다시 시도하려면
겹침 숫자가 다시 한 자리로 내려온 뒤에 하십시오.

## 2. 충돌 해소 — upstream 이 왜 그랬는지 먼저 확인한다

포크 줄과 upstream 삭제가 부딪히면 **포크 줄을 지키는 것이 기본이 아닙니다.** upstream 이 지운
이유를 확인해야 합니다. 2026-07-30 의 `Se.cs` 가 그 예입니다:

```powershell
git grep -n '<사라진 심볼>' upstream/main -- src   # upstream 에 남아 있나
git grep -n '<사라진 심볼>' main -- src            # 포크에서 읽는 곳이 있나
git log --oneline -3 upstream/main -- <파일>       # 어느 커밋이 지웠나
```

세 답이 "없음 / 없음 / 의도적 삭제"였으므로 포크 줄을 버렸습니다.

## 3. 빌드 — **반드시 `--no-incremental`**

```powershell
dotnet build --configuration Release --no-incremental
```

증분 빌드는 입력이 안 바뀐 프로젝트를 건너뛰고 **그 프로젝트의 경고도 같이 사라집니다.**
2026-07-30 에 이것 때문에 "경고 0"을 여러 번 잘못 읽었습니다.

경고가 늘었으면 **포크 파일 탓인지 upstream 탓인지 가릴 것**:

```powershell
# 포크가 추가한 경로에 경고가 있나 (있으면 그것만 고친다)
'JavData','NameCheck','SpeechRegister','Theming\Nakatashi' | ...
```

upstream 이 들여온 경고는 **갚지 않습니다**(*Fork policy* 6번). 새 기준선 숫자를 `CLAUDE.md` 에
적으십시오.

## 4. 메뉴 인벤토리 — 그물이 울리는 것이 정상이다

upstream 이 명령을 추가하면 `MainMenu_HasNoCommandsMissingFromBaseline` 이 실패합니다. **그것이
이 그물의 목적입니다.** 새 명령에 자리를 준 뒤:

```powershell
./tools/build.ps1 menu-baseline
git diff -- tests/UI/Features/Main/Layout/main-menu-inventory.baseline.txt
```

**추가 줄만 있어야 합니다. 삭제 줄은 기능 상실입니다.**

★ 이 태스크는 베이스라인을 **CRLF 로 씁니다.** 이 레포는 LF 전용이므로 되돌릴 것:

```powershell
$p = 'tests/UI/Features/Main/Layout/main-menu-inventory.baseline.txt'
[IO.File]::WriteAllText((Resolve-Path $p), ([IO.File]::ReadAllText((Resolve-Path $p)) -replace "`r`n","`n"), (New-Object Text.UTF8Encoding $false))
```

## 5. 위생 검사

- `packages.lock.json` — `git checkout --` **금지**(정당한 변경까지 날아간다).
  `checkout` → `dotnet restore` → LF 변환 순서. NuGet 이 CRLF 로 재작성한다.
- 병합된 upstream 파일의 **BOM·줄끝은 upstream 그대로여야** 한다. 표본으로 대조:
  `git cat-file blob upstream/main:<파일>` 과 바이트 비교. upstream 은 BOM 파일이 많고
  `Italian.json` 은 정당하게 CRLF 다 — 그것을 "고치면" 그게 오염이다.
- 포크의 언어 절이 살아 있는지: JSON 키 ↔ C# 속성명(camelCase) 대조. 어긋난 키는 그 라벨만
  조용히 영어로 남는다.
- `Languages\version.txt` 함정: 언어 파일이 바뀌어도 버전이 같으면 재전개가 안 된다.

## 6. 착지

```powershell
dotnet test --configuration Release            # 알려진 VobSub 1건만 실패해야 한다
git checkout main; git merge --ff-only sync/upstream-<날짜>
git push origin main
```

그리고 `CLAUDE.md` 에 회차 기록을 남깁니다 — 커밋 수, 겹침 수, 충돌과 그 해소 근거, 새 기준선
숫자(테스트·경고·메뉴 명령), 걸린 함정.
