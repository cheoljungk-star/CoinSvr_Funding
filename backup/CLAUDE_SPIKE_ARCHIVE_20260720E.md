# CLAUDE_SPIKE.md 압축 아카이브 (80회차 상세, 2026-07-20 생성)

이 파일은 `CLAUDE_SPIKE.md`가 20KB 압축 임계값을 넘어 81회차 기록 시 80회차 상세 전문을
이곳으로 옮긴 것이다. `CLAUDE_SPIKE.md`의 "과거 요약" 문단에는 이 회차의 핵심만 요약되어 있다.

## 자동분석 사이클 로그 [2026-07-20T08:53:15+09:00] (80회차)
- 표본: 신규 23건(results)/990건(skipped)/22건(postexit)/4건(trendveto_sim). 컷오프
  2026-07-19T22:52:28Z ~ 2026-07-19T23:51:20Z(약 1시간).
- ExitReason 분해: TRAIL n=9 avgPnl=+0.1918/승률100%/avgPeak=0.5523% vs SL n=12 avgPnl=-0.1563/
  승률0%/avgPeak=0.1061% vs TIMEOUT n=2 avgPnl=+0.0808/승률100%. 전체 순손익 +0.0119USDT(건별
  +0.0005) - 79회차(+0.0369)에 이어 2연속 플러스이나 거의 0에 수렴, TRAIL 100%승/SL 0%승 구조는
  변함없이 지속(장기 구조적 패턴).
- 스테일 진입가(A): 4/23(17.4%), SL중 4/12(33.3%). 79회차 7.7%(역대최저권)에서 재상승 - 78→79→80
  (22.2%→7.7%→17.4%)로 등락 지속, 개선추세 확정 아님.
- 극단치(≤-1.5USDT): 0/23(0%).
- TRAIL giveback: 9건 평균 약 65.3%(SKHYNIXUSDT 64.7%/DRAMUSDT 63.6%/SKHYUSDT 62.8%/BULLAUSDT
  65.1%/AIOUSDT 63.0%/JCTUSDT 70.3%/METUSDT 66.8%/SKLUSDT 63.7%/KORUUSDT 65.3%) - 목표
  25~30%를 여전히 큰 폭 초과, 16회차 확정 결론("폴링 고정지연" 구조적 원인, 추가조치 불필요)
  그대로 유지.
- 스킵 사유: TrendAligned1h 62.1%/Cooldown 21.3%/AlreadyActive 16.6%(DailyLossLimit 0건).
- Buy/Sell(F): Sell n=14 avg+0.0523/승률64.3% vs Buy n=9 avg-0.0801/승률22.2% - 79회차(Sell
  n=10>Buy n=3)에 이어 2연속 Sell우위.
- RSI(14)/%B(20): n≥10 구간 2개 확보(RSI[30,50) n=15 avg+0.0076/승률46.7%, %B[<0.2) n=13
  avg+0.0228/승률53.8%) - 나머지 구간은 표본부족, 결론 보류.
- TrendAligned1h 정렬교차(E): aligned n=2 avg+0.1109 vs counter n=21 avg-0.0100 - aligned우위,
  78·79회차에 이어 3연속 aligned>counter(단 n=2로 여전히 극소표본, 76회차 제기된 "구조적 우열
  없음" 반례 가설 검증은 표본 확대 전까지 보류).
- postexit(I): TRAIL Fwd300avg=-0.0239%(n=9) - 78·79회차에 이어 3연속 음전환(giveback을 더
  타이트하게 가도 된다는 신호가 계속 쌓이는 중이나 아직 조정 단계 아님). SL Fwd300avg=+0.0037%
  (n=11) - 거의 0에 가까운 미세 양전환, 79회차까지의 "SL 성급 가능성" 플래그 규모가 이번엔
  사실상 무의미한 수준으로 축소.
- trendveto_sim(L): 신규 4건, BANKUSDT 75.0%(3/4) 쏠림 + PROMUSDT 1건. 전체 Fwd300 평균
  -0.3447(BANKUSDT제외 n=1 -2.7419) - 79회차의 강한 양전환(+2.7153, AZTECUSDT쏠림에도 유지)이
  80회차에 재차 음전환으로 뒤집힘. "패턴 확정"보다 "심볼 구성에 극도로 민감한 불안정 지표"라는
  기존 재해석이 이번 반전으로 재차 뒷받침됨. 크기버킷은 [3,5) n=4뿐, [5,10)/[10,∞) 표본 0건.
- 파라미터 변경: 없음 - 신규 23건은 최소기준(5건)은 넘지만 ExitReason별(TRAIL9/SL12/TIMEOUT2)·
  aligned(n=2)·trendveto_sim(n=4) 등 세부 신호가 대부분 소표본이라 확신부족, "확신 없으면 안
  바꾼다" 원칙 유지.
- 롤백 조건: last3avg(78·79·80 평균 +0.0012) > prev3avg(75·76·77 평균 -0.0428) → 불성립(개선
  방향 유지).
- 인프라 확인: `rotate_data_logs.ps1` 실행 결과 아카이브 0건(여전히 UTC 07-19 이내, `.ui` 로그
  폴더는 이미 07-20으로 넘어갔으나 데이터 로테이션 기준(UTC)은 아직 미전환 - 혼동 주의 유지).
  `spike_analysis_state.json` 정상 갱신 확인. 신규 인프라 이슈 0건.
- ⚠️ `spike_automation_summary.log`가 20KB 임계값(20,386바이트)을 초과해 1~72회차를
  `backup/SPIKE_AUTOMATION_SUMMARY_ARCHIVE_20260720.log`로 이동, 73~80회차만 남기고 압축함.
  `CLAUDE_SPIKE.md`는 이번 회차 기준 아직 임계값 미만(추가 압축 불필요).
