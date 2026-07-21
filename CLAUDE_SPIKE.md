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

## 과거 요약 (1~94회차 + 관련 대화세션, 2026-07-16T12:37:00Z ~ 2026-07-21T11:53:12+09:00)
CLAUDE_SPIKE.md가 20KB 압축 임계값을 여러 차례 넘어 이 구간(1~94회차 전체)을 하나로 재압축함. 94회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260721B.md` 참고(이번 압축분), 92회차
상세 + 92회차 이후 사람과의 대화 세션(SL 보유시간/postexit 분석, MaxEntryReversalPct·SlConfirmTicks
신규 도입)은 `backup/CLAUDE_SPIKE_ARCHIVE_20260721.md` 참고, 91회차
상세 + 91회차 이후 사람과의 대화 세션 2건(TriggerPrice 로깅 추가, MaxSpreadPct 신규 필터 도입)은
`backup/CLAUDE_SPIKE_ARCHIVE_20260720P.md` 참고, 90회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260720O.md` 참고, 89회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260720M.md` 참고, 88회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260720L.md` 참고, 87회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260720K.md` 참고, 86회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260720J.md` 참고, 85회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260720I.md` 참고, 83~84회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260720H.md` 참고, 82회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260720G.md` 참고, 81회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260720F.md` 참고, 80회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260720E.md` 참고, 78~79회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260720D.md` 참고, 75~77회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260720C.md` 참고, 74회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260720B.md` 참고, 73회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260720.md` 참고, 72회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260719O.md` 참고, 71회차
상세는 `backup/CLAUDE_SPIKE_ARCHIVE_20260719N.md` 참고, 70회차
상세는 `CLAUDE_SPIKE_ARCHIVE_20260719M.md` 참고. 1~69회차 원문 전체는
`CLAUDE_SPIKE_ARCHIVE_20260716.md`(1~3), `CLAUDE_SPIKE_ARCHIVE_20260716B.md`(4~7), `CLAUDE_SPIKE_ARCHIVE_20260717.md`(8~12),
`CLAUDE_SPIKE_ARCHIVE_20260717B.md`(13~14), `CLAUDE_SPIKE_ARCHIVE_20260717C.md`(15~16), `CLAUDE_SPIKE_ARCHIVE_20260717E.md`(17~19),
`CLAUDE_SPIKE_ARCHIVE_20260717F.md`(20~21), `CLAUDE_SPIKE_ARCHIVE_20260717L.md`(22~28), `CLAUDE_SPIKE_ARCHIVE_20260717M.md`(29~31),
`CLAUDE_SPIKE_ARCHIVE_20260718.md`(32~33), `CLAUDE_SPIKE_ARCHIVE_20260718B.md`(34), `CLAUDE_SPIKE_ARCHIVE_20260718C.md`(35),
`CLAUDE_SPIKE_ARCHIVE_20260718G.md`(36~39), `CLAUDE_SPIKE_ARCHIVE_20260718H.md`(40), `CLAUDE_SPIKE_ARCHIVE_20260718I.md`(41),
`CLAUDE_SPIKE_ARCHIVE_20260718J.md`(41~42 상세), `CLAUDE_SPIKE_ARCHIVE_20260718K.md`(43), `CLAUDE_SPIKE_ARCHIVE_20260718L.md`(44),
`CLAUDE_SPIKE_ARCHIVE_20260718M.md`(45~46), `CLAUDE_SPIKE_ARCHIVE_20260718N.md`(47), `CLAUDE_SPIKE_ARCHIVE_20260718O.md`(48~49),
`CLAUDE_SPIKE_ARCHIVE_20260719.md`(50~51), `CLAUDE_SPIKE_ARCHIVE_20260719B.md`(52), `CLAUDE_SPIKE_ARCHIVE_20260719C.md`(53~54),
`CLAUDE_SPIKE_ARCHIVE_20260719D.md`(55), `CLAUDE_SPIKE_ARCHIVE_20260719E.md`(56~57), `CLAUDE_SPIKE_ARCHIVE_20260719F.md`(58~60),
`CLAUDE_SPIKE_ARCHIVE_20260719G.md`(61~62), `CLAUDE_SPIKE_ARCHIVE_20260719H.md`(63회차 상세 + 2026-07-19 대화세션 노트 2건 원문),
`CLAUDE_SPIKE_ARCHIVE_20260719I.md`(64~65회차 상세), `CLAUDE_SPIKE_ARCHIVE_20260719J.md`(66~67회차 상세),
`CLAUDE_SPIKE_ARCHIVE_20260719K.md`(68회차 상세), `CLAUDE_SPIKE_ARCHIVE_20260719L.md`(69회차 상세, 이번
압축분) 참고.
**파라미터 변경 이력(요약, 1~39회차 상세는 위 archive 목록 참고)**: 자동화가 직접 바꾼 것은
1회차 0.8→0.9, 3회차 TrailGivebackPct 30→25(실효없음 확인됨), 38회차 CooldownMinutes 10→9(자동화
최초·유일한 실질 조정)뿐 - **38회차 이후 config는 전부 사람 직접개입 8건**: 39회차
SpikeThresholdPct 0.9→1.0, 2026-07-18 DailyLossLimitUsdt 100→1000000+CooldownMinutes 9→4+
MaxConcurrentPositions 3→20(데이터축적 목적, 47회차 확인), 2026-07-19T04:14:47Z BookTicker
신선도체크+`EntryPriceIsFallback`(아래 (A)), 2026-07-20T00:30:24Z `AlignedBreakoutMaxSpikePct`
도입(아래 (O)), 2026-07-20T13:29:17Z `MaxSpreadPct`/`WideSpread` 도입(아래 (P)),
2026-07-20T14:05:23Z(KST23:06:11) `MaxEntryReversalPct`/`SlConfirmTicks` 도입(SL
즉발형/지연형 분리대응, 92회차 이후 상세) - **82~92회차 사이 롤백 형식조건이 산발적으로
성립/불성립을 반복했으나 "자동화 자신이 바꾼 파라미터가 없는 구간"이라는 84회차 판단 원칙을
그대로 유지 중, 다음 대화 세션에서 롤백조건 문구 명확화 필요.**
**핵심 이월 이슈(장기, 현재상태 요약 - 63~91회차 회차별 등락 상세는 backup/CLAUDE_SPIKE_ARCHIVE_20260720E~P.md 참고)**:
(A) 진입가 스테일(PeakPct=0) - 구조적 문제로 확정(첫틱갭), 2026-07-19 코드수정 이후로도 계속
등락(7.7~65.0% 범위), 91회차 40.7%→92회차 42.6%. "스테일이 SL의 주요 동인" 결론 유지, 92회차 이후
세션에서 SL을 즉발형(PeakPct=0)/지연형(PeakPct>0)으로 분리분석해 `MaxEntryReversalPct`(즉발형
대응·진입직전반전시 취소) 도입 - 다음 몇 사이클에서 스테일 비율 실제 완화 여부 검증 대기중.
(B) TRAIL giveback - 16회차 "폴링 고정지연" 구조적 원인 확정, 61.6~102%대에서 목표(25%)를 항상
초과하는 기조 계속(91회차 62~74%대, 92회차 62~102%대·AXTIUSDT는 반납이 피크를 넘어 음전환된
첫 사례), FundingHedger 세션에서 유사구조에 하드컷(`TRAIL_GIVEBACK_HARD_MULT`) 적용 선례 있어
SpikeScalpManager.cs 적용검토 요청이 여러 회차째 사람 세션 대기중(장기 미해결).
(C)(D)(M) 종결된 이슈(DailyLossLimit 해제/config_history 조작의심/비라틴 심볼명) - 재론 불요.
(E) TrendAligned1h aligned/counter 우열 - 41회차 이래 수 차례 완전교대 반복(70~91회차 사이 매
5~7회차 단위로 뒤집힘), "구조적 우열없음" 가설이 표본확대 전까지 유효, 91회차는 aligned 표본
극소(n=2)라 판단보류.
(F) Buy/Sell 우세 - 고정편향 없음 확정, 72회차 이래 3~5회차 단위로 계속 교대.
(G)(R) 롤백 형식조건(직전3평균<그전3평균) - 82~91회차 사이 여러 차례 성립/불성립을 반복했으나,
**자동화 자신이 마지막으로 config를 조정한 것은 38회차(CooldownMinutes 10→9)뿐이고 이후 entry는
전부 사람 직접개입**이라 "자동화 조정이 성과를 악화시켰다면 되돌린다"는 롤백조건의 취지 자체가
이 구간엔 적용 대상이 없음 - 형식요건이 몇 회 연속 충족되어도 기계적 실행은 계속 보류(84회차 최초
판단, 다음 대화 세션에서 롤백조건 문구를 "자동화가 직접 바꾼 파라미터가 있을 때만"으로 명확히
할지 확인 필요, 상세는 archive H~K 참고).
(H) 극단치(≤-1.5USDT) - 64~91회차 장기간 거의 0건 유지.
(I) postexit SL/TRAIL Fwd300 부호 - 회차마다 자주 뒤바뀌는 불안정 패턴 지속, "확정 안정화" 결론
아직 못 냄. RSI/%B×TrendAligned1h 교차분석은 스크립트 미지원으로 여전히 미수행.
(J)(K)(N) 단일심볼 손실집중(룰렛형 리스크 가설 지지) - `MaxDailyLossPerSymbolUsdt`(2026-07-19)
도입으로 대응, 이후 특정 회차 편중은 있으나 매번 다른 심볼로 롤링.
(L) trendveto_sim - "심볼구성에 극도로 민감한 불안정 지표"로 재해석 확정, 신규 발생 자체가
간헐적(0건인 회차 다수, 91·92회차 연속 0건).
(O)(Q) `AlignedBreakoutMaxSpikePct`/`AlignedBreakoutOverride` - 2026-07-20 코드도입 이후 누적표본이
82회차 첫 5건에서 91회차 n=72(23승49패,승률31.9%,순손익-1.0704)까지 확대, 85회차 이래 대체로
악화기조. 세부구간 재분해([3~3.6%)/[3.6~5%)/[5~10%)) 결과 손실이 특정 중간구간에 쏠리는 비단조
패턴이 반복 확인되어 **이 작업이 조정 가능한 단일 min/max 문턱(`AlignedBreakoutMaxSpikePct`/
`LargeSpikeSimThresholdPct`)으로는 선택적 배제가 불가능**하다는 결론이 굳어짐 - "중간구간 배제
전용 필드" 신설은 `.cs` 코드변경이라 범위 밖, 다음 대화 세션 논의 우선순위 유지.
(P) 스킵사유 순위 - TrendAligned1h가 83~91회차 장기간 1위를 유지했으나, 91회차 이후 세션에서
도입된 `MaxSpreadPct`/`WideSpread` 필터가 92회차부터 순위를 재편(92회차 3위19.9%→93회차
**1위64.2%**로 급부상, TrendAligned1h는 93회차 0%까지 하락) - WideSpread가 promote 이전 단계에서
후보를 대거 선점 차단하는 구조로 굳어지는 중, 트리거 기회 자체가 과도하게 줄어드는 건 아닌지
다음 몇 사이클 계속 관찰 필요(값 조정은 사람 세션 산출물이라 이 작업이 임의로 손대지 않음).
89~94회차 상세 서술(순손익 등락, override/롤백조건 세부수치)은 각각
`backup/CLAUDE_SPIKE_ARCHIVE_20260720M.md`(89)/`N.md`(89 이후 세션)/`O.md`(90)/`P.md`(91+세션)/
`CLAUDE_SPIKE_ARCHIVE_20260721.md`(92·93회차+세션)/`CLAUDE_SPIKE_ARCHIVE_20260721B.md`(94회차)
참고 - 건별평균 추이만 요약하면 89(+0.0261)→90(-0.0529)→91(-0.0923)→92(-0.0594)→93(-0.0393)→
94(-0.0132)로 90회차부터 마이너스가 이어지되 91회차 이후 낙폭이 4회차 연속 축소됨(95회차에
반전, 아래 참고). 93회차는 PC 재부팅 10시간46분 중단 이후 첫 사이클로 스킵사유 1위가
WideSpread(64.2%)로 급부상, TrendAligned1h는 0%까지 하락했다가 94회차 33.8%로 재상승, 95회차엔
TrendAligned1h가 74.7%로 다시 최상위 역전(아래 참고 - 두 필터 우위가 매회 뒤집히는 불안정
구조가 계속됨). 94회차 핵심: (A) 스테일진입가 SL조건부 비중 3회차 연속 감소(62.2%→52.2%→
41.7%)로 `MaxEntryReversalPct` 개선효과 조짐, (B) TRAIL giveback 9건 전부 61.5~87.3%대로 목표
(25%) 여전히 큰폭 초과(장기 구조적 이슈 불변), (Q) AlignedBreakoutOverride 3사이클 연속 0건
(누적 n=72,-1.0704 변동없음), 롤백 형식조건 불성립(자동화 직접조정은 여전히 38회차뿐).
95~96회차 상세(순손익 등락, 스테일비중 반전, override 누적갱신 등 세부수치)는
`backup/CLAUDE_SPIKE_ARCHIVE_20260721C.md` 참고 - 건별평균 추이만 이어서 요약하면
95(-0.1379)→96(-0.0769)로 95회차 급반전(6회차 연속개선 흐름 종료) 이후 소폭 개선되었으나
여전히 마이너스. 95회차 핵심: (A) 스테일진입가 SL조건부 비중 4연속 감소 지속(41.7%→33.3%),
(B) TRAIL 0건이라 giveback 분석 불가(최초 사례), (P) 스킵사유 1위가 TrendAligned1h로 재역전
(74.7%), (Q) override 재발생 2건 모두 손실(누적 n=74,-1.3743). 96회차 핵심: (A) 스테일진입가·
SL조건부 비중 둘 다 급등(55.6%/83.3%)하며 4회차 연속 개선흐름 반전 - 다음 사이클 재확인 필요,
(B) TRAIL 2건 모두 giveback 62~64%대로 목표(25%) 여전히 큰폭 초과, (P) TrendAligned1h가
95~96회차 연속 1위 유지(93~94회차의 매회 역전 패턴과 달리 처음 안정화 조짐), (Q) override
3건 추가로 누적 n=77(24승53패,-1.5022)까지 성과 추가 악화. 롤백 형식조건 96회차에 1회
성립(직전 95회차는 불성립)했으나 3연속 아니고 자동화 직접조정 자체가 여전히 38회차뿐이라
실행 대상 아님.
97회차 상세(세부수치)는 `backup/CLAUDE_SPIKE_ARCHIVE_20260721D.md` 참고 - 건별평균 추이만
이어서 요약하면 97(+0.0717)로 90회차 이래 처음 플러스 전환(96회차대비 큰 폭 개선). 97회차 핵심:
(A) 스테일진입가 2/7(28.6%), SL중스테일 2/2(100%) - "스테일=SL주요동인" 재확인, (B) TRAIL
giveback 4건 63.0~72.0%대로 여전히 목표(25%) 큰폭 초과, (P) TrendAligned1h가 95~97회차 3연속
1위로 안정화 조짐, (E) aligned(n=3,+0.1673)가 counter(n=4,+0.0001)보다 좋았던 드문 반례(극소
표본), (I) postexit TRAIL Fwd300 94·97회차 2연속 강한 양전환(+1.17/+1.26), (Q) override
신규1건 승리(BANKUSDT)로 누적 n=78(25승53패,-1.22698). 롤백 형식조건 96·97회차 2연속성립했으나
자동화 직접조정은 여전히 38회차뿐이라 실행보류.
98회차 상세(세부수치)는 `backup/CLAUDE_SPIKE_ARCHIVE_20260721E.md` 참고 - 건별평균 추이만
이어서 요약하면 98(-0.0019)로 97회차(+0.0717) 플러스전환 1회만에 재차 마이너스(거의 0 수준).
98회차 핵심: (A) 스테일진입가 0%로 급락(94~96회차 등락 이후 최초 전무), (B) TRAIL giveback 7건
63.3~71.4%대로 여전히 목표(25%) 큰폭 초과, (P) TrendAligned1h가 95~98회차 4연속 1위로 안정화
조짐, (E) override 발생 0건이라 aligned 표본 자체가 없어 우열비교 불가, (I) postexit SL Fwd300
양전환(+0.4871)/TRAIL Fwd300 음전환(-1.2714)으로 여전히 불안정, (Q) override 신규 0건(누적
n=78 불변), (L) trendveto_sim 4연속 0건. 롤백 형식조건 불성립(96·97회차 2연속성립이 98회차에
끊김).

## 자동분석 사이클 로그
(다음 사이클부터 여기에 append — 98회차 상세는 20KB 임계값 초과로
backup/CLAUDE_SPIKE_ARCHIVE_20260721E.md로 이동)

## 자동분석 사이클 로그 [2026-07-21T16:53:00+09:00]
99회차. 표본: 신규 18건(results)/1704건(skipped). cutoff 2026-07-21T06:52:19Z~현재.
ExitReason 분해: SL11(avgPnl-0.1614,승률0%,avgPeak0.0700%)/TRAIL7(avgPnl0.2620,승률100%,avgPeak0.7442%),
TIMEOUT 0건. 순손익+0.0583USDT(건별평균+0.0032) - 98회차(-0.0019)에 이어 사실상 0 근처에서
부호만 재차 반전(97회차 큰폭플러스 이후 2사이클째 미세 등락).
발견한 패턴:
(A) 스테일진입가(PeakPct=0) 6/18(33.3%), SL중스테일 6/11(54.5%) - 98회차 0.0%(4연속개선 뒤 최초
전무)에서 이번에 급반등, 등락폭이 매우 큼(소표본 변동성 감안).
(B) TRAIL giveback 7건 62.6~77.0%대(GPSUSDT77.0%/KERNELUSDT63.3%/AKEUSDT62.6%/POLYXUSDT64.7%/
UBUSDT64.8%/ZKCUSDT63.5%/HANAUSDT64.6%)로 목표(25%) 여전히 큰폭 초과 - 장기 구조적 이슈 불변.
(P) 스킵사유: TrendAligned1h67.2%(1위)/WideSpread14.9%/Cooldown13.4%/AlreadyActive3.9%/EntryFailed0.6%
- TrendAligned1h가 95~99회차 5연속 1위로 안정화 지속.
(F) Buy/Sell: Sell n=9 avg0.0010승률44.4%, Buy n=9 avg0.0055승률33.3% - avg기준Buy우위/승률기준
Sell우위로 지표 갈림(n소표본, 장기 무편향 결론 불변).
(E) TrendAligned1h aligned/counter: aligned n=6 avg+0.0867, counter n=12 avg-0.0385 - aligned가 우위.
97회차(n=3,+0.1673>counter+0.0001, "극소표본 드문 반례"로 기록)에 이어 이번(n=6)도 aligned우위로
2연속 관찰 - 표본이 조금씩 늘고 있으나 여전히 장기결론(counter 유리) 뒤집을 근거는 아님, 계속 추적.
(I) postexit: SL n=10 Fwd30avg-0.1718/Fwd60avg-0.2435/Fwd180avg-0.6626/Fwd300avg-0.8526 - 98회차
SL Fwd300 양전환(+0.4871)에서 이번엔 강한 음전환으로 재반전("SL판단이 옳았다"는 신호 강화, 방향
자체가 매회 크게 흔들려 추세 판단은 계속 보류). TRAIL n=6 Fwd30avg+0.0935/Fwd60avg+0.8996/
Fwd180avg+0.1080/Fwd300avg-0.2804 - Fwd300은 음전환 유지(98회차 -1.2714에서 폭은 축소)이나
Fwd60은 강한 양전환으로 구간별 부호가 혼조.
(Q) AlignedBreakoutOverride 신규 4건(1승3패, USUSDT-0.1702/KERNELUSDT+0.6874/LAUSDT-0.1561/
BANKUSDT-0.1669, 순손익+0.1942) - 누적 n=82(26승56패, 순손익-1.0328)로 98회차(-1.22698) 대비
소폭 개선.
(L) trendveto_sim 신규 0건(95~99회차 5연속 0건).
(RSI) RSI[0,30)n=6avg-0.0328승률33.3%/RSI[30,50)n=5avg-0.0837승률20.0%/RSI[50,60)n=5avg0.0307
승률60.0%/RSI[60,70)n=1/RSI[80,100]n=1 - 전구간 10건 미만이라 결론보류(기존 "낮은구간 유리"
방향과 달리 이번엔 [50,60)이 최고치인 소표본 등락, 확정 아님).
파라미터 변경: 없음 - 전체표본 18건은 최소기준(5건) 충족하나 ExitReason/RSI/override 등 세부구간이
대부분 10건 미만이라 절차 6번 기준 미달, 확신부족으로 유지.
롤백: 형식조건 불성립(last3avg{97,98,99}=+0.0243 vs prev3avg{94,95,96}=-0.0760, last>prev이므로
악화조건 자체가 성립 안 함). 자동화 직접조정은 여전히 38회차뿐이라 실행 대상 아님.

## 2026-07-21 사람과의 대화 세션 - WideSpread 임계값 검증용 가상추적 + 스테일컷 관측 로그 도입(코드 수정 + 재시작 완료)
사람이 "`MaxSpreadPct`=0.03% 임계값이 적정한지 다른 값이랑 비교해볼 수 있냐"고 질문 → `.ui` 로그
3일치(07-19~07-21)를 파싱해 WideSpread 스킵 이벤트 3,001건(고유 심볼 121종) 분석: 임계값을
0.05/0.08/0.1/0.15/0.2/0.3%로 완화하면 각각 81.4/51.2/34.0/11.2/4.2/0.6%만 여전히 걸러짐(즉
0.05%만 돼도 후보 558건, 0.1%면 1,981건이 추가로 열림). 단 **이 분석은 "후보량"만 보여줄 뿐
수익성은 전혀 알 수 없음** - `LogSkipped`가 스킵사유만 남기고 실측 스프레드값 자체를 저장하지
않았고, `TrendAligned1h` 스킵과 달리 `WideSpread` 스킵엔 가상추적(사후 forward 가격추적)이 붙어있지
않았기 때문. **조치 1**: `TryPromoteToTargetAsync`의 WideSpread 분기에 `SimulateWideSpreadForwardAsync`
신규 추가(`SimulateTrendVetoForwardAsync`와 동일한 30/60/180/300s forward 패턴, `TriggerPrice`
기준) - `spike_scalp_widespread_sim.jsonl`에 `SpreadPct`와 함께 기록. 같은 심볼이 300s 시뮬레이션
창 안에서 반복 스킵되면(`STARUSDT` 등 만성적으로 스프레드 넓은 코인이 표본을 왜곡) `_spreadSimCooldown`
으로 중복 시뮬레이션 방지. `LogSkipped`에도 `SpreadPct` 필드 추가(WideSpread 외 스킵사유는 null).
몇 사이클 쌓이면 스프레드 구간별 Fwd*Pct를 비교해 임계값 적정선을 데이터로 판단 가능(스프레드
자체의 슬리피지 비용은 차감 안 함 - 분석 시 SpreadPct/2 정도를 근사치로 뺄 것).

이어서 사람이 "SL(-0.3%)까지 기다리지 말고 진입 후 5~10초간 스테일(pnl 정체)이면 미리 손절하는 건
어떤지" 질문 → 오늘자(재부팅 후) 88건 확인 결과 SL 51건 중 22건(43%)이 `PeakPct=0`(단 한 틱도
유리한 방향 없이 그대로 SL) - 이 그룹은 정의상 TRAIL(0.4% 무장 필요)로 갈 가능성이 애초에 없어서
조기컷 리스크가 없어 보이나, **"TRAIL로 이긴 트레이드 중 초반 5~10초는 스테일이었다가 뒤늦게 터진
케이스가 있었는지"는 지금 로그(청산시점 최종 PeakPct만 기록, 시계열 없음)로는 검증 불가**함을 확인.
**조치 2**: `SpikeTarget`에 `RunStaleObservationAsync`/`LogStaleCheckpoint` 신규 추가 - 진입 후
5초/10초 시점의 pnl을 관측만 하고 청산 로직에는 전혀 관여하지 않음(`spike_scalp_stale_check.jsonl`).
`LogResult`에도 `EntryTimestamp` 필드 추가해 나중에 `(Symbol,EntryTimestamp)`로 두 파일을 조인,
"그 시점에 스테일이었던 트레이드의 최종 승률"을 실측할 수 있게 함 - 이 비율이 낮으면 그때 실제
조기컷 로직 도입 검토.

**빌드+재시작 완료(2026-07-21T16:56:06Z, KST 16:56, PID 12504→38596)** - 재시작 직전 확인:
FundingHedger는 13:00:00 KST DEXEUSDT 정산 이후 포지션 없음(13:59/14:59/15:59 스캔 전부 후보
0개), SpikeScalp는 16:51:10 BULLAUSDT(SL) 청산이 마지막이고 이후 스킵/취소만 발생(전부 DRY-RUN이라
어차피 무위험). 다음 FundingHedger 스캔(16:59 KST) 전에 안전하게 재시작 완료, 시작 로그 정상
확인(`🚀 [FUNDING-MGR] 시작`, `✅ [SPIKE] 광역스캔 구독 시작` 둘 다 확인).

⚠️ **다음 세션이 확인할 것**: (1) `spike_scalp_widespread_sim.jsonl`이 몇 사이클 쌓이면 스프레드
구간별(0.03~0.05%, 0.05~0.1%, 0.1%+) Fwd*Pct를 비교해 `MaxSpreadPct` 임계값을 완화할 근거가
있는지 판단할 것. (2) `spike_scalp_stale_check.jsonl`이 쌓이면 5초/10초 시점 pnl<=0이었던 트레이드
중 최종 TRAIL 승리 비율을 계산 - 낮으면(예: 5% 미만) 조기컷 로직 도입을, 무시 못할 수준이면
스테일컷 아이디어 자체를 재검토할 것. 둘 다 아직 관측 로그만 켠 상태로 실제 필터/청산 로직 변경은
없음.
인프라: 신규이슈 0건.
