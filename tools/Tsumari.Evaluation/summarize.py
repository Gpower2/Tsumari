import json, sys, statistics
from pathlib import Path
rows=[]
for arg in sys.argv[1:]:
    for line in Path(arg).read_text(encoding='utf-8-sig').splitlines():
        if line.strip():
            row=json.loads(line)
            row['evidence_file']=arg
            rows.append(row)
for (profile,revision,task) in sorted({(r['profile'],r['revision'],r['task']) for r in rows}):
    rs=[r for r in rows if (r['profile'],r['revision'],r['task'])==(profile,revision,task) and r['origin']!='warmup']
    ok=[r for r in rs if not r['error']]
    times=[r['durationMs']/1000 for r in ok]
    gpumem=[r['gpu']['peakMemoryMiB'] for r in ok if r['gpu']['peakMemoryMiB'] is not None]
    gpuutil=[r['gpu']['meanUtilizationPercent'] for r in ok if r['gpu']['meanUtilizationPercent'] is not None]
    residency=[m.get('size_vram',0)/1024**3 for r in ok for m in (r.get('residency') or {}).get('models',[]) if m.get('name')==r['effectiveSettings']['Model']]
    mechanical=sum(bool(r.get('mechanicalChecks')) and all(r['mechanicalChecks'].values()) for r in ok)
    print(json.dumps(dict(profile=profile,revision=revision,task=task,total=len(rs),success=len(ok),fail=len(rs)-len(ok),mechanicalPass=mechanical,meanSeconds=statistics.mean(times) if times else None,medianSeconds=statistics.median(times) if times else None,p95Seconds=sorted(times)[max(0,int(len(times)*.95)-1)] if times else None,peakWholeGpuMiB=max(gpumem) if gpumem else None,meanWholeGpuUtilization=statistics.mean(gpuutil) if gpuutil else None,maxEndpointModelVramGiB=max(residency) if residency else None)))
