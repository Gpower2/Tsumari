import json,sys,statistics,html
from pathlib import Path
files=sys.argv[1:-1]; out=Path(sys.argv[-1]); rows=[]
for file in files:
    rows.extend(json.loads(line) for line in Path(file).read_text(encoding='utf-8-sig').splitlines() if line.strip())
warmups=[r for r in rows if r['origin']=='warmup']
rows=[r for r in rows if r['origin']!='warmup']
def esc(s): return str(s or '').replace('|','\\|').replace('\r','').replace('\n','<br>')
def fmt(v): return 'unavailable' if v is None else f'{v:.2f}'
lines=['# Local model evaluation evidence','', 'All outputs are model observations, not gold translations. Mechanical checks do not score semantic quality. Warmups excluded. One run per case unless repeat is recorded. GPU memory and utilization are whole-device measurements, including the untouched live bot and other workloads. Endpoint residency reports model allocation separately. Timing includes the HTTP request and generation, not the later residency query.','', '| Model / revision | Task | Requests | Successful | Mechanical pass | Mean seconds | Median seconds | Peak whole GPU MiB | Mean whole GPU utilization % | Endpoint model VRAM GiB |','|---|---|---:|---:|---:|---:|---:|---:|---:|---:|']
for key in sorted({(r['profile'],r['revision'],r['task']) for r in rows}):
    rs=[r for r in rows if (r['profile'],r['revision'],r['task'])==key]; ok=[r for r in rs if not r['error']]; ts=[r['durationMs']/1000 for r in ok]
    mem=[r['gpu']['peakMemoryMiB'] for r in ok if r['gpu']['peakMemoryMiB'] is not None]; util=[r['gpu']['meanUtilizationPercent'] for r in ok if r['gpu']['meanUtilizationPercent'] is not None]
    vm=[m['size_vram']/1024**3 for r in ok for m in (r.get('residency') or {}).get('models',[]) if m['name']==r['effectiveSettings']['Model']]
    mech=sum(all(r['mechanicalChecks'].values()) for r in ok)
    lines.append(f'| {key[0]} / {key[1]} | {key[2]} | {len(rs)} | {len(ok)} | {mech} | {fmt(statistics.mean(ts) if ts else None)} | {fmt(statistics.median(ts) if ts else None)} | {max(mem) if mem else "unavailable"} | {fmt(statistics.mean(util) if util else None)} | {fmt(max(vm) if vm else None)} |')
lines+=['','## Warmup requests','', 'These are first requests in each run, not controlled cold-start measurements. Loading and existing cache state vary.','', '| Model | Revision | Seconds | Error |', '|---|---|---:|---|']
for r in warmups: lines.append(f'| {r["profile"]} | {r["revision"]} | {r["durationMs"]/1000:.2f} | {esc(r["error"])} |')
lines+=['','## Per-request results','', '| Model / revision | Case | Source → target | Seconds | Peak whole GPU MiB | Mean GPU % | Mechanical checks | Output or failure |','|---|---|---|---:|---:|---:|---|---|']
for r in rows:
    checks='; '.join(k+(' ✓' if v else ' FAIL') for k,v in r['mechanicalChecks'].items())
    lines.append(f'| {r["profile"]} / {r["revision"]} | {r["caseId"]} | {r["source"] or "unknown"} → {r["target"] or "analysis"} | {r["durationMs"]/1000:.2f} | {r["gpu"]["peakMemoryMiB"]} | {fmt(r["gpu"]["meanUtilizationPercent"])} | {esc(checks)} | {esc(r["response"] or r["error"])} |')
out.parent.mkdir(parents=True,exist_ok=True);out.write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(out)
