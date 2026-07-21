## 2026-07-20 사람과의 대화 세션 - 코드버그 2건 근본원인 규명+수정, giveback 파라미터 조정
사람이 "지금까지 모인거 분석해보자"로 시작한 세션에서 신규데이터 분석(trade 2건+skip 1건, 표본
부족으로 파라미터 자동조정 대상 아님) 도중 두 가지를 지목해 해결 요청 - 코드베이스를 직접 조사·수정함.

**1. `Collection was modified` 스캔예외(30~37회차 누적 5건) 근본원인 특정+수정 완료**:
`Fundinghedgemanager.cs`의 `DispatchAsync` 내 미끼선정 루프(`foreach (var mult in baitMultipliers)
{ foreach (var c in baitCandidates) { ... baitCandidates.Remove(c); ... } }`)에서 **바깥 foreach가
순회 중인 리스트를 안쪽에서 직접 `Remove`하고 있었음** - 미끼 진입 실패 후 다음 후보로 넘어가려던
의도였으나 C#에서 foreach 순회 중 원본 컬렉션 수정은 다음 `MoveNext()`에서 곧바로 예외를 던짐(후보가
2개 이상 남아있을 때만 재현되므로 "가끔" 발생하는 패턴과 일치). **수정**: `foreach (var c in
baitCandidates.ToList())`로 스냅샷을 순회하도록 변경, Remove는 원본에만 적용(재시도 배수 루프에서
제외 효과는 그대로 유지). 파일명 정정: 이 버그는 `Fundinghedgemanager.cs`에 있었음 - C전략 메인
로직 파일은 `Fundinghedger .cs`(공백 포함, 밑줄 아님 - `Fundinghedger_.cs`도 `Fundinghedger.cs`도
아닌 실제 파일명이 `Fundinghedger .cs`이니 혼동 주의).

**2. 트레일링청산 giveback이 설정치(35%)를 45~55%까지 초과하는 반복 패턴 - 근본원인 규명+완화**:
BOTUSDT(peak0.3725%→final0.1693%, giveback54.5%)/ACEUSDT(peak0.2883%→final0.1586%, giveback45.0%)
실제 로그를 초 단위까지 대조한 결과, "트레일링청산" 감지 로그와 "C청산 완료" 로그가 **동일
밀리초**로 찍혀 있어 주문 체결 지연이 원인이 아님을 확인. 실제 원인은 `WaitForReversalOrTimeoutAsync`
의 50ms 폴링 + `TRAIL_GIVEBACK_CONFIRM_TICKS`(2틱 연속확인, 노이즈 필터링용 디바운스) 조합 -
정산 직후 급격한 반락 구간에서는 1~2 폴링 틱 사이에 giveback 비율이 임계치를 훌쩍 넘어버리는데,
디바운스가 그 순간에도 그대로 적용되어 확인용 1틱(~50ms)만큼 반납이 더 진행된 뒤에야 청산되기
때문(폴링 구조 자체의 한계라 완전 제거는 불가능, 완화만 가능). **조치 2건**:
(1) 코드: `TRAIL_GIVEBACK_HARD_MULT`(1.3배) 상수 신규 도입 - giveback비율이 임계치의 1.3배를
넘는 명백한 급락은 노이즈일 수 없으므로 디바운스(2틱 확인)를 생략하고 즉시 청산(`Fundinghedger .cs`
`WaitForReversalOrTimeoutAsync`실거래 경로 + `RunSkipSimulationCoreAsync`시뮬레이션 경로 양쪽에
동일 적용, A/B/C 비교 일관성 유지). 컴파일 확인 완료(현재 CoinSvr.exe가 실행 중이라 exe 파일
잠금으로 최종 빌드산출물 복사만 실패 - 문법 오류 아님, **반영하려면 서비스 재시작 필요, 아직
미실행 - 사람 확인 대기 중**). (2) 파라미터: `TrailGivebackPct` 35→28로 `MaxParamChangeRatio`
(20%) 한도 내 하향 조정, `strategy_config_history.jsonl`에 근거 기록 완료 - **재시작 불필요,
다음 스캔 사이클부터 즉시 반영**(코드가 매 사이클 시작 시 config를 재로드하므로).

**3. 07-20 09:30 KST 재시작/DailyLossLimitUsdt 20→30 미스터리 해소**: 사람이 "본인이 변경한 적
없다"고 확인 - 조사 결과, (a) 09:30 재시작은 **오늘 아침 스파이크(SpikeScalp) 쪽 대화 세션에서
`AlignedBreakoutMaxSpikePct` 기능을 추가하며 빌드+재시작한 것**(`CLAUDE_SPIKE.md`에 기록 있음,
`SpikeScalpConfig.cs`/`SpikeScalpManager.cs` 수정시각 09:29) - FundingHedger와 SpikeScalp가 같은
프로세스라 재시작이 공유된 것뿐, FundingHedger 자체와는 무관. (b) `DailyLossLimitUsdt` 20→30은
**`strategy_config.json` 파일 자체의 최종수정시각이 2026-07-11 04:14**로 확인됨(오늘과 전혀 무관,
프로젝트 시작 다음날 이미 그 값이었고 9일간 그대로였음) - `StrategyConfig.cs`의 코드 기본값도
이미 30으로 되어 있어(07-15 수정 시점 기준) 서로 일치, 다만 `Save()`를 거치지 않은 변경이라
`strategy_config_history.jsonl`엔 반영 안 됨(초기 이력만 20으로 남아있는 게 그 흔적). 오래된
사실이라 되돌릴 필요 없음, 이력 로그와 실제 값의 최초 괴리만 참고로 남김.

⚠️ **다음 세션이 확인할 것**: (1) 위 giveback 하드컷 코드가 실제 반영되려면 CoinSvr.exe 재시작이
필요 - 재시작 여부와 이후 giveback 분포(28% 목표 대비 실제 얼마나 초과하는지)를 다음 사이클에서
반드시 재확인. (2) `TrailGivebackPct`=28 적용 이후에도 여전히 초과 폭이 크면 추가 하향 또는
`TRAIL_GIVEBACK_HARD_MULT` 조정을 검토. (3) 이번 세션 신규데이터(BOTUSDT/ACEUSDT/ZHIPUUSDT) 관찰:
"A_Est가 C상회" 누적 11:14→11:16(BOTUSDT/ACEUSDT 둘 다 C상회)로 C쪽 리드 확대. 강한추세 구간에서
36회차(ESPORTSUSDT)에 이어 이번(BOTUSDT/ZHIPUUSDT)도 B_Est가 C를 크게 앞서 "추세강함→C유리"
가설이 최근 2세션 연속 흔들림(누적 n=3뿐, 계속 관찰 필요). 양수펀딩 표본은 BOTUSDT/ZHIPUUSDT
추가로 정체 해소(2종→최대 4종).

**추가 개선 + 배포 완료(같은 세션 후속)**: 사람이 "폴링 말고 이벤트 수신에서 직접 처리 가능한지"
질문 - 실측해보니 50ms 폴링보다 더 큰 병목은 `Fundinghedgemanager.cs`의 BookTicker 콜백 자체에
있던 **100ms 과부하방지 스로틀(`BOOK_TICKER_MIN_INTERVAL_MS`)**이었음(스로틀 때문에 `_bookTicker`
캐시 자체가 최소 100ms 간격으로만 갱신됨 - 50ms 폴링을 아무리 당겨도 이 스로틀이 실질적 하한이었음).
**코드 구조 변경**: `RegisterExitMonitor`/`UnregisterExitMonitor`/`WaitForNextBookTickerTickAsync`
(TaskCompletionSource 기반, `_fundingWaiters`와 동일 패턴) 신규 도입 - 트레일링청산 감시 중인
심볼만 콜백에서 100ms 스로틀을 우회하고 이벤트 도착 즉시 신호를 받도록 함(다른 후보 심볼은 기존
스로틀 그대로라 부하 증가 없음). `WaitForReversalOrTimeoutAsync`가 `Task.Delay(50, ct)` 폴링 대신
이 신호를 기다리도록 전면 교체 - 실제 BookTicker 이벤트가 도착하는 즉시 반응하므로 반응 지연이
폴링주기(50ms)+스로틀(100ms) 조합이 아니라 거래소 이벤트 도착 지연만 남게 됨(하드컷 로직과
병행 적용, 서로 보완).
**빌드+재시작 완료(2026-07-20T13:00:16Z, KST 22:00, PID 38392→49000)** - 재시작 직전 12:59:00 UTC
스캔이 후보 0개로 즉시 종료된 직후(활성 포지션 없음)라 안전한 타이밍에 진행. 재시작 로그 정상
확인(`🚀 [FUNDING-MGR] 시작`, `✅ [SPIKE] 광역스캔 구독 시작` 둘 다 확인, 다음 스캔 13:59:00 UTC
정상 예약됨). **다음 사이클이 반드시 확인할 것**: 재시작 이후 신규 트레이드에서 giveback 비율이
실제로 28% 근처로 수렴하는지(이벤트기반+하드컷+파라미터하향 세 조치 조합 효과 검증), 이벤트기반
전환 이후 예상치 못한 부작용(예: BookTicker 이벤트가 뜸한 저유동성 심볼에서 타임아웃까지 못
깨어나는 경우는 없는지 - `remain<=0`이면 루프가 자체적으로 break하므로 이론상 문제없으나 실거래로
재확인 필요)이 없는지.
⚠️ **참고(스코프 밖)**: 이번 재시작 직전 로그에서 `RunSkipSimulationCoreAsync(PreTrendSkip):
The process cannot access the file 'skipped_results.jsonl' because it is being used by another
process` 예외 1건 발견(ACEUSDT, 21:00:14 KST) - 여러 스킵시뮬레이션 태스크가 동시에 같은 파일에
`File.AppendAllText`를 시도할 때 발생하는 것으로 보이는 별개의 파일락 경합 버그. 이번 세션 범위
밖이라 수정하지 않음 - 다음 세션에서 빈도가 잦으면 조사 필요.

## 2026-07-21 사람과의 대화 세션 - PC 재부팅으로 인한 데이터 공백 발견 + 자동시작 작업 등록
사람이 "간밤에 피씨가 재부팅되서 데이터가 안쌓였네"로 시작한 세션에서 원인 조사 요청.

**조사 결과**: PC가 2026-07-20 23:47:09(KST)에 재부팅되었으나(`Win32_OperatingSystem.LastBootUpTime`
확인), `CoinSvr.exe`를 부팅/로그온 시 자동 실행하는 스케줄 작업이 존재하지 않아 사람이 수동으로
재실행한 2026-07-21 10:33:28(KST, PID 12504)까지 **약 10시간 46분간 서비스 자체가 정지**되어
있었음(`trade_results.jsonl`/`skipped_results.jsonl` 마지막 기록이 재부팅 전 07-20 22:52에서
멈춰있다가 재실행 후에야 재개된 것으로 확인). 기존 스케줄 작업 3개(`CoinSvr_ClaudeCode_Tuning`,
`CoinSvr_ClaudeCode_CodeProposal`, `CoinSvr_SpikeScalp_HourlyAnalysis`)는 전부 Claude Code 자동
튜닝/분석 사이클용이고 거래 서비스 본체 기동과는 무관했던 것이 근본원인.

**조치**: 신규 스케줄 작업 `CoinSvr_AutoStart` 등록 완료 - 트리거 "로그온 시"(사용자 cheol),
액션은 `CoinSvr.exe`를 `CoinSvr/bin/Debug/net9.0/` 작업폴더에서 인자 없이 실행(기존에 explorer.exe
더블클릭으로 콘솔창 띄워 기동하던 방식과 동일하게 재현, 인자·작업폴더 일치). `MultipleInstances=
IgnoreNew`로 중복실행 방지 설정.

⚠️ **다음 세션이 확인할 것**: 다음 재부팅/로그온 시 `CoinSvr_AutoStart` 작업이 실제로 정상
동작해 서비스가 자동 기동되는지 확인(등록 직후엔 이미 수동 기동 중이었어서 트리거 자체는 아직
미검증). 정상 동작 확인되면 이후 압축 시 이 항목은 요약만 남기고 넘어가도 무방.
