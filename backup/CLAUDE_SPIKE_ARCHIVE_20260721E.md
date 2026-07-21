# CLAUDE_SPIKE.md 압축 아카이브 - 98회차 상세 (2026-07-21)

CLAUDE_SPIKE.md가 20KB 압축 임계값을 넘어 이동됨. 99회차 압축 시점에 분리.

## 자동분석 사이클 로그 [2026-07-21T15:53:00+09:00]
98회차. 표본: 신규 15건(results)/1309건(skipped). cutoff 2026-07-21T05:52:25Z~현재.
ExitReason 분해: TRAIL7(avgPnl0.1788,승률100%,avgPeak0.5242%)/SL8(avgPnl-0.1600,승률0%,avgPeak0.0731%),
TIMEOUT 0건. 순손익-0.0285USDT(건별평균-0.0019) - 97회차(+0.0717) 플러스전환 1회만에 재차 마이너스
전환.
발견한 패턴:
(A) 스테일진입가(PeakPct=0) 0/15(0.0%), SL중스테일 0/8(0.0%) - 94~96회차 등락 이후 이번 회차 최초로
0%까지 하락(n소표본 변동성 감안 필요, SL표본 전부 비스테일이라 "스테일=SL 주요동인" 결론은 이번엔
검증대상 아님).
(B) TRAIL giveback 7건 전부 63.3~71.4%대(ORDERUSDT66.7%/EPICUSDT63.7%/TREEUSDT65.9%/BANKUSDT63.3%/
ZHIPUUSDT71.4%/LAUSDT63.5%/BANKUSDT64.3%)로 목표(25%) 여전히 큰폭 초과 - 장기 구조적 이슈 불변.
(P) 스킵사유: TrendAligned1h70.1%(1위)/WideSpread17.2%/Cooldown8.7%/AlreadyActive3.8%/EntryFailed0.2%
- TrendAligned1h가 95~98회차 4연속 1위로 안정화 조짐 지속.
(F) Buy/Sell: Sell n=10 avg0.0105 승률50%, Buy n=5 avg-0.0267 승률40% - n소표본, 장기 무편향 결론
불변.
(E) TrendAligned1h aligned/counter: aligned n=0, counter n=15(avg-0.0019) - 이번 회차는 override
발생이 없어 전량 counter(비정렬) 진입뿐이라 aligned 표본 자체가 없어 우열비교 불가(97회차 aligned
유리 반례는 이번 회차 검증대상 아님).
(I) postexit TRAIL Fwd300 n=7 avg-1.2714 - 94·97회차의 강한 양전환(+1.17/+1.26)에서 이번엔 급격히
음전환, "불안정 패턴 지속" 장기결론 재확인(2회연속 일관 흐름이 다시 끊김). SL Fwd300 n=8 avg+0.4871 -
SL직후 반등 신호(StopLossPct는 사람 지시로 자동조정 대상 아님, ⚠️ 플래그만).
(Q) AlignedBreakoutOverride 신규 0건(누적 n=78, 25승53패, 순손익-1.22698 변동없음).
(L) trendveto_sim 신규 0건(95~98회차 4연속 0건).
(RSI) RSI[0,30)n=3avg0.1690승률100%/RSI[30,50)n=10avg-0.0229승률40%/RSI[60,70)n=2avg-0.1534승률0% -
[30,50)만 10건 충족, 낮은구간일수록 유리한 기존 방향은 유지되나 나머지구간 n부족으로 결론보류.
파라미터 변경: 없음 - 전체표본 15건은 최소기준(5건) 충족하나 ExitReason/RSI/%B 등 세부구간이 대부분
10건 미만이라 절차 6번 기준 미달, 확신부족으로 유지.
롤백: 형식조건 불성립(last3avg{96,97,98}=-0.0024 vs prev3avg{93,94,95}=-0.0635, last<prev 더이상
아님 - 96·97회차 2연속성립이 98회차에 끊김). 자동화 직접조정은 여전히 38회차뿐이라 실행 대상 아님.
인프라: 신규이슈 0건.
