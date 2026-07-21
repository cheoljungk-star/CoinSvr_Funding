# CoinSvr 코드수정 "제안" 자동 사이클 (FundingHedger/SpikeScalp와 완전 별개 프로세스)

## 역할
너는 이 작업을 **6시간마다** 반복 실행한다(Windows Task Scheduler `CoinSvr_ClaudeCode_CodeProposal`로
스케줄됨, FundingHedger의 6시간 주기 튜닝 작업·SpikeScalp의 1시간 주기 작업과는 완전히 별개의
스케줄/프로세스).

목표: 이미 다른 두 자동화(`CLAUDE_CODE_TASK.md`, `CLAUDE_CODE_TASK_SPIKE.md`)가 쌓아온 분석
결과(`CLAUDE.md`, `CLAUDE_SPIKE.md`, `automation_summary.log`, `spike_automation_summary.log`)를
읽고, 아래 두 좁은 카테고리에 한해 **코드 수정 "제안서"** 를 `code_proposals/` 폴더에 markdown
파일로 남긴다. **실제 `.cs` 파일은 절대 건드리지 않는다** — 제안만 만들고, 실제 반영은 사람이
대화 세션에서 검토·승인한 뒤에만 이뤄진다.

## ⚠️ 이 작업이 존재하는 이유 (혼동 금지)
- FundingHedger/SpikeScalp 자동화는 `strategy_config.json`/`spike_scalp_config.json`의 **숫자
  파라미터만** 조정한다(`.cs` 수정 절대 금지 — 이 규칙은 이 작업에도 동일하게 적용됨).
- 이 작업은 그 두 자동화가 수집한 데이터/판단을 근거로, config에 노출되지 않은 **아주 좁은 범위의
  코드 수정 아이디어**를 제안서 형태로만 생성한다. 이 작업도 `.cs`를 직접 쓰지 않는다는 점에서
  다른 두 작업과 동일한 안전 원칙을 공유한다 — 차이는 "제안서라는 산출물을 새로 만든다"는 것뿐이다.

## 절대 규칙 (위반 금지)
1. **`.cs` 파일에 절대 쓰지 않는다.** 이 작업이 만들 수 있는 파일은 오직
   `code_proposals/<UTC타임스탬프>_<slug>.md` 신규 파일과, 아래 "세션 간 연속성" 섹션에서 설명하는
   `CLAUDE_CODEPROPOSAL.md`/`code_proposal_automation_summary.log` 갱신뿐이다.
2. **빌드/재시작을 하지 않는다.** 이 작업은 컴파일도, 서비스 재시작도 하지 않는다 — 그건 사람이
   제안을 승인한 후 별도 세션에서 할 일이다.
3. **`strategy_config.json`/`spike_scalp_config.json`/`CLAUDE.md`/`CLAUDE_SPIKE.md`/
   `CLAUDE_CODE_TASK.md`/`CLAUDE_CODE_TASK_SPIKE.md`는 읽기 전용으로만 참고한다.** 이 파일들에
   쓰지 않는다(다른 두 자동화 전용 파일).
4. **아래 "절대 접근 금지 블랙리스트"에 해당하는 파일:메서드에 관련된 어떤 수정도 제안하지 않는다.**
   블랙리스트에 걸리는지 애매하면 제안 자체를 만들지 않는다(확신 없으면 안 만드는 게 기본값).
5. **`DebugDryRun` 플래그(두 클래스 모두)는 이 작업의 범위 밖이다.** 이 값을 바꾸라는 어떤 분석
   결과가 나오더라도 절대 제안조차 하지 않는다 — 실거래 전환은 사람이 직접 결정할 사안이다.
6. **새 config 필드 추가도 제안하지 않는다.** config에 새 필드를 추가하는 건 구조적 기능 추가라
   상수 튜닝보다 범위가 넓다 — 이 작업은 "이미 있는 하드코딩 상수" 또는 "로그에 필드 추가"만
   다룬다.
7. 확신이 서지 않으면(근거 회차가 1~2개뿐이거나, 패턴이 명확하지 않으면) **제안을 만들지 않고
   이번 사이클은 0건으로 종료한다.** 제안을 만드는 것보다 안 만드는 게 기본값이다.

## 절대 접근 금지 블랙리스트 (이 목록에 걸리면 제안 자체를 만들지 않음)
- `CoinSvr/Fundinghedger .cs`: `PlaceOrderAsync`, `EnterAsync`, `ExecuteExitAsync`, `PrepareEntry`,
  `SetBaitMode`, `ResetBaitMode`, `RecalcBaitQty`, `ExecuteBaitEntryAsync`, `CloseBaitAsync`,
  `RunBaitOnlyCloseAsync`, `ExecuteEntryAsync`, `WaitForEntryMomentumAsync`, `CloseAsync`,
  `CloseMainAsync`, 그리고 14번째 줄 `public static bool DebugDryRun` 필드 선언부.
- `CoinSvr/Fundinghedgemanager.cs`: `PlaceOrderViaSocketAsync`(미끼전용 dry-run 게이팅 포함 전체),
  `RunCloseAsync`, `PlaceOrderViaRestAsync`, `EnsureLeverageAsync` 계열, 마진/레버리지 설정 호출.
- `CoinSvr/SpikeScalpManager.cs`: `PlaceOrderAsync`(매니저), `EnterAsync`, `ExecuteExitAsync`,
  `OnPriceUpdate`의 SL/TRAIL 판정 분기, `RunTimeoutWatchdogAsync`의 청산 트리거, 28번째 줄
  `public static bool DebugDryRun` 필드 선언부.
- 수량/방향 결정 로직 전체: `CalcQty`, `GetStepSize`, `FloorToStep`, 후보 심볼 선정
  (`OnWidePriceUpdate`/`TryPromoteToTargetAsync`의 `side` 결정부), `ScanAndDispatchAsync`의
  진입방향 결정부.
- 같은 솔루션 내 다른 트레이딩 모듈(FundingHedger/SpikeScalp 소관 밖이지만 실행 관련은 전부 동일하게
  제외): `select_TRANSACTION.cs`의 `PlaceFuturesOrder_LONG_HEDGE/SHORT_HEDGE/CLOSE_HEDGE/LONG/SHORT/TP`,
  `HedgeGridManager.cs`의 `PlaceFuturesOrder_LONG_HEDGE`/`PlaceFuturesOrder_CLOSE_HEDGE`, `Ob.cs`의
  `PlaceMarketOrderAsync`/`PlaceStopOrderAsync`/`AmendStopOrderAsync`/`CancelAll`.
- `StrategyConfig.cs`/`SpikeScalpConfig.cs`의 `DailyLossLimitUsdt`/`MaxParamChangeRatio` 필드.

## 허용 카테고리 판단 체크리스트 (제안 전 반드시 자가검증)
1. 이 값을 바꾸면 실제 주문의 수량/방향/타이밍이 달라지는가? → **Yes면 제안 금지.**
   (예: 청산 루프의 폴링/대기 시간처럼 실행 타이밍에 직접 관여하는 인라인 매직넘버는 애매하니
   기본적으로 보류 — 예: `Fundinghedgemanager.cs`의 청산 대기 관련 `Task.Delay(...)`류.)
2. 이 필드가 로그/JSON 레코드에 값을 "추가"만 하는가(기존 필드 삭제나 의미 변경이 아님)? →
   **그것만 허용.**
3. 이미 `strategy_config.json`/`spike_scalp_config.json`에 있는 필드인가? → 있으면 이건 기존
   6h/1h 사이클 소관이라 이 작업에서 다루지 않는다.
4. 이미 `private const`로 분리돼 있고 실행경로에 간접적으로만 영향을 주는 상수인가?(예:
   `Fundinghedger .cs`의 `TRAIL_MIN_GIVEBACK_ABS_PCT` 같은, giveback 계산의 분모 보호용 상수) →
   이런 성격이 전형적인 허용 후보다.

## 입력 파일 (새로 분석하지 않고 기존 결과를 재사용한다)
- `CLAUDE.md`, `CLAUDE_SPIKE.md`: "다음 사이클이 알아야 할 것" / `⚠️ 전략재검토필요:` 로 누적
  표시된 항목들 — 여러 회차에 걸쳐 반복된 패턴만 이 작업의 근거로 쓴다(1~2회차짜리 단발성 관찰은
  근거로 부족).
- `automation_summary.log`, `spike_automation_summary.log`: 최근 사이클 요약(전체 재독 금지 —
  `tail -n <적당한 줄수>`만 사용, `CLAUDE_CODE_TASK.md`의 "파일 읽기 예산" 규칙을 그대로 따른다).
- 각 소스파일(`Fundinghedger .cs`, `Fundinghedgemanager.cs`, `SpikeScalpManager.cs`)은 제안
  대상 코드의 정확한 현재 스니펫을 인용하기 위해서만 읽는다(수정 목적이 아니라 인용 목적).

## 제안서 파일 형식
`code_proposals/<UTC타임스탬프>_<slug>.md` (신규 파일만 생성, 기존 파일은 절대 덮어쓰지 않는다):

```markdown
# 코드수정 제안: <한줄요약>
Status: PENDING_REVIEW
근거 회차: <CLAUDE.md 또는 CLAUDE_SPIKE.md의 N회차 참조>
카테고리: 하드코딩상수 | 로그필드추가

## 대상
파일: <경로>
위치: <메서드명/줄 근처>

## 현재 코드
​```csharp
<스니펫>
​```

## 제안 코드
​```csharp
<스니펫>
​```

## 근거
<왜 이 변경이 필요한지, 어떤 데이터 패턴(몇 회차, 몇 건)에서 나왔는지>

## 안전성 자가확인
- [ ] 주문 수량/방향/타이밍에 영향 없음
- [ ] DebugDryRun 미접근
- [ ] 블랙리스트 메서드 미접근
```

`Status:` 필드는 생성 시 `PENDING_REVIEW`로 1회만 쓰고, 이후 이 자동화가 다시 건드리지 않는다
(사람이 검토 후 `REVIEWED_APPLIED`/`REVIEWED_REJECTED`로 직접 고친다).

## 매 사이클 절차
1. `CLAUDE.md`/`CLAUDE_SPIKE.md`의 최근 누적 내용에서 "여러 회차 반복 확인된 패턴"이 있는지 확인.
2. 후보가 있으면 위 체크리스트 4개 항목을 전부 통과하는지 검증.
3. 통과하면 `code_proposals/`에 제안서 1개(사이클당 가급적 1~2건 이내로 제한 — 한 번에 여러 개를
   쏟아내지 않는다) 생성.
4. 통과 못 하거나 애초에 근거가 부족하면 이번 사이클은 제안 0건으로 종료 — 이것도 정상적인 결과다.
5. `code_proposal_automation_summary.log`(프로젝트 루트, 신규 파일, append)에 한 줄 기록:
   ```
   [<로컬시간(KST) 타임스탬프>] N회차 - 검토 M건 - 신규제안 X건(<파일명들>) 또는 "제안 없음(사유)"
   ```
6. 제안이 1건 이상이면 `automation_summary.log`(FundingHedger 전용 파일, **읽기는 하되 이 줄만
   append 허용** — 다른 내용은 절대 건드리지 않는다)에도 눈에 띄는 플래그를 추가:
   ```
   🔧 코드수정제안 대기중(N건 신규): code_proposals/<파일명> 등 — 다음 대화 세션에서 검토 필요
   ```
   (이 줄 추가가 애매하거나 위험하다고 판단되면 생략하고 `code_proposal_automation_summary.log`
   만으로 대체해도 무방 — 다른 자동화 파일을 건드리는 것 자체가 조심스러우면 하지 않는 쪽을 택한다.)
7. **제안이 1건 이상 생성된 사이클에서만** `PushNotification` 도구를 호출해 사람에게 모바일 알림을
   보낸다(status는 반드시 `"proactive"`). 메시지는 200자 이내 한 줄, 무엇을 검토해야 하는지 바로
   알 수 있게: 예) `"🔧 코드수정제안 N건 대기중: <파일명 요약> — code_proposals/ 검토 필요"`.
   **제안 0건인 사이클에는 절대 알림을 보내지 않는다** — 매 6시간마다 "이번엔 없음" 알림이 오면
   금방 무시하게 되므로, 알림은 실제로 검토할 게 생겼을 때만 값어치가 있다. 이 도구 호출 자체가
   실패하거나 사용 불가능하면(예: 오류 반환) 조용히 넘어가고 위 5·6번 로그 기록만으로 충분하다 —
   재시도하거나 다른 방식으로 알리려 하지 않는다.

## 세션 간 연속성 — `CLAUDE_CODEPROPOSAL.md`
매 사이클 종료 시 `CLAUDE_CODEPROPOSAL.md`(프로젝트 루트) 끝에 append(기존 내용 삭제 금지):
```markdown
## 코드제안 사이클 로그 [<로컬시간(KST) 타임스탬프>]
- 검토한 근거: <CLAUDE.md/CLAUDE_SPIKE.md 몇 회차를 봤는지>
- 신규 제안: <건수 및 파일명, 없으면 "없음 - 사유">
- 다음 사이클이 알아야 할 것: <이번에 보류한 후보가 있다면 왜 보류했는지, 표본이 더 쌓이면
  재검토할 것 등>
```
파일이 15~20KB를 넘으면(다른 두 연속성 파일과 동일 관례) 오래된 절반을
`D:\000.WORK\000.NET\CoinSvr_Funding\backup\CLAUDE_CODEPROPOSAL_ARCHIVE_<날짜>.md`로 옮기고
최근 것만 남긴다(`backup` 폴더는 이미 존재).

## 하지 말아야 할 것
- `.cs` 파일에 직접 쓰기, 빌드, 서비스 재시작 — 전부 금지.
- 위 블랙리스트에 해당하는 파일:메서드 근처의 어떤 수정도 제안하지 않기.
- `DebugDryRun` 값 변경 제안(코드값이라 이 작업 범위 밖).
- 새 config 필드 추가 제안(구조적 변경은 사람과의 대화에서만 결정).
- 한 사이클에 여러 건을 무리하게 만들어내기 — 근거 부족하면 0건이 정상.
- `strategy_config.json`/`spike_scalp_config.json`/`CLAUDE.md`/`CLAUDE_SPIKE.md`/
  `CLAUDE_CODE_TASK.md`/`CLAUDE_CODE_TASK_SPIKE.md`에 쓰기(읽기만 허용).
