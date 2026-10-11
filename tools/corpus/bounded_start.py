"""Cooperative startup for the existing PS7 process identity guardian."""
import os, pathlib, time

def wait_for_guardian():
    value=os.environ.get('SEZIKA_START_GATE')
    if not value: return
    gate=pathlib.Path(value).resolve()
    started=time.monotonic()
    for _ in range(160):
        if time.monotonic()-started>=40: break
        if (gate/'cancel.request').exists(): raise RuntimeError('Startup cancelled')
        if (gate/'go').is_file(): return
        time.sleep(.25)
    raise TimeoutError('Guardian startup gate deadline')
