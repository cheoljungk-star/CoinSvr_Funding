using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoinSvr
{
    // FundingHedger와 완전 독립된 테스트용 스파이크 모멘텀 스캘핑 설정.
    // StrategyConfig.cs(펀딩비 전략)와 자본/리스크/파라미터 전부 분리됨.
    public sealed class SpikeScalpConfig
    {
        // ── 감지 파라미터 ──
        public decimal SpikeThresholdPct { get; set; } = 0.8m;   // 60초 내 이 이상 가격변동시 트리거 후보
        public decimal VolumeSpikeMultiplier { get; set; } = 3m;  // 평소(15분 기준) 60초당 거래대금 증가분 대비 배수
        public int WideWindowSec { get; set; } = 60;              // 스파이크 판단 윈도우
        public int BaselineWindowSec { get; set; } = 900;         // 거래대금 평소 기준선(15분)

        // ── 진입/청산 파라미터 ──
        public decimal StopLossPct { get; set; } = 0.3m;          // 고정 SL
        public decimal TrailArmPct { get; set; } = 0.4m;          // 트레일링 무장 최소 피크(기존 TP값 재활용)
        public decimal TrailGivebackPct { get; set; } = 30m;      // 피크 대비 이 비율 반납시 청산(잠정값)
        public int MaxHoldMs { get; set; } = 180000;              // 최대보유 3분(1~5분 범위 내)
        public int CooldownMinutes { get; set; } = 10;            // 같은 심볼 재진입 방지

        // ── 자본/리스크 (FundingHedger와 완전 별도 카운터, 자동화가 절대 건드리지 않음) ──
        public decimal TestNotional { get; set; } = 50m;          // 포지션당 명목가치(USDT)
        public int MaxConcurrentPositions { get; set; } = 3;      // 동시보유 상한
        public int Leverage { get; set; } = 5;
        public decimal DailyLossLimitUsdt { get; set; } = 90000m;    // SpikeScalp 전용 일일손실한도(FundingHedger와 무관)
        // 2026-07-19 신규: 심볼 하나가 하루 동안 이 금액 이상 손실을 내면 그날은 그 심볼 재진입을 막는다
        // (ESPORTSUSDT 같은 특정 심볼 반복손실 집중 문제 대응, UTC 자정 기준 리셋). 0 이하면 비활성화.
        // DailyLossLimitUsdt와 동일하게 자동화 사이클이 건드리지 않는 안전장치 - 사람이 직접 조정.
        public decimal MaxDailyLossPerSymbolUsdt { get; set; } = 10m;
        public decimal MaxParamChangeRatio { get; set; } = 0.2m;  // 자동화 사이클당 파라미터 최대 변화폭 ±20%(안전장치, 사람만 수정)

        public decimal MaxTickPriceRatioPct { get; set; } = 0.1m; // FundingHedger와 동일 필터 재사용
        public int ConfirmTicks { get; set; } = 2;                 // 최근 N틱이 스파이크 방향과 일치해야 진입

        // 2026-07-20 사람과의 대화 세션: "특정 심볼에서 PeakPct=0(진입 직후 바로 불리)로 끝나는 트레이드가
        // 유독 잦다"는 관찰을 조사한 결과, tickSize/가격 비율(위 MaxTickPriceRatioPct)로는 이 심볼들이
        // 전혀 걸러지지 않음을 확인(전부 0.003~0.08%대로 기준치 0.1% 이내) - tickSize는 거래소가 정한
        // 최소 호가단위일 뿐, 실제 시장에서 형성되는 매수/매도 호가 간격(스프레드)과는 별개 수치이기
        // 때문. 공개 API로 실측한 결과 이 심볼들의 실시간 스프레드((ask-bid)/mid*100)는 0.017~0.135%로
        // 주요심볼(BTCUSDT 0.0002%, ETHUSDT 0.0005%, SOLUSDT 0.0131%)보다 뚜렷이 넓어, 얇은 유동성을
        // tickSize보다 훨씬 직접적으로 반영함을 확인 - 이 값을 초과하면 후보에서 제외한다(광역스캔은
        // 전 심볼 BookTicker를 구독하지 않아 스파이크가 실제로 확정된 시점에만 REST로 1회 조회).
        // ⚠️ 이 필터만으로 문제 심볼 전부가 걸러지진 않음(1000XECUSDT/ZHIPUUSDT는 스프레드 자체는
        // 낮은데도 stale 빈도가 높아 다른 원인일 가능성) - 완전한 해결책이 아니라 부분 완화로 도입.
        public decimal MaxSpreadPct { get; set; } = 0.03m;

        // 2026-07-20 사람과의 대화 세션: SL의 절반 가까이가 진입 5초 이내 발생(postexit Fwd값도 계속
        // 마이너스라 SL이 과도한 게 아니라 진입 자체가 이미 늦었던 것으로 확인) - 광역스캔 감지~실제
        // 주문 사이 지연 동안 트리거가 대비 이 값(%) 이상 반대방향으로 이미 움직였으면 진입을 취소한다.
        public decimal MaxEntryReversalPct { get; set; } = 0.1m;

        // 2026-07-20 사람과의 대화 세션: 위와 같은 분석에서 SL이 단일 틱 노이즈로 즉발 트리거되는
        // 것도 일부 기여할 수 있다고 보고, FundingHedger의 트레일링청산 디바운스(TRAIL_GIVEBACK_CONFIRM_TICKS)와
        // 동일한 취지로 SL에도 연속 확인 틱을 요구한다(트레일 자체는 건드리지 않음 - 범위 밖).
        public int SlConfirmTicks { get; set; } = 2;

        // 2026-07-18 백테스트(과거 완료 트레이드 1591건, 24h→1h 5개 윈도우 비교) 결과: 짧은 윈도우일수록
        // "추세와 반대방향 스파이크가 오히려 성과가 좋다"는 신호가 훨씬 강하게 나타남(1h gap -0.90 > 24h -0.14,
        // ExitReason 분해에서 추세일치 스파이크의 58.8%가 SL로 끝나는 반면 추세역행은 26.3%뿐 - 구조적 신호로 판단).
        // 그래서 "추세 반대만 거부"가 아니라 "추세와 같은 방향(따라가기)이면 거부"로 설계 변경함.
        // Trend1hPct의 절댓값이 이 값 이상이고, 스파이크 방향이 그 1h추세와 같으면 진입 스킵.
        public decimal Min1hAlignedVetoPct { get; set; } = 0.1m;
        public int Trend1hWindowSec { get; set; } = 3600; // 1h 추세 측정 윈도우(초)

        // 2026-07-18 사람과의 대화 세션: TrendAligned1h로 거부된 스파이크 중 "진짜 큰 돌파"(몇십%급)는
        // 순방향(추세추종)이 오히려 맞을 수 있다는 가설 검증용. 실주문 없이 가상 가격추적만 하며(자본
        // 리스크 없음), 이 값 미만은 추적 안 함(전량 추적시 표본폭주 - TrendAligned1h 스킵 2583건 중
        // |스파이크|>=3%는 247건 수준으로 확인됨). 필터 자체는 안 건드림, 로그 전용.
        public decimal LargeSpikeSimThresholdPct { get; set; } = 3m;

        // 2026-07-19 신규: spike_scalp_trendveto_sim.jsonl 표본(1,413건, 여러 심볼 분산)에서 "초반
        // 돌파"(|스파이크| LargeSpikeSimThresholdPct~이 값 사이)는 순방향 탑승 시 Fwd300이 뚜렷이
        // 플러스(3~5%대 +0.60%, 5~10%대 +0.29%)인 반면, 이 값을 넘는 극단화된 스파이크는 +0.05%로
        // 사실상 무의미(단일심볼 사례에선 마이너스)임을 확인 - 그래서 TrendAligned1h인 스파이크 중
        // [LargeSpikeSimThresholdPct, AlignedBreakoutMaxSpikePct] 구간만 스킵 대신 순방향 진입을
        // 허용한다(그 밖은 기존처럼 스킵 유지). 사람과의 대화 세션에서 도입, 표본이 더 쌓이면 이 값
        // 자체도 재조정 가능.
        public decimal AlignedBreakoutMaxSpikePct { get; set; } = 10m;

        [JsonIgnore] public static string FilePath => Path.Combine(AppContext.BaseDirectory, "spike_scalp_config.json");
        [JsonIgnore] public static string HistoryPath => Path.Combine(AppContext.BaseDirectory, "spike_scalp_config_history.jsonl");

        public static SpikeScalpConfig LoadOrDefault()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var cfg = JsonSerializer.Deserialize<SpikeScalpConfig>(json);
                    if (cfg != null) return cfg;
                }
            }
            catch { }
            var def = new SpikeScalpConfig();
            def.Save("초기 기본값 생성");
            return def;
        }

        public void Save(string reason)
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
                var entry = new { Timestamp = DateTime.UtcNow, Reason = reason, Config = this };
                File.AppendAllText(HistoryPath, JsonSerializer.Serialize(entry) + Environment.NewLine);
            }
            catch { }
        }
    }
}