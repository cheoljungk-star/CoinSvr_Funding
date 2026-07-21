CLAUDE_SPIKE.md 91회차 상세 로그 + 91회차 이후 사람과의 대화 세션 2건(TriggerPrice 로깅 추가,
MaxSpreadPct 신규 필터 도입) 원문 아카이브. 92회차 압축 시 이동.

## 자동분석 사이클 로그 [2026-07-20T21:53:40+09:00]
표본: 신규 27건(results)/1047건(skipped)/25건(postexit)/0건(trendveto_sim). ExitReason 분해: SL 21건
(avg-0.1782,승률0%)/TRAIL 5건(avg+0.2282,승률100%)/TIMEOUT 1건(avg+0.1074,승률100%). 전체 순손익
-2.4934 USDT, 건별평균 -0.0923 - 89회차(+0.0261)→세션(+0.0204)→90회차(-0.0529)에 이어 91회차도
마이너스 지속(3연속 마이너스, 89회차의 반전이 일시적이었을 가능성이 커짐).
발견한 패턴: (A) 스테일진입가(PeakPct=0) 40.7%(11/27), SL중 52.4%(11/21) - 90회차(42.1%/51.7%)와
유사 수준 유지, 장기결론 불변. (B) TRAIL giveback 5건 전부 62~74%대(TACUSDT62.9%/INTWUSDT64.4%/
HANAUSDT74.4%/SYNUSDT63.8%/AVAAIUSDT64.4%)로 목표(25%) 여전히 큰폭 초과, postexit Fwd값도 TRAIL
전구간 뚜렷한 양수(Fwd300 +1.3222)로 "청산이 일렀다" 신호 재확인 - FundingHedger 하드컷
(`TRAIL_GIVEBACK_HARD_MULT`) 적용검토 요청은 여전히 다음 사람 세션 대기중. (E) TrendAligned1h
aligned(n=2,avg-0.1169) vs counter(n=25,avg-0.0904) - aligned 표본 극소(n=2)라 결론 무의미,
표본부족 유지. (P) 스킵사유 TrendAligned1h 66.6%>Cooldown16.8%>AlreadyActive16.6% - 1위 유지
기조 지속(83회차 이래 9연속). RSI[30,50) 구간 n=17 avg-0.1208 승률17.6%로 계속 저조(87회차 이래
패턴 재확인). (Q) AlignedBreakoutOverride 이번 신규 1건(HANAUSDT, |SpikeChangePct|3.39%, SL패,
-0.5805) - [3,3.6) 구간에 재차 집중(87회차 이래 이 구간이 마이너스로 굳어진 패턴과 일치) - 누적
n=72(23승49패,승률31.9%,순손익-1.0704)로 악화 지속.
파라미터 변경: 없음 - 신규 표본 대부분 세부구간별 10건 미만이고, TRAIL giveback/override 문제는
기존처럼 config 클램프로 선택 조정 불가능한 구조로 이미 결론난 사안(확신부족 원칙 유지).
롤백: 해당없음 - last3avg(89,90,91)=-0.0397 > prev3avg(86,87,88)=-0.0525로 형식조건 불성립
(89회차부터 3연속 불성립 이어짐).

## 2026-07-20 사람과의 대화 세션 (91회차 이후) - TriggerPrice 로깅 추가 + 재빌드/재시작
사람이 "(A) 스테일 진입가(PeakPct=0)를 패치했냐"고 물어 조사 - **코드 버그가 아닌 것으로 판단,
패치하지 않았고 대신 향후 검증용 로깅만 추가함**.
- 조사 결과: (1) 이미 적용된 07-19 수정(`EntryPriceIsFallback`/`maxAgeMs=1000`)이 원인이 아님을
  확인 - fallback=true 트레이드의 PeakPct=0 비율(16.7%, 8/48)이 fallback=false(36.6%, 138/377)보다
  오히려 낮음. (2) FundingHedger와 달리 `OnPriceUpdate`는 BookTicker 콜백에서 스로틀 없이 직접
  호출되는 이미 완전한 이벤트기반 구조 - 폴링/스로틀 버그가 애초에 없음(FundingHedger 세션에서
  발견한 유형의 문제는 여기 해당 없음). (3) "진입가가 이미 국지적 고점/저점에서 체결된 것 아니냐"
  가설을 검증하려 했으나 `spike_scalp_results.jsonl`에 `TriggerPrice`가 없어 슬리피지 계산 불가 -
  데이터 부족으로 결론 못 냄.
- **조치**: 코드 수정 없이 `LogResult`(SpikeScalpManager.cs)에 `TriggerPrice`와 `EntrySlippagePct`
  (방향보정, 트리거가 대비 체결가 - 양수면 유리한 방향으로 더 간 뒤 체결) 필드만 추가해 다음
  사이클부터 이 가설을 실제로 검증할 수 있게 함. 빌드 확인 후 재시작 완료(PID 49000→53280, KST
  22:16:37) - 재시작 순간 진행 중이던 드라이런 트레이드 1건(PROMUSDT) 유실됐으나 실주문 없는
  시뮬레이션이라 실질적 영향 없음.
- **다음 세션이 할 일**: `EntrySlippagePct` 표본이 쌓이면 PeakPct=0 그룹과 정상 그룹의
  평균 슬리피지를 비교해 "체결가가 이미 국지적 극값 근처였다"는 가설을 검증할 것 - 이 필드는
  아직 신규 도입이라 표본 0건에서 시작(정상, 다음 몇 사이클은 표본부족일 수 있음).

## 2026-07-20 사람과의 대화 세션 (후속) - MaxSpreadPct 신규 필터 도입 + 재빌드/재시작
사람이 "고-stale 심볼들이 저번 역방향/순방향(TrendAligned1h·AlignedBreakoutOverride) 필터와
관련있는지"를 물어 결합 분석(라이브 431건+아카이브 541건=972건) - **무관함을 확인**: 고스테일
심볼군의 override비율(7.5%)·추세정렬비율(15.8%)이 전체 평균(7.4%/17.5%)과 사실상 동일했고,
1000XECUSDT/SOLVUSDT/GUSDT는 override·정렬 0%인데도(전량 역방향 진입) stale 55~80%로 여전히
높아 이 필터와 완전히 독립된 현상임을 확인.
**MaxTickPriceRatioPct 필터가 이미 있었다는 걸 뒤늦게 확인**(내가 직전 턴에 "필터가 없다"고
잘못 말한 것 정정) - 그런데 공개 API로 고스테일 심볼들의 실제 tickSize/가격 비율을 실측하니
전부 0.003~0.08%로 기존 임계값(0.1%) 이내라 이 필터로는 애초에 안 걸러지고 있었음. 대신
**실시간 호가스프레드((ask-bid)/mid*100)를 실측**하니 0.017~0.135%로 주요심볼(BTC 0.0002%/
ETH 0.0005%/SOL 0.0131%)보다 뚜렷이 넓음을 확인 - tickSize(거래소가 정한 최소단위)와 실제
시장 스프레드(유동성이 결정)는 서로 다른 수치라는 게 핵심. **조치**: `SpikeScalpConfig.cs`에
`MaxSpreadPct`(기본0.03%) 신규 필드 추가, `SpikeScalpManager.cs`의 `TryPromoteToTargetAsync`에
스파이크 확정 시점 1회 REST(`GetBookPriceAsync`) 조회로 체크 추가(광역스캔은 전 심볼 BookTicker
미구독이라 상시감시는 불가, 확정된 후보만 개별조회). `WideSpread`라는 신규 SkipReason으로 기록됨.
⚠️ **한계**: 이 필터가 고스테일 심볼 전부를 잡아내진 못함 - 1000XECUSDT(스프레드 0.013%)·
ZHIPUUSDT(0.018%)는 스프레드 자체가 낮은데도 stale이 잦아 다른 원인일 가능성(부분완화로 도입,
완전 해결책 아님).
빌드 확인(CS 에러 0) 후 재시작 완료(PID 53280→40448, KST 22:29:54) - 재시작 시점 진행 중이던
드라이런 트레이드 없음(안전), FundingHedger 다음 스캔(13:59 UTC)까지 29분 여유 확인 후 진행.
**다음 사이클이 확인할 것**: `WideSpread` 스킵 발생 빈도, 그리고 이 필터 적용 이후 신규
트레이드에서 고스테일 심볼(특히 AERGOUSDT/XVGUSDT/TAGUSDT/PROMUSDT/LABUSDT류 - 스프레드
0.03% 초과군)의 재등장 빈도가 실제로 줄어드는지 - `EntrySlippagePct`와 함께 병행 관찰.
