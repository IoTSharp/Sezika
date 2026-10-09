#!/bin/bash
set -u
test "$(hostname)" = digisyn-gpu01 || exit 65
timeout 4s journalctl -k --since '2026-10-09 10:06:00 UTC' --until '2026-10-09 10:13:00 UTC' --grep='Xid|fallen off|GSP|GPU reset' --no-pager -n 12
printf 'kernel_query_exit=%s\n' "$?"
timeout 4s nvidia-smi -L
timeout 3s ps -p 2990,2982,3055,3068,2376890 -o pid=,ppid=,lstart=,args=
printf 'read_only_no_reset_no_signals=true\n'
