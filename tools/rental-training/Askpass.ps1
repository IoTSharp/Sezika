if ($PSVersionTable.PSVersion.Major -lt 7) { exit 1 }
if (-not $env:SEZIKA_SSH_PASSWORD) { exit 1 }
[Console]::Write($env:SEZIKA_SSH_PASSWORD)
