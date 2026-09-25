import json
from pathlib import Path
import time

task = Path('/mnt/d/source/Sezika/.artifacts/s3-aot-20260926')
started = time.monotonic()
files = list(task.glob('*process.json'))
if len(files) > 20:
    raise RuntimeError('Linux evidence count limit')
live = []
checked = 0
for path in files:
    if time.monotonic() - started > 10:
        raise TimeoutError('Linux identity audit deadline')
    record = json.loads(path.read_text())
    processes = record['processes'] + [record['parent']]
    if len(processes) > 2048:
        raise RuntimeError('Linux identity count limit')
    for previous in processes:
        if time.monotonic() - started > 10:
            raise TimeoutError('Linux identity audit deadline')
        checked += 1
        try:
            raw = (Path('/proc') / str(previous['pid']) / 'stat').read_text()
        except FileNotFoundError:
            continue
        values = raw[raw.rfind(')')+2:].split()
        if int(values[19]) == previous['start_ticks'] and int(values[2]) == previous['group']:
            live.append(previous)
result = dict(files=len(files), recorded_processes_checked=checked, live_matching_processes=live,
              timeout_seconds=10, maximum_files=20, elapsed_seconds=time.monotonic()-started)
(task / 'linux-cleanup-audit.json').write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps(result))
if live:
    raise RuntimeError('Recorded Linux process remains alive')
