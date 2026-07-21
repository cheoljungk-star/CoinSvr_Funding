# CLAUDE_SPIKE.md 압축 아카이브 (97회차 상세 원문)

이 파일은 CLAUDE_SPIKE.md가 20KB 압축 임계값을 넘어 97회차 상세를 이동한 것이다.
98회차 압축 시점(2026-07-21T15:53:00+09:00경)에 분리됨.

## 자동분석 사이클 로그 [2026-07-21T14:53:22+09:00]
97회차. 표본: 신규 7건(results)/1116건(skipped). cutoff 2026-07-21T04:52:23Z~현재.
ExitReason 분해: TRAIL4(avgPnl0.1940,승률100%,avgPeak0.5764%)/SL2(avgPnl-0.1644,승률0%,avgPeak0)/
TIMEOUT1(avgPnl0.0551,승률100%). 순손익0.5021USDT(건별평균0.0717) - 90회차 이래 처음 플러스 전환
(89(+0.0261)이후 첫 플러스).
발견한 패턴:
(A) 스테일진입가(PeakPct=0) 2/7(28.6%), SL중 스테일 2/2(100%) - "스테일이 SL의 주요동인" 결론
재확인(n소표본).
(B) TRAIL giveback 4건 전부 63.0~72.0%대(ZHIPUUSDT72.0%/ORDERUSDT64.3%/BLESSUSDT66.0%/
BANKUSDT63.0%)로 목표(25%) 여전히 큰폭 초과 - 장기 구조적 이슈 불변.
(P) 스킵사유: TrendAligned1h68.6%(1위)/WideSpread19.4%/Cooldown8.3%/AlreadyActive3.4%/
EntryFailed0.2% - TrendAligned1h가 95~97회차 3연속 1위로 안정화 조짐 지속.
(F) Buy/Sell: Buy n=2 avg0.1133 승률100%, Sell n=5 avg0.0551 승률60% - n극소표본이라 판단보류
(장기 무편향 결론 불변).
(E) TrendAligned1h aligned/counter: aligned n=3 avg0.1673, counter n=4 avg0.0001 - 이번 회차는
aligned가 더 좋았음(장기 "구조적 우열없음" 가설과 배치되나 n=3/4 극소표본이라 반례로만 기록).
(I) postexit TRAIL Fwd300 n=4 avg+1.2620 - 94회차(+1.1722)에 이어 2회 연속 강한 양전환, giveback이
여전히 타이트하다는 신호 재확인(불안정패턴 지속중이나 최근 2회는 일관).
(Q) AlignedBreakoutOverride 신규1건(BANKUSDT, +0.27522, 승리) - 누적n=78(25승53패,순손익
-1.22698), 96회차 3연속손실 이후 첫 승리 사례지만 누적은 여전히 큰폭 마이너스.
(L) trendveto_sim 신규 0건(95·96회차에 이어 3연속 0건).
파라미터 변경: 없음 - 표본부족/확신부족(전체 7건, 세부구간 대부분 10건 미만).
롤백: 형식조건 2회연속성립(last3avg-0.0477<prev3avg-0.0373, 96회차부터 연속) - 자동화 자신이
마지막으로 config를 조정한 것은 여전히 38회차뿐이라 84회차 판단원칙대로 기계적 실행은 계속 보류.
인프라: 신규이슈 0건.
