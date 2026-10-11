"""One bounded native child; cooperative PS7 root registration before launch."""
import argparse,subprocess
from bounded_start import wait_for_guardian

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--seconds',type=int,required=True);p.add_argument('command',nargs=argparse.REMAINDER);a=p.parse_args()
    if not 1<=a.seconds<=1800 or not 1<=len(a.command)<=128:raise ValueError('Explicit child bounds required')
    wait_for_guardian()
    child=subprocess.Popen(a.command)
    try:result=child.wait(timeout=a.seconds)
    finally:
        if child.poll() is None:
            child.terminate()
            try:child.wait(timeout=5)
            except subprocess.TimeoutExpired:child.kill();child.wait(timeout=5)
    raise SystemExit(result)
