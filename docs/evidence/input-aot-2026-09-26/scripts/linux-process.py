"""Task-local Linux process-group supervisor; never runs model code in Python."""
import datetime
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time

seconds, evidence, *command = sys.argv[1:]
seconds = int(seconds)
if not 1 <= seconds <= 1800 or not command or len(command) > 128:
    raise ValueError("bounded timeout and command required")
started = time.monotonic()
cancelled = False
tracked = {}

def cancel(signum, frame):
    global cancelled
    cancelled = True

signal.signal(signal.SIGTERM, cancel)
signal.signal(signal.SIGINT, cancel)

def identity(pid):
    try:
        base = Path('/proc') / str(pid)
        raw = (base / 'stat').read_text()
        fields = raw[raw.rfind(')') + 2:].split()
        return dict(pid=int(pid), ppid=int(fields[1]), group=int(fields[2]),
                    start_ticks=int(fields[19]), command=(base / 'cmdline').read_bytes().replace(b'\0', b' ').decode(errors='replace'),
                    observed_utc=datetime.datetime.now(datetime.timezone.utc).isoformat())
    except (OSError, ValueError, IndexError):
        return None

parent = identity(os.getpid())
process = subprocess.Popen(command, start_new_session=True)
root_identity = identity(process.pid)
if root_identity:
    tracked[(root_identity['pid'], root_identity['start_ticks'])] = root_identity
print(json.dumps(dict(event='started', parent=parent, root=root_identity, timeout_seconds=seconds)), flush=True)

def scan():
    scan_start = time.monotonic()
    count = 0
    with os.scandir('/proc') as entries:
        for entry in entries:
            count += 1
            if count > 16384 or time.monotonic() - scan_start > 3:
                raise RuntimeError('process scan limit')
            if not entry.name.isdecimal():
                continue
            item = identity(entry.name)
            if item and item['group'] == process.pid:
                tracked.setdefault((item['pid'], item['start_ticks']), item)

exit_code = None
cleanup = []
try:
    for iteration in range(seconds * 2 + 1):
        if iteration % 6 == 0:
            scan()
        if process.poll() is not None:
            exit_code = process.returncode
            break
        if cancelled or time.monotonic() - started >= seconds:
            break
        if iteration % 60 == 0:
            print(f'linux PID {process.pid}: {time.monotonic()-started:.1f}s/{seconds}s', flush=True)
        time.sleep(.5)
finally:
    scan()
    # The session leader remains unreaped while live. Only this newly created
    # process group is signalled; no executable-name matching or user processes.
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        pass
    for attempt in range(20):
        live = [item for item in tracked.values() if (now := identity(item['pid'])) and now['start_ticks'] == item['start_ticks']]
        if not live:
            break
        time.sleep(.1)
    if live:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        cleanup = [item['pid'] for item in live]
    process.wait(timeout=5)
    result = dict(parent=parent, root=root_identity, command=command, timeout_seconds=seconds,
                  elapsed_seconds=time.monotonic()-started, exit_code=exit_code, cancelled=cancelled,
                  completed=exit_code == 0, forced_cleanup_pids=cleanup, processes=list(tracked.values()))
    Path(evidence).write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result), flush=True)
sys.exit(exit_code if exit_code is not None else 124)
