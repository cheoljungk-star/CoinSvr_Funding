import json
cutoff='2026-07-14T16:00:06Z'
recs=[]
with open('CoinSvr/bin/Debug/net9.0/trade_results.jsonl', encoding='utf-8') as f:
    for line in f:
        line=line.strip()
        if not line: continue
        try:
            r=json.loads(line)
        except Exception as e:
            print('ERR', e); continue
        recs.append(r)
print('total', len(recs))
new = [r for r in recs if r['Timestamp'] > cutoff]
print('new since cutoff', len(new))
for r in new:
    print(r['Timestamp'], r['Symbol'], r.get('IsBait'), r.get('C_ProfitPct'), r.get('A_ProfitPct_Est'), r.get('B_ProfitPct_Est'), r.get('Actual_ProfitPct'), r.get('C_PeakProfitPct'), r.get('Trend30Pct'))
