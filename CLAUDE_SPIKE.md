# CoinSvr SpikeScalp 자동분석 연속성 기록

이 파일은 `CLAUDE_CODE_TASK_SPIKE.md`(1시간 주기 자동화)가 매 사이클 append하는 로그다.
FundingHedger의 `CLAUDE.md`와는 완전히 별개 파일 - 서로 섞지 않는다.

## 프로젝트 배경
FundingHedger(펀딩비 정산 타이밍 기반)와 별개로, 펀딩비와 무관하게 가격+거래대금 동시 스파이크가
뜬 심볼을 모멘텀 방향으로 짧게(1~5분) 추격 진입 후 트레일링스탑/고정SL/타임아웃 중 먼저 오는
조건으로 정리하는 단순 테스트 모듈. 2026-07-16 최초 구현, 현재 `SpikeScalpManager.DebugDryRun=true`
(드라이런 - 실주문 없음, BookTicker 실데이터로 PnL만 시뮬레이션).

## 초기 설정값 (2026-07-16 기준)
- SpikeThresholdPct=0.8, VolumeSpikeMultiplier=3, StopLossPct=0.3(고정,자동조정 대상 아님),
  TrailArmPct=0.4, TrailGivebackPct=30, MaxHoldMs=180000(3분), CooldownMinutes=10,
  MaxConcurrentPositions=3, DailyLossLimitUsdt=20(안전장치, 자동조정 대상 아님)
- 전부 잠정값 - 표본 쌓이면 `CLAUDE_CODE_TASK_SPIKE.md` 절차에 따라 조정 예정.

## 관련 파일 (2026-07-19 갱신)
- 날짜별 아카이브: `CoinSvr/bin/Debug/net9.0/DataArchive/<YYYYMMDD>/<파일명>` — `rotate_data_logs.ps1`
  (FundingHedger와 공유, 6개 jsonl 파일 전부 처리)이 "오늘 이전" 날짜 레코드를 여기로 옮긴다.
- 증분분석 상태파일: `CoinSvr/bin/Debug/net9.0/spike_analysis_state.json` — `analyze_spike_cycle.py`가
  "마지막 분석 이후" 컷오프(`LastCutoffUtc`)를 자동 관리. 사람/이 작업 모두 직접 편집하지 않는다.
- 분석 스크립트: `analyze_spike_cycle.py`(프로젝트 루트) — 기존 analyze_spike_cycle.py+2.py 2단계
  수동 CUTOFF 편집 방식을 통합, 상태파일 기반 자동 컷오프로 교체(2026-07-19).

⚠️ **2026-07-19 데이터 유실 사고**: 사람과의 대화 세션에서 위 로테이션 인프라(`rotate_data_logs.ps1`)를
만들던 중 `-WhatIf`(미리보기) 모드 구현 버그로 `spike_scalp_results.jsonl`(2026-07-16~07-18,
1,976줄)/`spike_scalp_skipped.jsonl`(2026-07-16~07-18, **110,920줄**)/`spike_scalp_postexit.jsonl`
(2026-07-18, 307줄)/`spike_scalp_trendveto_sim.jsonl`(2026-07-18, 462줄)의 **2026-07-19 이전 전체
이력이 삭제됨**(git 저장소 아님, 휴지통/VSS 백업 전무로 복구 실패 확인, `.ui` 로그로도
`spike_scalp_skipped`류는 재구성 불가능 확인 - 사람이 복구 미진행 결정). 버그 자체는 수정 완료
(WhatIf 모드가 이제 라이브 파일을 실제로 건드리지 않음, 격리 테스트로 검증됨). **아래 1~60회차
요약 서술은 그대로 유효한 기록이지만, 그 근거였던 원본 raw 레코드는 더 이상 존재하지 않으므로
재검증이 불가능하다**는 점을 인지할 것 — 2026-07-19 이후 신규 데이터부터는 로테이션+아카이브로
안전하게 보존된다.

## ⚠️ 2026-07-20 사람과의 대화 세션 - AlignedBreakoutMaxSpikePct 도입 (코드 수정 + 재시작 완료)
`spike_scalp_trendveto_sim.jsonl` 표본이 1,413건(여러 심볼 분산)까지 쌓이면서 "초반 돌파(|스파이크|
3~10%대)는 순방향 탑승 시 Fwd300이 뚜렷이 플러스(3~5%대 +0.60%, 5~10%대 +0.29%)인데, 그 이상
극단화된 스파이크(10%+)는 +0.05%로 사실상 무의미"하다는 패턴이 확정적으로 재현됨(2026-07-19
ESPORTSUSDT 단일사례에서 처음 관측된 것과 방향 일치). 이를 근거로 `SpikeScalpConfig`에
`AlignedBreakoutMaxSpikePct`(기본 10)를 신규 도입 - `TrendAligned1h`에 걸릴 스파이크 중
`|SpikeChangePct|`가 `LargeSpikeSimThresholdPct`(3%)~`AlignedBreakoutMaxSpikePct`(10%) 구간이면
스킵 대신 **순방향(추세추종) 진입을 허용**하도록 `SpikeScalpManager.cs` 수정(그 구간 밖은 기존대로
스킵 유지, `spike_scalp_trendveto_sim.jsonl` 가상추적도 그 구간에서만 계속). 이렇게 진입한
트레이드는 `spike_scalp_results.jsonl`에 `AlignedBreakoutOverride=true`로 표시됨 - **다음
사이클부터 이 필드로 override 트레이드만 따로 뽑아서 실제로 도움이 되는지 검증할 것**(표본이
아직 0건에서 시작하므로 처음 몇 사이클은 표본부족일 수 있음, 정상).
**2026-07-20T00:30:24Z(KST 09:30:24) 재빌드+재시작 완료(PID 5940→38392 교체, 정상기동 확인)** -
이 시각 이후 신규 레코드부터 이 로직이 실제로 반영된 것. 이 작업(자동화 사이클)은 여전히 `.cs`
파일을 수정하지 않으며, `AlignedBreakoutMaxSpikePct` 값 자체는 `DailyLossLimitUsdt`/
`MaxParamChangeRatio`/`MaxDailyLossPerSymbolUsdt`와 달리 **일반 튜닝 파라미터로 취급 가능**하다
(표본이 쌓이면 기존 클램프 절차로 조정 검토 가능, 안전장치 3종과 혼동하지 말 것).

## 자동분석 사이클 로그
(여기부터 매 사이클 append)

## 과거 요약 (1~102회차 + 관련 대화세션, 2026-07-16T12:37:00Z ~ 2026-07-21T22:53:53+09:00)
CLAUDE_SPIKE.md가 20KB 압축 임계값을 여러 차례 넘어 이 구간(1~102회차 전체) 전부를 재압축함. 회차별
원문 상세는 각 backup/CLAUDE_SPIKE_ARCHIVE_*.md에 보존되어 있음(1~69회차:
`_20260716.md`~`_20260719L.md` 연속분, 70~94회차: `_20260719M.md`~`_20260721B.md` 연속분, 95~96:
`_20260721C.md`, 97: `_20260721D.md`, 98: `_20260721E.md`, 99+세션: `_20260721F.md`, 100~102:
`_20260721G.md` - 정확한 회차→파일 매핑은 git 이력상 이전 버전의 이 섹션 참고).
**파라미터 변경 이력(요약)**: 자동화가 직접 바꾼 것은 1회차 0.8→0.9, 3회차 TrailGivebackPct
30→25(실효없음 확인됨), 38회차 CooldownMinutes 10→9(자동화 최초·유일한 실질 조정)뿐 -
**38회차 이후(39~102회차) config는 전부 사람 직접개입 8건**: 39회차 SpikeThresholdPct 0.9→1.0,
2026-07-18 DailyLossLimitUsdt 100→1000000+CooldownMinutes 9→4+MaxConcurrentPositions 3→20(데이터
축적 목적), 2026-07-19 BookTicker 신선도체크+`EntryPriceIsFallback`, 2026-07-20
`AlignedBreakoutMaxSpikePct`/`MaxSpreadPct`+`WideSpread`/`MaxEntryReversalPct`+`SlConfirmTicks`
순차 도입, 2026-07-21 `spike_scalp_widespread_sim.jsonl`+`spike_scalp_stale_check.jsonl` 관측
로그 도입(로직 미관여) - **자동화 자신이 마지막으로 config를 조정한 것은 여전히 38회차뿐이라,
82회차부터 102회차까지 롤백 형식조건이 산발적으로 성립/불성립을 반복해도 "조정 실체가 없어 되돌릴
것도 없다"는 84회차 판단 원칙을 계속 유지 중(문구 명확화는 다음 대화 세션 과제로 이월).**
**핵심 이월 이슈(장기, 102회차 기준 현재상태)**:
(A) 진입가 스테일(PeakPct=0) - 구조적 문제(첫틱갭)로 확정, `MaxEntryReversalPct` 도입(92회차 이후
세션) 이후로도 매 사이클 0~80.0% 범위로 등락 지속(개선효과 아직 확정 못함), 100~102회차
18.8%→57.1%(n=7소표본)→31.2%로 큰 진폭.
(B) TRAIL giveback - 16회차 "폴링 고정지연" 구조적 원인 확정, 매 사이클 60~102%대에서 목표(25%)를
항상 초과(3회차 30→25 config 조정도 실효 없었던 전례), 100~102회차 3연속 62.5~70.0%대 밴드로
좁고 안정적으로 목표초과 지속 - FundingHedger 세션의 하드컷(`TRAIL_GIVEBACK_HARD_MULT`) 선례
적용검토가 여러 회차째 사람 세션 대기중(장기 미해결, .cs변경이라 이 작업 범위 밖).
(C)(D)(M) 종결된 이슈(DailyLossLimit 해제/config_history 조작의심/비라틴 심볼명) - 재론 불요.
(E) TrendAligned1h aligned/counter 우열 - 41~96회차 사이 수 차례 완전교대("구조적 우열없음" 가설
유지)했으나, 97·99·100·101·102회차 5/6회차 연속으로 aligned가 counter를 우세(102회차 aligned
n=6,+0.0126 > counter n=10,-0.0485) - 표본이 계속 늘고 있어 장기결론 재검토 여부를 다음 몇
사이클 계속 추적할 것(아직 뒤집을 근거로 단정하지 않음).
(F) Buy/Sell 우세 - 고정편향 없음 확정, 매 회차 계속 교대(102회차 Sell우위로 재반전).
(G)(R) 롤백 형식조건 - 82~102회차 사이 여러 차례 성립/불성립 반복(102회차 성립)하나 위 파라미터
변경이력 문단 참고, 자동화 직접조정 없는 구간이라 실행 대상 아님이 매 회차 재확인됨.
(H) 극단치(≤-1.5USDT) - 64~102회차 장기간 거의 0건 유지.
(I) postexit SL/TRAIL Fwd300 부호 - 회차마다 뒤바뀌는 불안정 패턴 지속(101·102회차 연속 SL은
음전환("SL판단 옳았다"), TRAIL은 100회차 강한 양전환에서 101·102 연속 음전환으로 반전 - "조기청산"
신호가 최근엔 약화되는 쪽이나 매 회차 n소표본이라 결론 보류). RSI/%B×TrendAligned1h 교차분석은
스크립트 미지원으로 여전히 미수행.
(J)(K)(N) 단일심볼 손실집중(룰렛형 리스크 가설 지지) - `MaxDailyLossPerSymbolUsdt`(2026-07-19)
도입으로 대응, 이후 특정 회차 편중은 있으나 매번 다른 심볼로 롤링.
(L) trendveto_sim - "심볼구성에 극도로 민감한 불안정 지표"로 재해석 확정, 95~101회차 7연속 0건
후 102회차 6건 재등장(ONEUSDT 5건이 전체 평균을 견인, 유일 타심볼 DEXEUSDT는 정반대 방향) -
재해석 결론과 정확히 일치하는 패턴, 여전히 문턱 조정 근거로는 못 씀.
(O)(Q) `AlignedBreakoutMaxSpikePct`/`AlignedBreakoutOverride` - 2026-07-20 코드도입 이후 82회차
첫 5건부터 85~99회차 대체로 악화기조(99회차 누적 n=82,26승56패,-1.0328)였다가 100회차 신규 5건
(+2.6172)으로 처음 누적 흑자 전환(n=87,29승58패,+1.5844) 후 101·102회차 신규 0건으로 정체 -
표본 더 쌓일 때까지 추세전환 단정 보류. 세부구간([3~3.6%)/[3.6~5%)/[5~10%)) 비단조 패턴 문제로
이 작업이 조정 가능한 min/max 단일 문턱으로는 선택적 배제가 불가능하다는 결론은 유지("중간구간
배제 전용 필드" 신설은 `.cs` 변경이라 범위 밖). 102회차부터 별도 베이지안 누적(spike_bayes_state.json)
집계도 병행 관측 시작(a=1,b=4 - 위 n=87 수동집계와 불일치, 원인 미확인, 계속 추적).
(P) 스킵사유 순위 - TrendAligned1h가 장기 1위였다가 91회차 이후 도입된 `MaxSpreadPct`/`WideSpread`
필터로 92~94회차 사이 두 필터가 매회 뒤집히는 불안정 구간을 거쳐, 95~102회차엔 TrendAligned1h가
8연속 1위(64.0~88.4%대로 진폭 큼)로 안정화, WideSpread는 대체로 2위(7.2~22.6%대) 고정.
95~99회차 회차별 등락 상세는 각 backup 파일(위 매핑 참고) 확인 - 건별평균 추이만 이어서 적으면
95(-0.1379)→96(-0.0769)→97(+0.0717)→98(-0.0019)→99(+0.0032)→100(+0.0460)→101(+0.0302)→
102(-0.0256, 3연속 플러스 후 재차 마이너스). 100~102회차 상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260721G.md`
참고, 103회차부터는 아래 "자동분석 사이클 로그"에 append.
2026-07-21 사람과의 대화 세션 2건(각 `_20260721F.md`에 원문): (1) WideSpread 임계값 검증 -
`.ui`로그 3,001건 분석으로 "후보량"은 확인했으나 수익성 근거 부재를 확인, `spike_scalp_widespread_sim.jsonl`
(SpreadPct+Fwd30~300s) 신규 도입. (2) 스테일컷(진입 5~10초 pnl정체시 조기SL) 아이디어 검토 -
현재 로그로는 검증 불가 확인, `spike_scalp_stale_check.jsonl`(5s/10s 관측, 로직 미관여) 신규 도입.
둘 다 관측전용, 102회차 기준 각각 141/136줄 누적(본격 분석은 표본 더 쌓인 뒤 진행 예정, 이번
사이클도 직접분석 안 함).

## 자동분석 사이클 로그
(다음 사이클부터 여기에 append — 1~102회차 전체가 20KB 임계값 초과로 위 "과거 요약"으로 재압축됨)
