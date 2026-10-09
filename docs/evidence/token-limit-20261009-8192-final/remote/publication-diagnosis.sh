#!/bin/bash
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
timeout 2s cat "$task/.artifacts/stage-capacity/payload.stderr.log"
timeout 2s stat -f -c '%T' "$task/output/capacity"
timeout 2s stat -c 'inode=%i dev=%d birth=%w changed=%z modified=%y size=%s name=%n' "$task/output/capacity/"*
timeout 2s sed -n '95,170p' /usr/include/linux/stat.h
timeout 2s awk '/AT_EMPTY_PATH|AT_SYMLINK_NOFOLLOW/ {print}' /usr/include/x86_64-linux-gnu/bits/fcntl-linux.h
