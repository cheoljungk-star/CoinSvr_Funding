# CLAUDE_SPIKE.md 압축 아카이브 (2026-07-21F) — 99회차 상세 + 같은 날 사람과의 대화 세션 원문

이 파일은 `CLAUDE_SPIKE.md`가 20KB 압축 임계값을 넘어 100회차 기록 시점에 이동된 내용이다.

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
