# CoinSvr FundingHedger 자동 튜닝 — 세션 간 연속성 로그

이 파일은 `CLAUDE_CODE_TASK.md`에 정의된 12시간 자동 튜닝 사이클이 세션 간 맥락을 이어가기 위해
누적 기록하는 파일이다. 매 사이클 종료 시 하단에 새 항목을 append하며, 기존 내용은 지우지 않는다.

관련 파일:
- 수치 변경 이력: `CoinSvr/bin/Debug/net9.0/strategy_config_history.jsonl`
- 사이클별 요약: `automation_summary.log` (프로젝트 루트, `CoinSvr/bin/Debug/net9.0/`가 아님 —
  Claude Code 자신이 만드는 메타파일이라 .bat이 cd한 위치에 생성됨. trade_results.jsonl/
  strategy_config.json 등 C# 앱이 쓰는 파일과는 경로가 다르니 혼동 주의)
- 실거래 결과: `CoinSvr/bin/Debug/net9.0/trade_results.jsonl`
- 스킵 시뮬레이션: `CoinSvr/bin/Debug/net9.0/skipped_results.jsonl`
- 날짜별 아카이브(2026-07-19 신설): `CoinSvr/bin/Debug/net9.0/DataArchive/<YYYYMMDD>/<파일명>` —
  `rotate_data_logs.ps1`이 "오늘 이전" 날짜 레코드를 여기로 옮긴다(라이브 파일 무한증가 방지).
- 증분분석 상태파일(2026-07-19 신설): `CoinSvr/bin/Debug/net9.0/analysis_state.json` —
  `analyze_cycle.ps1`이 "마지막 분석 이후" 컷오프(`LastCutoffUtc`)를 자동 관리. 사람/이 작업 모두
  직접 편집하지 않는다.

⚠️ **2026-07-19 데이터 유실 사고**: 사람과의 대화 세션에서 `rotate_data_logs.ps1`(위 로테이션
인프라)을 만들던 중 `-WhatIf`(미리보기) 모드 구현 버그로 `trade_results.jsonl`(2026-07-10~07-18,
112줄)과 `skipped_results.jsonl`(2026-07-14~07-18, 41줄) 및 spike_scalp 계열 파일들의 **2026-07-19
이전 전체 이력이 삭제됨**(git 저장소 아님, 휴지통/VSS 백업 전무로 복구 실패 확인, `.ui` 로그로
부분 재구성 가능했으나 사람이 진행하지 않기로 결정). 버그 자체는 수정 완료(WhatIf 모드가 이제
라이브 파일을 실제로 건드리지 않음, 격리 테스트로 검증됨). **이 파일의 5~33회차 요약 서술은 그대로
유효한 기록이지만, 그 근거였던 원본 raw 레코드는 더 이상 존재하지 않으므로 재검증이 불가능하다**는
점을 인지할 것 — 2026-07-19 이후 신규 데이터부터는 로테이션+아카이브로 안전하게 보존된다.

## 과거 요약 (5~37회차 + 사람과의 대화 세션, 2026-07-11T06:41:14Z ~ 2026-07-20T10:41:00Z)
CLAUDE.md가 20KB 압축 임계값을 다시 넘어 이 구간을 압축함. 5~34회차 상세는 기존 아카이브
(`CLAUDE_MD_ARCHIVE_20260713.md`~`CLAUDE_MD_ARCHIVE_20260720.md`) 참고. 이번 압축분(5~34회차
요약본 원문 + 35·36·37회차 상세 전문)은 `CLAUDE_MD_ARCHIVE_20260720B.md`에 보존.
5~24회차: 파라미터 변경 없음 지속. 트레일링 giveback 35%초과 반복(2026-07-20 사람과의 대화
세션에서 근본원인 규명+수정, 아래 참고). **2026-07-15 코드패치 3건**: B_ProfitPct_Est 펀딩비
누락버그 수정, tick/price 비율 후보필터(`MaxTickPriceRatioPct` 0.1%) 도입, 펀딩비크기 vs
반락폭 상관계수0.43(n=76) 확인. **2026-07-16**: SnapA/B는 실거래 판단에 안 쓰이고 사후분석용
뿐 - 실거래 추세필터는 `Trend30Pct` 사용, SnapSameTick=true 레코드는 A_Est 비교 제외.
25~37회차: "A_Est가 C상회" 누적이 25회차 이래 계속 뒤집히다 35회차에 11:11 정동률 도달 →
36회차에 11:14로 C쪽이 처음 유의미하게 앞섬(37회차는 신규데이터 0건으로 갱신 없음, 최신
누적은 11:14 유지). "추세약함→B유리/추세강함→C유리"는 25~35회차 11연속 지지 후 36회차에
양끝 구간(n=1씩)에서 처음 뒤집힘 - 표본부족 상태의 첫 반례로 기록, 가설 기각 아님. 펀딩비크기
3구간은 33~35회차 "중간구간 최고치" 비단조 패턴 재현 후 36회차엔 다른 모양(고구간 최저,
ESPORTSUSDT 펀딩비 2.0%는 이상치 가능성) - 단순 가설 재검토 필요할 수 있음. 양수펀딩 표본
누적 3건(EBAYUSDT+FWDIUSDT, 심볼 2종)에서 장기 정체. `analyze_cycle.ps1` 구간별 승률표시 버그
미해결. Collection was modified 스캔예외 누적 5건(30~37회차, 2026-07-20 사람과의 대화 세션에서
근본원인 규명+수정, 아래 참고). 37회차: 09:30 KST 서비스 재시작(원인 불명으로 플래그됨,
2026-07-20 대화 세션에서 스파이크 쪽 `AlignedBreakoutMaxSpikePct` 기능 추가 세션이 원인이었음을
확인 완료 - FundingHedger와 SpikeScalp가 같은 프로세스라 재시작이 공유됨). `DataArchive` 폴더
07-20 UTC 전환 시점에 정상적으로 최초 생성 확인됨(더 이상 반복 플래그 불필요).

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

## 자동튜닝 사이클 로그 [2026-07-21T15:39:28+09:00] (38회차)
- 표본: 9건 (trade 5 + skip 4, 기간: 2026-07-20T10:23:44Z ~ 2026-07-21T06:36:32Z, 자동컷오프
  약20시간 - 이 구간에 사람과의 대화세션 2건, 07-20T13:00Z 코드배포, 07-20 23:47 KST PC재부팅이
  모두 걸쳐 있음)
- A/B/C 비교 (trade 5건, paper): C=평균-0.0751%/승률40%(2/5), A_Est=평균0.0253%/승률60%(3/5),
  B_Est=평균-0.0571%/승률80%(4/5). non-bait 2건만: C=평균-0.1061%/승률50%. 실측(Actual) 5건
  전부 null이라 실측 비교는 여전히 불가.
- 발견한 패턴: (1) "A_Est가 C상회" 개별비교 이번 A4승C1승 → 누적 11:16(C리드5칸)에서 15:17(C리드
  2칸)로 급격히 좁혀짐 - "구조적 우열 없음" 가설 방향에 부합, 단일사이클 스윙폭이 큼(지속 확인 필요).
  (2) Trend30Pct 강한추세 구간(n=3)에서 B_Est(0.1685%)가 C(-0.0836%)를 크게 앞서 "추세강함→C유리"
  가설이 36회차(n=1)에 이어 n=3 표본으로 재차 반박됨 - 25~35회차 11연속 지지에서 최근 2연속 반례로
  전환, 가설 신뢰도 재검토 필요. (3) 펀딩비크기 3구간 "중간구간최고치" 비단조 패턴이 36회차 이탈 후
  다시 재현(저-0.1392%/중+0.0724%/고-0.1816%, 각n=3). (4) 트레일링청산 유효표본(DODOXUSDT45.5%,
  DEXEUSDT33.3%) - 07-20T13:00Z TrailGivebackPct=28+하드컷+이벤트기반 배포 이후 giveback이 목표28%
  대비 여전히 초과하나, 배포 전 BOTUSDT54.5%/ACEUSDT45.0%보다는 개선 방향(n=2뿐이라 확정 아님).
  (5) ERAUSDT giveback1033.3%는 peak0.0307%가 TrailMinPeakPct(0.05%) 미달로 트레일링 자체가
  작동 안 한 상태에서 발생한 별종 극단치 - 기존 THEUSDT/1000XECUSDT급 저유동성 급변과 동일 계열
  추정, giveback 로직 문제 아님. (6) Collection was modified 스캔예외: 07-20T13:00Z 배포(foreach
  스냅샷 수정) 이후 07-20 잔여시간+07-21 전체 .ui 로그에서 신규 0건 - 버그수정 효과 확인됨(누적
  5건에서 정지). (7) 구간별 승률표시는 기존 known bug로 이번에도 전구간 0%로 나와 신뢰 불가,
  평균값만 사용.
- 파라미터 변경: 없음 - trade 5건으로 최소기준은 충족하나 세부 비교군(트레일링청산 n=2, non-bait
  n=2 등)이 전부 5건 미만이라 방향성 확신 부족(규칙5 적용, 특히 TrailGivebackPct는 배포 직후라
  효과 검증 표본 자체가 더 필요).
- 롤백: 해당없음 (config history에 자동튜닝 사이클발 변경이 아직 0건 - 07-20 사람세션발 1건뿐이라
  "직전3 vs 이전3" 비교 자체 불가).
- 다음 사이클이 알아야 할 것: (1) TrailGivebackPct=28 배포 이후 giveback 수렴 여부를 표본 5건
  이상 쌓일 때까지 계속 추적할 것(이번 n=2는 판단 근거로 부족). (2) "추세강함→C유리" 가설이 최근
  2연속(36회차 n=1, 38회차 n=3) 반박됐으므로, 다음 사이클에 강한추세 구간 표본이 더 쌓이면 이
  가설 자체를 "기각 검토" 단계로 격상할지 판단할 것. (3) "A_Est가 C상회" 누적이 15:17로 좁혀진
  추세가 다음 사이클에도 이어지는지, 아니면 이번이 일시적 스윙이었는지 확인. (4) 07-21 PC재부팅
  이후 등록된 `CoinSvr_AutoStart` 스케줄 작업은 아직 실제 재부팅으로 검증되지 않은 상태 그대로임 -
  이 사이클 범위 밖이라 별도 조치 안 함.
