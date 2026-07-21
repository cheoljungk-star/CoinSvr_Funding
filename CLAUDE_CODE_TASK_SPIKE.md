# CoinSvr SpikeScalp 자동 분석 작업 (FundingHedger와 완전 별개 프로세스)

## 역할
너는 이 작업을 **1시간마다** 반복 실행한다(Windows Task Scheduler로 스케줄됨, FundingHedger의
6시간 주기 작업과는 완전히 별개의 스케줄/프로세스).
목표: `spike_scalp_results.jsonl`/`spike_scalp_skipped.jsonl`에 쌓인 결과를 분석해서
`spike_scalp_config.json`의 파라미터를 더 나은 방향으로 조금씩 조정한다.

⚠️ **이 작업은 FundingHedger 자동화와 절대 섞이지 않는다**: 읽는 파일, 쓰는 config, 기록하는
연속성 파일(`CLAUDE_SPIKE.md`)이 전부 다르다. `strategy_config.json`/`CLAUDE.md`/
`CLAUDE_CODE_TASK.md`는 이 작업에서 절대 건드리지 않는다.

## 절대 규칙 (위반 금지)
1. **C# 소스코드(.cs)는 절대 수정하지 않는다.** 오직 `spike_scalp_config.json`만 수정한다.
2. **빌드/재시작을 하지 않는다.** ⚠️ **2026-07-18 정정: 아래 "config 미반영 관련 중요 사실" 참고 -
   서비스는 사이클마다 config를 자동으로 다시 읽지 않는다(이전 문서의 착오). 그래도 이 작업은
   재시작 권한이 없으므로 절차상 규칙 자체는 동일하게 유지 - config 파일 값을 계산해 저장하는
   것까지만 하고, 실제 반영(재시작)은 사람 몫으로 남긴다.**
3. **`DailyLossLimitUsdt`, `MaxParamChangeRatio`, `MaxDailyLossPerSymbolUsdt` 세 필드는 절대 수정하지
   않는다.** 사람이 수동으로만 바꾸는 안전장치다.
   ⚠️ 2026-07-18 사람과의 대화 세션: 스킵사유의 55.2%가 이 한도 하나 때문이었고, 게다가 실제 카운터
   (`_dailyRealizedLossUsdt`)가 디스크에 저장 안 되는 메모리 변수라 **프로세스 재시작마다 리셋**되는
   것까지 겹쳐 "진짜 하루 단위 리스크 관리"로 기능하지 않고 있었음이 코드로 확인됨(`SpikeScalpManager.cs:53-54`).
   지금은 데이터 축적이 우선인 테스트 단계라 판단해 사람이 직접 `100→1000000`(사실상 무제한)으로
   올려둔 상태 - **이 값이 비정상적으로 커 보여도 자동으로 낮추지 말 것**, 사람이 다시 낮추라고
   명시하기 전까진 그대로 둔다.
   ⚠️ `MaxDailyLossPerSymbolUsdt`(2026-07-19 사람과의 대화 세션에서 코드로 신규 도입, 기본 10 USDT)도
   `_dailySymbolLossUsdt`라는 동일한 인메모리 변수로 추적되므로 **똑같이 프로세스 재시작마다
   리셋된다** - 위와 같은 이유로 이 값도 자동화가 건드리지 않는다.
4. 값 변경은 반드시 아래 "설정 반영 절차"를 따른다 — 직접 JSON을 덮어쓰지 말고, 계산 근거를 로그로 남긴 뒤 적용한다.
5. 확신이 서지 않으면(표본 부족, 방향성 불명확) **값을 바꾸지 않고 그대로 둔다.**
6. **`SpikeScalpManager.DebugDryRun` 플래그(코드상 값)는 이 작업의 범위 밖이다.** 이 값을 바꾸라는
   어떤 분석 결과가 나오더라도 절대 건드리지 않는다 — 이건 .cs 코드 값이고, 실거래 전환은 사람이
   직접 결정할 사안이다.

## 실행 경로 (중요)
이 작업은 `D:\000.WORK\000.NET\CoinSvr_Funding`(프로젝트 루트)에서 실행된다(FundingHedger의
`CLAUDE_CODE_TASK.md`와 동일 위치, `.bat`이 여기로 cd 함). 하지만 실제 데이터 파일들은
서비스 실행파일 기준 경로인 `CoinSvr\bin\Debug\net9.0\` 하위에 있다 - 파일 읽을 때 반드시
아래 상대경로를 붙여서 접근할 것:
- `CoinSvr\bin\Debug\net9.0\spike_scalp_results.jsonl`
- `CoinSvr\bin\Debug\net9.0\spike_scalp_skipped.jsonl`
- `CoinSvr\bin\Debug\net9.0\spike_scalp_config.json`
- `CoinSvr\bin\Debug\net9.0\spike_scalp_config_history.jsonl`
- `CoinSvr\bin\Debug\net9.0\DataArchive\<YYYYMMDD>\<파일명>`(2026-07-19 신설): `rotate_data_logs.ps1`이
  "오늘 이전" 날짜 레코드를 옮겨두는 곳(FundingHedger와 공유하는 로테이션 스크립트 - 6개 jsonl
  파일 전부를 한 번에 처리한다). `analyze_spike_cycle.py`가 필요할 때 자동으로 함께 읽는다.
- `CoinSvr\bin\Debug\net9.0\spike_analysis_state.json`(2026-07-19 신설): `analyze_spike_cycle.py`가
  관리하는 증분분석 컷오프 상태파일(`LastCutoffUtc`) - 이 작업이 자동으로 읽고 갱신하며, 사람이나
  이 작업이 직접 편집하지 않는다.
`CLAUDE_CODE_TASK_SPIKE.md`/`CLAUDE_SPIKE.md` 자체는 프로젝트 루트(cd된 위치)에 있다.

## 데이터 로그 로테이션 + 증분분석 (2026-07-19 신설, 매 사이클 필수 - 분석 절차의 실질적 0번째 단계)
`spike_scalp_results.jsonl`/`spike_scalp_skipped.jsonl`/`spike_scalp_postexit.jsonl`/
`spike_scalp_trendveto_sim.jsonl`은 계속 append만 되고 잘리지 않아 무한히 커진다(특히
`spike_scalp_skipped.jsonl`은 스킵이 워낙 잦아 순식간에 수십MB로 불어남). 아래 순서로 실행한다:
```powershell
powershell -File "D:\000.WORK\000.NET\CoinSvr_Funding\rotate_data_logs.ps1"
python "D:\000.WORK\000.NET\CoinSvr_Funding\analyze_spike_cycle.py"
```
1. `rotate_data_logs.ps1`(FundingHedger와 공유, 6개 jsonl 파일을 한 번에 처리)이 "오늘 이전" 날짜를
   `DataArchive\<YYYYMMDD>\`로 옮긴다. 서비스가 계속 append 중이어도 안전하다(내부 rename+병합복구,
   자세한 설명은 스크립트 상단 주석 참고). 옮길 게 없는 날은 빠르게 끝나므로 매 사이클 그냥 먼저
   실행할 것.
2. `analyze_spike_cycle.py`가 `spike_analysis_state.json`의 `LastCutoffUtc`를 자동으로 읽어 "직전
   사이클 이후 신규 레코드만" 가져오고(컷오프가 DataArchive로 옮겨진 과거 날짜에 걸치면 그 아카이브도
   자동으로 함께 읽음), 아래 "분석 절차"의 통계를 전부 계산해 출력한 뒤 종료 시 상태를 자동 갱신한다.
   **이전엔 매 사이클 CUTOFF 상수를 스크립트에 수동으로 적어넣어야 했는데(CLAUDE_SPIKE.md에서 직전
   컷오프를 찾아 파싱), 이제 그럴 필요가 없다.** 수동으로 특정 구간만 보고 싶으면
   `--since-hours N` 또는 `--cutoff <UTC ISO>` 옵션으로 상태파일을 무시하고 강제 지정 가능(임시
   분석용, 정상 사이클에서는 옵션 없이 실행).
⚠️ `spike_analysis_state.json`은 이 작업 전용 상태파일이다 - 값이 이상해 보여도 사람이 직접
수정하지 않는다(스크립트가 다음 실행에 자동으로 "now"로 갱신하므로 한 사이클 정도 넓은 창으로
분석되는 것은 정상 - 중복집계일 뿐 유실은 아님).

## 입력 파일
- `spike_scalp_results.jsonl`: 진입~청산이 완료된 케이스마다 한 줄(JSON). 주요 필드:
  - `Symbol, Side`: 심볼, 진입방향(Buy=모멘텀 상승추격, Sell=하락추격)
  - `SpikeChangePct`: 트리거 당시 60초간 가격변동%(부호 있음, 방향 판단 근거)
  - `EntryPrice`: 진입가(근사치 - 트리거가격, 실제체결가 아님에 주의)
  - `ExitReason`: `TRAIL`(트레일링스탑 히트) / `SL`(고정손절) / `TIMEOUT`(MaxHoldMs 도달)
  - `RealizedUsdt`: 실현손익(USDT 근사, 드라이런 중엔 가상)
  - `PeakPct`: 보유 중 최고 수익률(%) - giveback 비율 역산에 사용
  - `DirRsi14`/`DirPctB20`(2026-07-18 신규, **로그 전용·진입필터 미사용**): 1h캔들 종가 기준
    Wilder RSI(14)/20기간 볼린저 %B를 스파이크 방향 기준으로 재투영한 값(Buy면 그대로, Sell이면
    RSI는 `100-RSI`, %B는 `1-%B` - "값이 높을수록 그 방향으로 이미 과열/소진됐다"는 의미로 통일).
    같은 날 사람과의 대화 세션 백테스트(과거 트레이드 기준, RESULTS_PATH만 사용해 klines 역산)
    결론: 두 지표 모두 단조롭지 않고(극단값 근처에서만 뚜렷), **이미 배포된 1h추세필터
    (`TrendAligned1h`)가 잡아낸 위험군의 위험도를 더 세분화할 뿐 그 자체로 필터를 뒤집을 근거는
    아님** - 그래서 진입 로직엔 미반영, 사후분석 축적용으로만 로깅 시작. 진입 확정 시점에
    백그라운드 REST(`GetKlinesAsync`, 1h, limit 30)로 조회해 청산 시점 로그에 채워 넣는 방식이라,
    보유시간이 아주 짧은 트레이드는 REST 왕복이 못 끝나 `null`로 남을 수 있음(정상, 인프라이슈
    아님) - 표본 쌓이면 dir_rsi/%B 구간별 승률·평균수익 분석을 이 작업의 정기 분석 항목에 추가할지
    사람과 상의할 것(현재는 이 작업 범위에 아직 미포함).
- `spike_scalp_skipped.jsonl`: 스파이크는 감지됐으나(가격+거래대금 조건 충족) 진입 안 한 케이스.
  - `SkipReason`: `AlreadyActive`(이미 그 심볼 포지션 보유중) / `Cooldown`(재진입 대기중) /
    `MaxConcurrentPositions`(동시보유 상한) / `DailyLossLimit`(일일손실한도 소진) /
    `EntryFailed`(수량계산·레버리지설정·주문 실패) / `TrendAligned1h`(2026-07-18 신규, 아래 설명) /
    `SymbolDailyLossLimit`(2026-07-19 신규 - 특정 심볼이 하루 `MaxDailyLossPerSymbolUsdt` 이상
    손실나면 그 심볼만 당일 재진입 차단. 이월이슈(J) "특정 심볼 반복손실 집중" 대응용, 사람과의
    대화 세션에서 `.cs` 코드로 직접 추가 - 이 작업은 여전히 값만 조정하고 이 필드 자체를 켜고 끄지
    않는다)
  - ⚠️ **1h/24h 추세 필터 배경 (2026-07-18 사람과의 대화 세션에서 코드패치 + 방향 반전)**:
    처음엔 "24시간 추세와 반대방향 스파이크를 거부"하는 필터(`CounterTrend24h`)로 시작했으나,
    과거 완료 트레이드 1591건(2026-07-16~18)을 Binance 과거 1h Kline으로 역산해 1h/2h/6h/12h/24h
    다섯 윈도우를 백테스트한 결과 **정반대 방향**이 맞다는 게 확인됨: 짧은 윈도우일수록 "추세와
    반대(counter) 스파이크가 오히려 성과가 훨씬 좋다"는 신호가 강해지고(1h에서 gap -0.90으로
    최대, 24h는 -0.14로 최소), ExitReason 분해에서 "추세와 같은 방향(aligned)" 스파이크의 58.8%가
    SL로 끝나는 반면 추세역행은 26.3%뿐이라는 명확한 메커니즘도 확인됨(추세를 뒤늦게 쫓아가는
    스파이크는 반락 위험이 크고, 추세에 반하는 스파이크는 진짜 반전일 가능성이 높은 것으로 해석).
    이에 따라 **필터를 1h 기준·"추세와 같은 방향이면 거부"로 교체**함 — `SkipReason=TrendAligned1h`는
    `Trend1hPct`의 절댓값이 `Min1hAlignedVetoPct`(기본 0.1%) 이상이고 스파이크 방향이 그 1h추세와
    **같을 때** 스킵된 경우를 뜻한다(반대가 아니라 같은 방향일 때 거부하는 것에 주의 — 옛
    `CounterTrend24h`와는 조건 부등호 방향 자체가 다름).
  - `Trend24hPct`/`Trend1hPct`(2026-07-18 신규): 각각 24시간/1시간 변동률. `Trend24hPct`는 Binance
    24hr 티커 `PriceChangePercent`를 그대로 캐시(참고용, 필터에 미사용). `Trend1hPct`는 자체
    롤링버퍼(`Trend1hWindowSec`, 기본 3600초)로 계산되며 **실제 진입 필터의 근거**로 쓰인다(서비스
    재시작 직후 윈도우가 90% 이상 안 채워지면 값이 비어있을 수 있음 - 정상). 둘 다
    `spike_scalp_results.jsonl`/`spike_scalp_skipped.jsonl` 양쪽에 필터 작동 여부와 무관하게 항상
    기록되므로, `Min1hAlignedVetoPct` 임계값(기본 0.1%)이 적절한지 다음 몇 사이클에 걸쳐 계속
    검증할 것.
  - **이 파일이 왜 중요한가**: `AlreadyActive`/`MaxConcurrent`/`Cooldown` 비율이 높으면 "임계값이
    느슨해서 기회는 많은데 캡 때문에 못 먹는" 상황 - `MaxConcurrentPositions`를 늘리는 게 나을 수
    있음. 반대로 스킵이 거의 없고 `results.jsonl` 표본 자체가 적으면 "임계값이 너무 타이트해서
    애초에 트리거가 안 걸리는" 상황 - `SpikeThresholdPct`/`VolumeSpikeMultiplier`를 낮추는 게
    맞는 방향일 수 있음. 이 둘을 구분해서 판단할 것.
- `spike_scalp_config.json`: 현재 설정값(아래 튜닝 대상 참고)
- `spike_scalp_postexit.jsonl`(2026-07-18 신규): 청산 이후 가격추적 전용 파일. `spike_scalp_results.jsonl`과
  `(Symbol,Timestamp)` 조합으로 **1:1 조인** 가능(둘 다 `SpikeTarget.ExitTimestamp` 동일값 사용).
  - `Fwd30sPct`/`Fwd60sPct`/`Fwd180sPct`/`Fwd300sPct`: 청산 후 30/60/180/300초 시점 가격을, **원래
    포지션 방향 기준으로 재투영**한 변화율. **양수 = 청산 안 했으면 그 방향으로 계속 벌었을 것(조기청산/
    과도한 SL·TrailGivebackPct 신호), 음수 = 청산 이후 반전(그 타이밍에 나온 게 맞았다는 신호)**.
  - REST/틱 지연으로 광역스캔 버퍼에 해당 시점 데이터가 없으면 `null` - 정상(인프라이슈 아님).
  - `ExitPrice`는 체결가 재조회가 아니라 `pnlPct` 역산 근사치임에 유의(EntryPrice와 동일한 한계).
- `spike_scalp_trendveto_sim.jsonl`(2026-07-18 신규): "60초 윈도우로 잡는 스파이크는 대부분 잔파도라
  역방향(평균회귀)이 맞지만, 진짜 몇십%급 돌파는 순방향(추세추종)이 맞을 수 있는데 지금 구조로는
  그런 경우가 `TrendAligned1h`로 전량 거부당해 검증 데이터가 0건"이라는 사람과의 대화 세션 지적에서
  나온 가상추적 전용 파일 - **실주문 없음, 자본 리스크 없음**. `SkipReason=TrendAligned1h`로 거부된
  케이스 중 `|SpikeChangePct| >= LargeSpikeSimThresholdPct`(기본 3%)인 것만 대상으로, 스파이크 방향
  (=거부된 순방향/추세추종 방향)을 기준으로 거부 시점 가격 대비 30/60/180/300초 후 가격을 재투영.
  - `Fwd30sPct`/`Fwd60sPct`/`Fwd180sPct`/`Fwd300sPct`: **양수 = 순방향(추세추종)으로 탔으면 벌었을
    것(지금 거부 로직이 기회를 놓친 신호), 음수 = 거부한 게 맞았음(반락, 잔파도였다는 뜻)**.
  - `SpikeChangePct`(스파이크 크기, 부호는 원래 스파이크 방향), `Trend1hPct`(당시 1h추세)도 함께
    기록되므로, `spike_scalp_skipped.jsonl`의 `TrendAligned1h` 레코드와 `(Symbol,Timestamp)`가
    정확히 일치하진 않지만(이 파일은 별도 타이머 완료 시점에 기록) `Symbol`+`SpikeChangePct`+
    `Trend1hPct` 조합으로 근사 매칭 가능.
  - null은 광역스캔 버퍼 지연 등 정상 인프라 사유(청산후추적과 동일 관례).

## ⚠️ config 미반영 관련 중요 사실 (2026-07-18 사람과의 대화 세션에서 코드로 확인)
`FrmMain.cs:158`에서 `SpikeScalpConfig.LoadOrDefault()`는 **앱 시작 시 딱 한 번만** 호출되고,
`SpikeScalpManager._cfg`는 `readonly` 필드라 이후 절대 재할당되지 않는다(FundingHedger 쪽
`Fundinghedgemanager.cs:602`에는 주기적 재로드가 있지만 SpikeScalp엔 그런 코드가 없음). 즉
**`spike_scalp_config.json`을 이 작업이 수정해도, 사람이 CoinSvr.exe를 재시작하기 전까지는
살아있는 프로세스에 전혀 반영되지 않는다.** 지난 사이클들의 "X→Y로 조정, 즉시 반영 확인" 같은
기록은 실제로는 그 다음 재시작(재부팅/사람 개입) 시점에야 진짜로 적용됐을 가능성이 높다 - 즉
파라미터 변경의 실제 효과 측정 시점이 config파일 Timestamp가 아니라 **그 이후 가장 가까운 앱
재시작 시각**이라는 뜻이므로, 사이클 간 "직전 변경의 효과" 판단 시 이 시차를 감안할 것. 이 사실이
바뀌었다는 건 다음 재시작(사람이 언제 할지 이 작업은 알 수 없음) 시점에 실제로 config가 갱신됨을
뜻하며, 이 작업은 여전히 재시작을 시도해서는 안 된다(위 "절대 규칙" 2번 참고).

## 튜닝 대상 파라미터 (spike_scalp_config.json)
- `SpikeThresholdPct`(기본 0.8): 60초 내 이 이상 가격변동시 트리거 후보
- `VolumeSpikeMultiplier`(기본 3): 평소(15분) 60초당 거래대금 대비 이 배수 이상이면 트리거
- `StopLossPct`(기본 0.3): 고정 손절 - **사람이 명시적으로 "SL 값은 그대로 유지"라고 지시한 값이다.
  이 필드는 자동화가 바꾸지 않는다** (TP는 트레일링으로 전환됐으나 SL은 고정 유지가 원래 의도).
- `TrailArmPct`(기본 0.4): 트레일링 무장 최소 피크
- `TrailGivebackPct`(기본 30): 피크 대비 이 비율 반납시 청산
- `MaxHoldMs`(기본 180000=3분, 범위 60000~300000 즉 1~5분): 최대보유시간
- `CooldownMinutes`(2026-07-18 기준 4, 원래 기본 10 - 자동화가 10→9로 1차 조정 후 사람이 데이터
  축적 목적으로 9→4로 추가 인하, 클램프 미적용의 사람 직접지시): 같은 심볼 재진입 방지 시간.
  전체 스킵의 23.3%가 이 사유였음 - 낮출수록 표본수집 속도는 빨라지나 같은 심볼의 거의 동일한
  스파이크에 반복 재진입할 위험도 커짐(데이터 다양성보다 양이 늘어나는 트레이드오프임을 인지할 것).
- `MaxConcurrentPositions`(기본 3): 동시보유 상한
- `Min1hAlignedVetoPct`(기본 0.1, 2026-07-18 신규·설계변경): 1시간 변동률(`Trend1hPct`) 절댓값이
  이 값 이상이고 스파이크 방향이 그 1h추세와 **같으면**(반대 아님) 진입 스킵(`TrendAligned1h`).
  값을 키우면 필터가 느슨해지고(거의 안 걸림), 줄이면 더 많은 케이스가 걸러짐 - 백테스트에서는
  구간이 커질수록(|trend1h|≥2%) 효과가 더 뚜렷했으니(gap -1.82), 표본이 쌓이면 임계값을 올려
  "확실히 강한 추세일치만" 거르는 방향이 나을지도 검토할 것.
- `Trend1hWindowSec`(기본 3600): 1h 추세 측정 윈도우(초). 서비스 재시작 직후엔 이 시간만큼
  데이터가 쌓이기 전까지 `Trend1hPct`가 비어있어 필터가 작동하지 않는 게 정상.
- `LargeSpikeSimThresholdPct`(기본 3, 2026-07-18 신규): `TrendAligned1h`로 거부된 스파이크 중
  `spike_scalp_trendveto_sim.jsonl` 가상추적을 시작할 최소 `|SpikeChangePct|`. **실거래 필터에는
  영향 없음(가상추적 트리거 문턱일 뿐)** - 값을 낮추면 표본이 늘지만(2026-07-18 기준 3%↑ 약 247건
  vs 2%↑ 약 603건), 너무 낮추면 잔파도까지 섞여 "진짜 큰 돌파"라는 취지가 흐려짐. 표본이 계속
  부족하면 낮추는 걸 검토하되, 클램프 원칙(`MaxParamChangeRatio`)은 이 필드에도 동일 적용.
- `AlignedBreakoutMaxSpikePct`(기본 10, 2026-07-20 신규, 사람과의 대화 세션에서 `.cs` 코드 도입):
  `TrendAligned1h`에 걸릴 스파이크 중 `|SpikeChangePct|`가 `LargeSpikeSimThresholdPct`~이 값 사이면
  스킵 대신 **순방향 진입을 실제로 허용**한다(그 밖은 기존대로 스킵). trendveto_sim 1,413건 근거
  (3~5%대 Fwd300 +0.60%, 5~10%대 +0.29%, 10%+는 +0.05%로 무의미)로 도입 - `DailyLossLimitUsdt`/
  `MaxParamChangeRatio`/`MaxDailyLossPerSymbolUsdt`와 달리 **안전장치가 아니라 일반 튜닝
  파라미터**이므로 이 작업이 클램프 절차(±`MaxParamChangeRatio`)로 조정 검토 가능. 이 override로
  진입한 트레이드는 `spike_scalp_results.jsonl`의 `AlignedBreakoutOverride=true`로 표시되므로,
  매 사이클 이 필드가 있는 레코드만 따로 뽑아 승률/평균수익을 확인하고 이 값 조정 여부를 판단할 것
  (표본이 쌓이기 전까지는 "표본부족"으로만 기록).

## 분석 절차 (매 사이클 필수, 위 "데이터 로그 로테이션 + 증분분석" 절차로 먼저 스크립트를 실행한 뒤)
1. **표본 확인**: `analyze_spike_cycle.py` 출력의 "신규 results" 건수(직전 사이클 이후, 상태파일
   기준 자동 판단). 5건 미만이면 "표본부족"으로 기록하고 파라미터 변경 없이 종료.
2. **ExitReason별 분해**: `TRAIL`/`SL`/`TIMEOUT` 각각의 평균 `RealizedUsdt`, 승률, 평균 `PeakPct`.
   - `SL` 비율이 과도하게 높으면(예: 50%+) → 진입 타이밍 자체(임계값)가 너무 이르거나 늦은 신호를
     잡고 있을 가능성 - `SpikeThresholdPct`/`VolumeSpikeMultiplier` 조정 검토.
   - `TIMEOUT` 비율이 높고 그 케이스들의 평균 pnl이 미미하면(모멘텀이 안 붙고 옆으로 샘) →
     `MaxHoldMs`를 줄여 자본회전을 높이는 게 나을 수 있음.
   - `TRAIL` 승률·평균수익이 압도적으로 좋으면 → 임계값 자체는 유효한 신호를 잡고 있다는 뜻.
3. **giveback 비율 분석**: `(PeakPct - 실제pnl) / PeakPct`를 케이스별로 계산해 분포 확인 -
   `TrailGivebackPct` 30%가 너무 타이트(자주 조기청산)한지 너무 느슨(너무 많이 반납)한지 판단.
4. **스킵 사유 분해** (위 "입력 파일" 설명 참고) - 캡 문제 vs 임계값 문제 구분.
5. **방향별(Buy vs Sell) 비교**: 모멘텀 추격이 상승/하락 어느 쪽에서 더 잘 먹히는지 확인 -
   한쪽만 계속 손실이면 방향 자체를 재검토할 근거(단, 이 작업 범위에서 방향로직 변경은 안 함 -
   사람에게 보고만).
6. 표본이 충분히 쌓이면(각 구간 10건 이상) 파라미터 조정 여부 판단.
7. **RSI(14)/%B(20) 구간별 분석** (2026-07-18 신규, `DirRsi14`/`DirPctB20` 필드 - null인 레코드는
   제외): 2026-07-18 사람과의 대화 세션 백테스트(`backtest_indicators.py`, 과거 트레이드 1591건)와
   동일한 방식으로 아래를 매 사이클 계산한다:
   - 구간별(예: RSI `[0,30) [30,50) [50,60) [60,70) [70,80) [80,100]`, %B `[<0.2) [0.2,0.4) [0.4,0.6)
     [0.6,0.8) [0.8,1.0) [≥1.0)`) 승률·평균 `RealizedUsdt`
   - **`Trend1hPct` 정렬여부(1h추세와 스파이크 방향 일치 vs 반대)와의 교차분석**: RSI/%B가 낮음/중간/
     높음 구간별로 "1h일치(위험군)"와 "1h역행(안전군)"의 평균수익 격차(gap)를 비교 - 처음 백테스트
     결론(RSI≥70에서 gap -1.53까지 벌어짐, 즉 RSI 낮다고 위험군을 구제하진 않음)이 라이브 데이터에서도
     유지되는지 검증하는 게 핵심 목적.
   - 표본이 구간별 10건 미만이면 "표본부족"으로만 표기, 결론 내리지 않는다.
   - **이 지표를 진입 필터로 쓸지는 이 작업 범위 밖**(백테스트 결론: 기존 1h필터의 위험도를 세분화할
     뿐 뒤집을 근거는 아님) - 표본이 쌓여 백테스트 결론과 다른 신호가 뚜렷해지면 `⚠️ 전략재검토필요:`로
     플래그만 하고 실제 필터 반영은 사람이 결정한다.
8. **청산 후 가격추적(`spike_scalp_postexit.jsonl`) 분석** (2026-07-18 신규, "TP/SL 이후 데이터를
   안 남기면 giveback/SL 비율이 적정한지 판단 근거가 없다"는 지적으로 도입): `spike_scalp_results.jsonl`과
   `(Symbol,Timestamp)`로 조인해서
   - `ExitReason`별(TRAIL/SL/TIMEOUT)로 `Fwd30sPct`/`Fwd60sPct`/`Fwd180sPct`/`Fwd300sPct` 평균을 비교.
     **TRAIL 청산 건들의 Fwd값이 대체로 양수(추가로 계속 벌었을 방향)면 `TrailGivebackPct`가 너무
     타이트해서 조기청산하고 있다는 신호** - 반대로 대체로 음수/0 근처면 현재 값이 적정하거나 오히려
     더 타이트하게(반납 비율을 낮게) 가도 된다는 신호.
   - **SL 청산 건들의 Fwd값이 대체로 음수(청산 이후에도 계속 불리한 방향)면 SL 판단이 옳았다는 뜻**,
     반대로 양수가 많으면(청산 직후 반등) 손절이 너무 성급했을 가능성 - 단, `StopLossPct`는 사람이
     "그대로 유지" 지시한 값이라 이 분석 결과로도 자동 조정하지 않고 `⚠️` 플래그만 남긴다.
   - 표본 10건 미만 구간은 결론 보류.
9. **추세일치 거부 가상추적(`spike_scalp_trendveto_sim.jsonl`) 분석** (2026-07-18 신규, "짧은 60초
   윈도우 스파이크는 대부분 잔파도라 역방향이 맞지만 진짜 큰 돌파는 순방향이 맞을 수 있는데, 지금은
   그런 큰 스파이크도 추세일치면 전량 거부돼서 검증 데이터가 0건"이라는 지적에서 도입):
   - `Fwd30sPct`~`Fwd300sPct` 평균이 **대체로 양수면 "진짜 돌파는 순방향이 맞다"는 가설 지지** →
     `Min1hAlignedVetoPct`를 올려서(더 강한 추세일치만 거부) 이런 큰 스파이크를 필터에서 빠져나가게
     하거나, 별도의 "크기 기반 예외"를 두는 방향을 사람에게 제안할 근거가 됨.
   - **대체로 음수/0 근처면 현재 거부 로직이 맞다는 뜻** - 크기와 무관하게 추세일치 거부를 유지.
   - `SpikeChangePct` 크기로 하위 버킷을 나눠(예: `[3,5) [5,10) [10,∞)`) 버킷별 평균도 같이 본다 -
     "일정 크기 이상부터 방향이 뒤집히는 문턱"이 있는지가 핵심 질문.
   - 표본 10건 미만이면 "표본부족"으로만 표기. **이 분석 결과로 필터 로직 자체(부등호 방향, 추세일치
     기준)를 바꾸는 건 이 작업 범위 밖** - `⚠️ 전략재검토필요:`로 플래그만 하고 실제 반영은 사람이
     결정한다(`Min1hAlignedVetoPct` 수치 조정 자체는 기존처럼 이 작업이 클램프 내에서 할 수 있음).

## 설정 반영 절차 (FundingHedger와 동일 원칙 - 클램프 적용)
1. 분석 결과로 제안값을 만든다.
2. **직접 spike_scalp_config.json에 쓰지 않는다.** 각 파라미터 변경폭은
   `현재값 × MaxParamChangeRatio`(기본 20%)를 넘지 않게 수동으로 클램프해서 계산한다.
3. `spike_scalp_config.json`을 갱신한다.
4. `spike_scalp_config_history.jsonl`에 다음 형식으로 한 줄 추가(append, 기존 라인 유지):
   ```json
   {"Timestamp":"<UTC ISO>","Reason":"<근거 요약, 예: 최근1h 8건 중 SL비율 62%로 과다 - SpikeThresholdPct 0.8→0.9 상향>","Config":{...변경후 전체 config...}}
   ```

## 롤백 조건
최근 3개 사이클(3시간) 연속으로 "이번 사이클 평균 RealizedUsdt < 그 이전 3개 사이클 평균"이면,
`spike_scalp_config_history.jsonl`에서 4번째 이전 설정값으로 되돌리고 Reason에 "성과 악화로 롤백"을 명시.

## 세션 간 연속성 — `CLAUDE_SPIKE.md` + `spike_automation_summary.log`
FundingHedger가 `CLAUDE.md`(서사형 상세기록)와 `automation_summary.log`(사이클별 한줄요약, 압축용)
두 파일을 같이 쓰는 것과 동일하게, SpikeScalp도 두 파일을 같이 관리한다:

**1) `CLAUDE_SPIKE.md`** (프로젝트 루트) — 매 사이클 실행 후 아래 형식으로 append(타임스탬프는
UTC가 아니라 **로컬시간(KST, UTC+9)** 으로 기록, 예: `[2026-07-18T12:34:56+09:00]`):
```
## 자동분석 사이클 로그 [<로컬시간(KST) 타임스탬프>]
표본: 신규 N건(results)/M건(skipped). ExitReason 분해: TRAIL x건/SL y건/TIMEOUT z건, 평균pnl...
발견한 패턴: ...
파라미터 변경: <있으면 무엇을 왜, 없으면 "없음 - 표본부족/확신부족">
롤백: <해당하면 명시, 아니면 "해당없음">
```
파일이 15~20KB를 넘으면(FundingHedger의 CLAUDE.md 압축 관례와 동일 기준) 오래된 회차를
`D:\000.WORK\000.NET\CoinSvr_Funding\backup\CLAUDE_SPIKE_ARCHIVE_<날짜>.md`로 옮기고
(`backup` 폴더가 없으면 먼저 생성) 요약만 남긴다.

**2) `spike_automation_summary.log`** (프로젝트 루트, 텍스트 로그) — 매 사이클 끝에 한 줄만
append(타임스탬프는 동일하게 로컬시간(KST)로 기록):
```
[<로컬시간(KST) 타임스탬프>] N회차 - 신규 X건/스킵 Y건 - ExitReason TRAIL a/SL b/TIMEOUT c - 순손익 Z USDT - 변경: <한줄요약 또는 "없음">
```
이 파일도 20KB 넘으면 오래된 줄을
`D:\000.WORK\000.NET\CoinSvr_Funding\backup\SPIKE_AUTOMATION_SUMMARY_ARCHIVE_<날짜>.log`로 옮기고
(`backup` 폴더가 없으면 먼저 생성) 최근 것만 남긴다(FundingHedger의 automation_summary.log
압축 관례와 동일). 기존에 이미 UTC로 적힌 줄들은 그대로 두고 변환하지 않는다 — 신규 기록분부터만
적용.
**두 파일의 역할 구분**: `CLAUDE_SPIKE.md`는 다음 세션이 "왜 그렇게 판단했는지" 맥락을 이해하기
위한 상세기록, `spike_automation_summary.log`는 사람이 터미널에서 `tail`로 빠르게 훑어보기 위한
압축요약 - 두 파일 다 매 사이클 반드시 갱신한다(하나만 쓰고 끝내지 않는다).

## 하지 말아야 할 것
- `Fundinghedger_.cs`, `Fundinghedgemanager.cs`, `StrategyConfig.cs`, `strategy_config.json`,
  `CLAUDE.md`, `CLAUDE_CODE_TASK.md`, `automation_summary.log` — 이 작업에서 절대 읽거나 쓰지
  않는다(FundingHedger 작업 전용).
- `SpikeScalpManager.cs`/`SpikeScalpConfig.cs` 등 .cs 파일 수정.
- `DebugDryRun` 값 변경(코드값이라 이 작업 범위 밖).
- 표본 부족 상태에서 파라미터 확정 변경.
