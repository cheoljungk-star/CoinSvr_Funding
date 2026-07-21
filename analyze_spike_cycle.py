# -*- coding: utf-8 -*-
"""
CoinSvr SpikeScalp 자동분석 사이클용 고정 분석 스크립트 (2026-07-19 통합판).

기존에는 analyze_spike_cycle.py(신규 레코드 추출 + _tmp_*.json 덤프)와
analyze_spike_cycle2.py(그 tmp json을 읽어 통계 출력) 두 단계로 나뉘어 있었고,
CUTOFF 상수를 매 사이클 Claude가 CLAUDE_SPIKE.md를 보고 수동으로 편집해야 했다.
이 스크립트는 그 두 단계를 하나로 합치고, CUTOFF도 spike_analysis_state.json의
LastCutoffUtc를 자동으로 읽고 갱신한다 - 더 이상 수동 편집이 필요 없다.

또한 rotate_data_logs.ps1이 "오늘 이전" 날짜를 DataArchive\\<yyyyMMdd>\\<파일명>으로
옮기므로, 컷오프가 과거 날짜에 걸치면 그 아카이브도 함께 읽는다(라이브 파일에는
오늘 것만 남아있음).

사용법:
    python analyze_spike_cycle.py                 # 상태파일 기준 자동 컷오프
    python analyze_spike_cycle.py --since-hours 6 # 상태파일 무시하고 강제로 N시간 전부터
    python analyze_spike_cycle.py --cutoff 2026-07-19T00:00:00Z  # 특정 시각부터(수동 지정)

이 스크립트는 숫자 집계까지만 담당한다 - 해석/서술(CLAUDE_SPIKE.md, spike_automation_summary.log에
적을 문장)은 Claude가 직접 작성한다.
"""
import argparse
import json
import sys
from collections import defaultdict
from datetime import datetime, timedelta, timezone
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

BASE = Path(r"D:\000.WORK\000.NET\CoinSvr_Funding\CoinSvr\bin\Debug\net9.0")
ARCHIVE_ROOT = BASE / "DataArchive"
STATE_FILE = BASE / "spike_analysis_state.json"

FILES = ["spike_scalp_results.jsonl", "spike_scalp_skipped.jsonl",
         "spike_scalp_postexit.jsonl", "spike_scalp_trendveto_sim.jsonl"]


def parse_ts(s):
    return datetime.fromisoformat(s.replace("Z", "+00:00"))


def read_jsonl(path):
    if not path.exists():
        return []
    out = []
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                out.append(json.loads(line))
            except Exception:
                continue
    return out


def read_since(filename, cutoff_dt):
    """라이브 파일 + (컷오프가 과거 날짜에 걸치면) 해당 날짜들의 DataArchive도 합쳐서 읽는다."""
    today_utc = datetime.now(timezone.utc).date()
    records = []

    d = cutoff_dt.date()
    while d < today_utc:
        archive_path = ARCHIVE_ROOT / d.strftime("%Y%m%d") / filename
        records.extend(read_jsonl(archive_path))
        d += timedelta(days=1)

    records.extend(read_jsonl(BASE / filename))

    out = []
    for rec in records:
        ts = rec.get("Timestamp")
        if not ts:
            continue
        try:
            dt = parse_ts(ts)
        except Exception:
            continue
        if dt >= cutoff_dt:
            out.append(rec)
    return out


def avg(lst):
    return sum(lst) / len(lst) if lst else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--since-hours", type=float, default=None,
                     help="상태파일을 무시하고 이 시간(시간단위) 전부터 강제 분석")
    ap.add_argument("--cutoff", type=str, default=None,
                     help="상태파일을 무시하고 이 UTC ISO 시각부터 강제 분석 (예: 2026-07-19T00:00:00Z)")
    args = ap.parse_args()

    now_utc = datetime.now(timezone.utc)
    explicit = args.since_hours is not None or args.cutoff is not None

    if args.cutoff:
        cutoff_dt = parse_ts(args.cutoff)
        print(f"명시적 --cutoff 사용: {args.cutoff}")
    elif args.since_hours is not None:
        cutoff_dt = now_utc - timedelta(hours=args.since_hours)
        print(f"명시적 --since-hours={args.since_hours} 사용")
    elif STATE_FILE.exists():
        state = json.loads(STATE_FILE.read_text(encoding="utf-8-sig"))
        cutoff_dt = parse_ts(state["LastCutoffUtc"])
        print("상태파일 기반 컷오프 사용(spike_analysis_state.json)")
    else:
        cutoff_dt = now_utc - timedelta(hours=1)
        print("상태파일 없음 - 1시간 전 폴백 사용(최초 실행으로 추정)")

    results = read_since("spike_scalp_results.jsonl", cutoff_dt)
    skipped = read_since("spike_scalp_skipped.jsonl", cutoff_dt)
    postexit = read_since("spike_scalp_postexit.jsonl", cutoff_dt)
    trendveto = read_since("spike_scalp_trendveto_sim.jsonl", cutoff_dt)

    # 분석 결과와 무관하게 항상 상태를 갱신한다(다음 사이클이 정확히 이어받도록).
    # BOM 없는 UTF-8로 직접 써서 C#/PowerShell 쪽 파일들과 인코딩을 통일한다.
    STATE_FILE.write_text(
        json.dumps({"LastCutoffUtc": now_utc.strftime("%Y-%m-%dT%H:%M:%SZ")}),
        encoding="utf-8",
    )

    print("=" * 64)
    print(f" 분석 구간: cutoff UTC {cutoff_dt.strftime('%Y-%m-%dT%H:%M:%SZ')} ~ 현재")
    print(f"신규 results: {len(results)}")
    print(f"신규 skipped: {len(skipped)}")
    print(f"신규 postexit: {len(postexit)}")
    print(f"신규 trendveto_sim: {len(trendveto)}")
    print("=" * 64)

    if results:
        ts_list = [r.get("Timestamp") for r in results]
        print("results ts range:", min(ts_list), "~", max(ts_list))

    if not results and not skipped:
        print("신규 레코드 없음 - 이번 구간 분석 대상 없음.")
        return

    print()
    print("=== 1. ExitReason 분해 ===")
    if results:
        by_reason = defaultdict(list)
        for r in results:
            by_reason[r["ExitReason"]].append(r)
        total_pnl = sum(r["RealizedUsdt"] for r in results)
        for reason, recs in by_reason.items():
            pnls = [r["RealizedUsdt"] for r in recs]
            wins = sum(1 for p in pnls if p > 0)
            peaks = [r["PeakPct"] for r in recs]
            print(f"{reason}: n={len(recs)} avgPnl={avg(pnls):.4f} winrate={wins/len(recs)*100:.1f}% avgPeak={avg(peaks):.4f}%")
        print(f"전체 순손익: {total_pnl:.4f} USDT, 건별평균: {total_pnl/len(results):.4f}")
    else:
        print("results 없음")

    print()
    print("=== 2. 스테일 진입가(PeakPct=0) 비중 ===")
    if results:
        stale = [r for r in results if r["PeakPct"] == 0]
        print(f"PeakPct=0: {len(stale)}/{len(results)} ({len(stale)/len(results)*100:.1f}%)")
        sl_only = [r for r in results if r["ExitReason"] == "SL"]
        if sl_only:
            stale_sl = [r for r in sl_only if r["PeakPct"] == 0]
            print(f"SL중 스테일: {len(stale_sl)}/{len(sl_only)} ({len(stale_sl)/len(sl_only)*100:.1f}%)")

    print()
    print("=== 3. 극단치(<=-1.5 USDT) ===")
    if results:
        extreme = [r for r in results if r["RealizedUsdt"] <= -1.5]
        for r in extreme:
            print(f"  {r['Symbol']} {r['Side']} {r['RealizedUsdt']:.4f} stale={r['PeakPct']==0}")
        print(f"극단치 {len(extreme)}/{len(results)} ({len(extreme)/len(results)*100:.1f}%)")

    print()
    print("=== 4. TRAIL giveback 비율 ===")
    if results:
        trail = [r for r in results if r["ExitReason"] == "TRAIL"]
        print(f"TRAIL n={len(trail)}")
        for r in trail:
            print(f"  {r['Symbol']} peak={r['PeakPct']:.4f} realized={r['RealizedUsdt']:.4f}")

    print()
    print("=== 5. 스킵 사유 분해 ===")
    if skipped:
        skip_reasons = defaultdict(int)
        for s in skipped:
            skip_reasons[s["SkipReason"]] += 1
        total_skip = len(skipped)
        for reason, cnt in sorted(skip_reasons.items(), key=lambda x: -x[1]):
            print(f"  {reason}: {cnt} ({cnt/total_skip*100:.1f}%)")
    else:
        print("skipped 없음")

    print()
    print("=== 6. Buy/Sell 비교 ===")
    if results:
        by_side = defaultdict(list)
        for r in results:
            by_side[r["Side"]].append(r["RealizedUsdt"])
        for side, pnls in by_side.items():
            wins = sum(1 for p in pnls if p > 0)
            print(f"{side}: n={len(pnls)} avg={avg(pnls):.4f} winrate={wins/len(pnls)*100:.1f}%")

    print()
    print("=== 7. RSI(14)/%B(20) 구간분석 ===")
    if results:
        rsi_bins = [(0, 30), (30, 50), (50, 60), (60, 70), (70, 80), (80, 100.0001)]
        pctb_bins = [(-999, 0.2), (0.2, 0.4), (0.4, 0.6), (0.6, 0.8), (0.8, 1.0), (1.0, 999)]
        rsi_valid = [r for r in results if r.get("DirRsi14") is not None]
        print(f"DirRsi14 not-null: {len(rsi_valid)}/{len(results)}")
        for lo, hi in rsi_bins:
            bucket = [r for r in rsi_valid if lo <= r["DirRsi14"] < hi]
            if bucket:
                pnls = [r["RealizedUsdt"] for r in bucket]
                wins = sum(1 for p in pnls if p > 0)
                print(f"  RSI[{lo},{hi}): n={len(bucket)} avg={avg(pnls):.4f} winrate={wins/len(bucket)*100:.1f}%")
        pctb_valid = [r for r in results if r.get("DirPctB20") is not None]
        print(f"DirPctB20 not-null: {len(pctb_valid)}/{len(results)}")
        for lo, hi in pctb_bins:
            bucket = [r for r in pctb_valid if lo <= r["DirPctB20"] < hi]
            if bucket:
                pnls = [r["RealizedUsdt"] for r in bucket]
                wins = sum(1 for p in pnls if p > 0)
                print(f"  %B[{lo},{hi}): n={len(bucket)} avg={avg(pnls):.4f} winrate={wins/len(bucket)*100:.1f}%")

    print()
    print("=== 8. TrendAligned1h 정렬여부 교차분석 ===")
    if results:
        def is_aligned(r):
            trend1h = r.get("Trend1hPct")
            spike = r.get("SpikeChangePct")
            if trend1h is None or spike is None:
                return None
            return (trend1h > 0 and spike > 0) or (trend1h < 0 and spike < 0)

        aligned_pnls, counter_pnls = [], []
        for r in results:
            al = is_aligned(r)
            if al is True:
                aligned_pnls.append(r["RealizedUsdt"])
            elif al is False:
                counter_pnls.append(r["RealizedUsdt"])
        print(f"aligned(1h와 스파이크방향 동일): n={len(aligned_pnls)} avg={avg(aligned_pnls)}")
        print(f"counter(1h와 반대): n={len(counter_pnls)} avg={avg(counter_pnls)}")

    print()
    print("=== 9. postexit 분석 (ExitReason별 Fwd) ===")
    if postexit:
        pe_by_reason = defaultdict(list)
        for p in postexit:
            pe_by_reason[p["ExitReason"]].append(p)
        for reason, recs in pe_by_reason.items():
            f30 = [r["Fwd30sPct"] for r in recs if r.get("Fwd30sPct") is not None]
            f60 = [r["Fwd60sPct"] for r in recs if r.get("Fwd60sPct") is not None]
            f180 = [r["Fwd180sPct"] for r in recs if r.get("Fwd180sPct") is not None]
            f300 = [r["Fwd300sPct"] for r in recs if r.get("Fwd300sPct") is not None]
            print(f"{reason}: n={len(recs)} Fwd30avg={avg(f30)} Fwd60avg={avg(f60)} Fwd180avg={avg(f180)} Fwd300avg={avg(f300)}(n={len(f300)})")
    else:
        print("postexit 신규 없음")

    print()
    print("=== 10. trendveto_sim 분석 ===")
    print(f"trendveto_sim 신규: {len(trendveto)}")
    if trendveto:
        sym_count = defaultdict(int)
        for t in trendveto:
            sym_count[t["Symbol"]] += 1
        for sym, cnt in sorted(sym_count.items(), key=lambda x: -x[1])[:5]:
            print(f"  {sym}: {cnt} ({cnt/len(trendveto)*100:.1f}%)")
        f300_all = [t["Fwd300sPct"] for t in trendveto if t.get("Fwd300sPct") is not None]
        print(f"전체 Fwd300 평균: {avg(f300_all)} (n={len(f300_all)})")
        top_sym = max(sym_count, key=sym_count.get)
        f300_excl = [t["Fwd300sPct"] for t in trendveto if t["Symbol"] != top_sym and t.get("Fwd300sPct") is not None]
        print(f"{top_sym} 제외 Fwd300 평균: {avg(f300_excl)} (n={len(f300_excl)})")
        buckets = [(3, 5), (5, 10), (10, 999)]
        for lo, hi in buckets:
            b = [t["Fwd300sPct"] for t in trendveto
                 if t.get("SpikeChangePct") is not None and lo <= abs(t["SpikeChangePct"]) < hi and t.get("Fwd300sPct") is not None]
            print(f"  크기[{lo},{hi}): n={len(b)} avg={avg(b)}")

    print()
    print("=== 11. 이번 사이클 건별평균(롤백조건 계산용 - 직전 사이클들과 수동 비교 필요) ===")
    if results:
        total_pnl = sum(r["RealizedUsdt"] for r in results)
        print(f"이번 사이클 건별평균: {total_pnl/len(results):.4f} (n={len(results)})")

    print()
    print("=" * 64)
    print(" 분석 종료 - 위 결과를 근거로 CLAUDE_SPIKE.md / spike_automation_summary.log에")
    print(" 서술형 요약을 작성할 것. 이 스크립트는 숫자 집계만 담당함.")
    print("=" * 64)


if __name__ == "__main__":
    main()
