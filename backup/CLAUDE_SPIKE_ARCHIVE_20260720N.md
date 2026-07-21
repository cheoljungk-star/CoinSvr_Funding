# CLAUDE_SPIKE.md 압축 아카이브 (2026-07-20N) - 89회차 이후 사람과의 대화 세션 노트 원문

## 2026-07-20 사람과의 대화 세션 (89회차 이후, FundingHedger 세션과 병행 진행)
사람이 FundingHedger 쪽 대화 세션 중 "스파이크 쪽은 왜 분석 안 해주냐"고 요청해 `analyze_spike_cycle.py`를
직접 실행(컷오프 2026-07-20T08:52:40Z~현재, 상태파일 자동갱신됨). **코드/파라미터 변경은 하지 않음
- 아래는 관찰 결과 기록만**.
- 신규 results 58건 / skipped 2519건 / postexit 55건 / trendveto_sim 0건.
- ExitReason 분해: TRAIL n=24 avg+0.2651 승률100% / SL n=30 avg-0.1681 승률0% / TIMEOUT n=4
  avg-0.0339 승률0%. 전체 순손익 +1.1850USDT, 건별평균 +0.0204 - 89회차(+0.0261)에 이어 플러스 유지.
- (A) 스테일 진입가(PeakPct=0) 32.8%(19/58), SL중 60.0%(18/30) - 여전히 SL의 주요 동인이라는 기존
  결론과 일치.
- (B) TRAIL giveback: FWDIUSDT/BANKUSDT/KITEUSDT 등 24건 전부 peak 대비 realized가 대략 35~40%
  수준으로 수렴(예: FWDIUSDT peak0.4325→realized0.1546=35.7%, AKEUSDT peak1.7603→realized0.6616=
  37.6%) - **이 세션에서 FundingHedger 쪽(`Fundinghedger .cs`)의 동일 패턴(giveback이 폴링갭+
  디바운스 compounding으로 설정치를 초과)을 근본원인 규명 후 코드 완화(하드컷)+파라미터 하향으로
  대응했음(CLAUDE.md 참고)** - SpikeScalp의 `WaitForReversalOrTimeoutAsync`류 로직도 동일 구조라면
  같은 완화가 적용 가능할 수 있음 - **다음 스파이크 세션에서 SpikeScalpManager.cs의 트레일링 로직이
  FundingHedger와 같은 폴링+디바운스 구조인지 확인하고, 같다면 동일한 하드컷 완화 적용을 검토할 것**
  (16회차에 "구조적 원인 확정, 추가조치 불필요"로 결론났던 것을 재검토할 근거가 될 수 있음).
- (Q) `AlignedBreakoutOverride`: 기능 도입(07-20T00:30 재시작) 이후 누적 65건(23승42패, 승률35.4%,
  순손익 +0.4548, 건별평균+0.0070) - ExitReason별 TRAIL n=23(승률100%,avg+0.3541) / SL n=42(승률0%,
  avg-0.1831)로 전체 구조와 동일 패턴. 89회차까지의 "84~87회차 3연속 악화 후 88·89회차 반등" 흐름과
  이어보면 소폭이나마 누적 플러스 유지 중.
- 다음 세션이 알아야 할 것: 위 (B) TRAIL giveback 구조적 재검토 건, RSI/%B/TrendAligned1h 등 세부
  교차분석은 이번엔 스크립트 표준출력만 확인하고 CLAUDE_SPIKE.md 서술은 깊게 갱신하지 않음(FundingHedger
  코드수정이 이 세션의 주 목적이라 스파이크 쪽은 가볍게 훑음) - 다음 정규 사이클에서 통상 절차대로
  전체 분해 이어갈 것.
