using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoinSvr
{
    // Claude Code 자동화가 6시간마다 값을 제안/수정하는 대상.
    // 실제 반영은 항상 ApplyBoundedChange를 통해서만 이뤄져 사이클당 변경폭이 캡핑됨.
    public sealed class StrategyConfig
    {
        // ── 튜닝 대상 (자동화가 수정) ──
        public decimal TrailGivebackPct { get; set; } = 35m;
        public decimal TrailMinPeakPct { get; set; } = 0.05m;
        public decimal MinFundingPct { get; set; } = 0.15m;
        public int EntryConfirmTicks { get; set; } = 2;
        public int EntryMaxWaitMs { get; set; } = 6000;
        public decimal PreTrendSkipPct { get; set; } = 0.15m;
        public int MinHoldMs { get; set; } = 3000;
        public int MaxHoldMs { get; set; } = 15000;
        // tick/price 비율(%) 임계값 - 이 초과 심볼은 후보선정에서 제외(2026-07-15 신규, GWEIUSDT 사례).
        // 잠정 기본값 0.1% - GWEIUSDT(0.21%)는 걸러지고 정상군(0.02%대)은 통과하는 수준.
        public decimal MaxTickPriceRatioPct { get; set; } = 0.1m;

        // ── 안전장치 (자동화가 절대 건드리지 않음. 사람이 수동으로만 변경) ──
        public decimal DailyLossLimitUsdt { get; set; } = 30m;
        public decimal MaxParamChangeRatio { get; set; } = 0.2m; // 사이클당 파라미터 최대 변화폭 ±20%

        [JsonIgnore] public static string FilePath => Path.Combine(AppContext.BaseDirectory, "strategy_config.json");
        [JsonIgnore] public static string HistoryPath => Path.Combine(AppContext.BaseDirectory, "strategy_config_history.jsonl");

        public static StrategyConfig LoadOrDefault()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var cfg = JsonSerializer.Deserialize<StrategyConfig>(json);
                    if (cfg != null) return cfg;
                }
            }
            catch { }
            var def = new StrategyConfig();
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

        // 자동화가 제안한 값을 현재값 대비 ±MaxParamChangeRatio 범위로 클램프해서 저장.
        // DailyLossLimitUsdt/MaxParamChangeRatio(안전장치)는 제안값 무시하고 기존값 유지.
        public StrategyConfig ApplyBoundedChange(StrategyConfig proposed, string reason)
        {
            decimal Clamp(decimal cur, decimal prop)
            {
                if (cur == 0) return prop;
                decimal maxDelta = Math.Abs(cur) * MaxParamChangeRatio;
                decimal delta = Math.Max(-maxDelta, Math.Min(maxDelta, prop - cur));
                return cur + delta;
            }
            int ClampInt(int cur, int prop) => (int)Math.Round(Clamp(cur, prop));

            var next = new StrategyConfig
            {
                TrailGivebackPct = Clamp(TrailGivebackPct, proposed.TrailGivebackPct),
                TrailMinPeakPct = Clamp(TrailMinPeakPct, proposed.TrailMinPeakPct),
                MinFundingPct = Clamp(MinFundingPct, proposed.MinFundingPct),
                EntryConfirmTicks = ClampInt(EntryConfirmTicks, proposed.EntryConfirmTicks),
                EntryMaxWaitMs = ClampInt(EntryMaxWaitMs, proposed.EntryMaxWaitMs),
                PreTrendSkipPct = Clamp(PreTrendSkipPct, proposed.PreTrendSkipPct),
                MinHoldMs = ClampInt(MinHoldMs, proposed.MinHoldMs),
                MaxHoldMs = ClampInt(MaxHoldMs, proposed.MaxHoldMs),
                MaxTickPriceRatioPct = Clamp(MaxTickPriceRatioPct, proposed.MaxTickPriceRatioPct),
                DailyLossLimitUsdt = DailyLossLimitUsdt,
                MaxParamChangeRatio = MaxParamChangeRatio
            };
            next.Save(reason);
            return next;
        }
    }
}