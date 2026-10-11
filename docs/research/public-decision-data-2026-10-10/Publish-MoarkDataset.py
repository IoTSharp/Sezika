"""Task-scoped Git/gai publication with frozen assets and no secret logging."""
import argparse
import ctypes
import hashlib
import hmac
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path
import psutil
import requests
sys.path.insert(0, r"D:\source\Sezika\.artifacts\model-publishing-20261009-01a12099")
from windows_hf_credential import Credential, native, read_credential

parser = argparse.ArgumentParser()
parser.add_argument("stage", choices=["prepare", "extend", "push", "push-proxy", "push-standard", "refs", "lfs-check"])
parser.add_argument("--version", choices=["v1", "v2"], default="v1")
args = parser.parse_args()
root = Path(r"D:\source\Sezika\.artifacts\public-decision-data-20261010")
data_root = Path(r"C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10")
suffix = "" if args.version == "v1" else "-v2"
package = data_root / ("upload/moark-public-decision" + suffix)
receipt_stage = "prepare" if args.stage == "extend" else args.stage
ready_path = root / ("publish-prepare" + suffix + ".json")
repo = data_root / "upload/moark-git-repository"
url = "https://moark.com/maikebing/public-decision-data"
git = r"C:\Program Files\Git\cmd\git.exe"
gai = Path(r"D:\source\Sezika\.artifacts\model-publishing-20261009-01a12099\moark-current\gai.exe")
started = time.monotonic()
budget = 1700 if args.stage.startswith("push") else 300
state = {"stage": args.stage, "pid": os.getpid(), "parent_pid": os.getppid(),
         "created": psutil.Process().create_time(), "max_seconds": budget,
         "max_commands": 40, "commands": [], "secret_output": False, "http": []}
secrets = []
env = {k: v for k, v in os.environ.items() if not k.startswith("GIT_TRACE") and k != "GIT_CURL_VERBOSE"}
env.update(GIT_TERMINAL_PROMPT="0", GIT_LFS_SKIP_SMUDGE="1", PYTHONIOENCODING="utf-8")
env['PATH'] = r'C:\Program Files\Git\bin;C:\Program Files\Git\usr\bin;' + env.get('PATH', '')
if args.stage == "push-proxy":
    env.update(HTTPS_PROXY="http://127.0.0.1:7890", HTTP_PROXY="http://127.0.0.1:7890")
helper = '!"' + sys.executable.replace("\\", "/") + '" "' + str(root / "dataset_git_credential.py").replace("\\", "/") + '"'

def check():
    if time.monotonic() - started >= budget or (data_root / "CANCEL").exists():
        raise TimeoutError("Publication deadline or cancellation")

def safe(text):
    for value in secrets:
        text = text.replace(value, "[REDACTED]")
    return re.sub(r"(https?://[^\s?]+)\?[^\s]+", r"\1?[REDACTED_QUERY]", text)

def sha(path):
    digest = hashlib.sha256()
    length = path.stat().st_size
    with path.open("rb") as stream:
        for _ in range((length + 4 * 1024 * 1024 - 1) // (4 * 1024 * 1024) + 1):
            check()
            block = stream.read(4 * 1024 * 1024)
            if not block:
                return digest.hexdigest()
            digest.update(block)
    raise ValueError("Hash iteration boundary")

def run(command, timeout=35, cwd=repo, proxy=False):
    check()
    if len(state["commands"]) >= 40:
        raise TimeoutError("Git command boundary")
    argv = [git, "-c", "credential.helper=", "-c", "credential.helper=" + helper,
            "-c", "credential.useHttpPath=true", "-c", "core.autocrlf=false",
            "-c", "lfs.concurrenttransfers=1", "-c", "lfs.activitytimeout=90",
            "-c", "lfs.dialtimeout=15", "-c", "lfs.transfer.maxretries=1"]
    if proxy or args.stage == "push-proxy":
        argv += ["-c", "http.proxy=http://127.0.0.1:7890"]
    if args.stage == "push-standard":
        argv += ["-c", "lfs.standalonetransferagent=", "-c", "lfs.basictransfersonly=true"]
    child = subprocess.Popen(argv + command, cwd=cwd, env=env, stdout=subprocess.PIPE,
                             stderr=subprocess.PIPE, text=True, encoding="utf-8", errors="replace")
    proc = psutil.Process(child.pid)
    row = {"command": command, "pid": child.pid, "created": proc.create_time(),
           "argv": proc.cmdline(), "parent_pid": os.getpid()}
    state["commands"].append(row)
    (root / ("publish-" + receipt_stage + suffix + "-running.json")).write_text(json.dumps(state, indent=2), encoding="utf-8")
    try:
        out, err = child.communicate(timeout=min(timeout, max(1, budget - (time.monotonic() - started))))
    except subprocess.TimeoutExpired:
        # Root and descendants are also registered by the external guardian.
        child.kill()
        child.wait(timeout=5)
        row.update(exit_code=child.returncode, status="TIMEOUT")
        raise TimeoutError("Git command deadline")
    row.update(exit_code=child.returncode, stdout=safe(out)[-16000:], stderr=safe(err)[-16000:])
    visible_out = str(len(out.splitlines())) + " LFS paths" if command[:2] == ["lfs", "ls-files"] else row["stdout"]
    print(json.dumps({"command": command, "exit_code": child.returncode,
                      "stdout": visible_out, "stderr": row["stderr"]}, ensure_ascii=True), flush=True)
    if child.returncode != 0:
        raise RuntimeError("Git command failed")
    return out.strip()

def alias(value, key):
    target = "gitee-ai.maikebing.public-decision-data:" + key
    raw = bytearray(value.encode("utf-8"))
    buffer = (ctypes.c_ubyte * len(raw)).from_buffer(raw)
    record = Credential()
    record.Type = 1
    record.TargetName = target
    record.UserName = key
    record.Persist = 2
    record.Comment = "Sezika public dataset upload; native gai keyring"
    record.CredentialBlobSize = len(raw)
    record.CredentialBlob = ctypes.cast(buffer, ctypes.POINTER(ctypes.c_ubyte))
    api = native()
    pointer = ctypes.POINTER(Credential)()
    try:
        if not api.CredWriteW(ctypes.byref(record), 0):
            raise ctypes.WinError(ctypes.get_last_error())
        if not api.CredReadW(target, 1, 0, ctypes.byref(pointer)):
            raise ctypes.WinError(ctypes.get_last_error())
        if not hmac.compare_digest(bytes(raw), ctypes.string_at(pointer.contents.CredentialBlob, pointer.contents.CredentialBlobSize)):
            raise ValueError("Alias readback mismatch")
        state.setdefault("aliases", []).append({"target": target, "encoding": "UTF8", "readback": "PASS"})
    finally:
        ctypes.memset(ctypes.addressof(buffer), 0, len(raw))
        if pointer:
            ctypes.memset(pointer.contents.CredentialBlob, 0, pointer.contents.CredentialBlobSize)
            api.CredFree(pointer)

def lfs_check():
    rows = []
    for line in run(["lfs", "ls-files", "--long"]).splitlines():
        oid, _, path = line.split(" ", 2)
        rows.append({"oid": oid, "size": (repo / path).stat().st_size, "lfs_file_path": path})
    gitee_token = read_credential("git:https://gitee.com")
    moark_token = read_credential("Codex:MoArk:maikebing:Sezika")
    secrets.extend([gitee_token, moark_token])
    body = {"owner": "maikebing", "repo_path": "public-decision-data",
            "gitee_access_token": gitee_token, "lfs_files": rows}
    for mode in ["direct", "proxy"]:
        check()
        try:
            with requests.Session() as session:
                session.trust_env = False
                response = session.post("https://moark.com/api/v1/hub/lfs/missing-lfs",
                    headers={"Authorization": "Bearer " + moark_token}, json=body,
                    timeout=(5, 15), allow_redirects=False,
                    proxies={"https": "http://127.0.0.1:7890"} if mode == "proxy" else {})
            row = {"operation": "missing-lfs", "http_status": response.status_code,
                   "mode": mode, "submitted_objects": len(rows)}
            if response.status_code == 200:
                missing = response.json()
                if not isinstance(missing, list):
                    raise ValueError("Unexpected LFS response shape")
                row["missing_objects"] = len(missing)
                state["lfs_missing"] = len(missing)
            state["http"].append(row)
            print(json.dumps(row), flush=True)
            if response.status_code != 200:
                raise ValueError("LFS initialization rejected")
            return
        except requests.RequestException:
            if mode == "proxy":
                raise RuntimeError("LFS network failure") from None

try:
    if args.stage in ["prepare", "extend"]:
        verified = json.loads((root / "browser-inspect.json").read_text(encoding="utf-8"))
        if verified.get("git_repository", {}).get("full_name") != "maikebing/public-decision-data" or verified["git_repository"].get("public") is not True:
            raise ValueError("Verified public repository identity missing")
        if gai.stat().st_size == 0 or sha(gai) != "a6bdf492a706705631be4b61caa93ba10f63c1df89410e1a16f71162e0353877":
            raise ValueError("Official gai identity mismatch")
        if args.stage == "extend":
            if args.version != "v2" or ready_path.exists():
                raise ValueError("Extension requires a new v2 receipt")
            previous = json.loads((root / "publish-prepare.json").read_text(encoding="utf-8"))
            verification = json.loads((root / "verify.json").read_text(encoding="utf-8"))
            if verification.get("status") != "PASS_anonymous_all_assets_full_content_sha256" or verification.get("revision") != previous["revision"]:
                raise ValueError("Initial release not independently verified")
            if run(["config", "--get", "remote.origin.url"]) != url or run(["rev-parse", "HEAD"]) != previous["revision"]:
                raise ValueError("Extension baseline differs")
            if run(["status", "--porcelain"]):
                raise ValueError("Extension checkout changed")
            if not run(["ls-remote", "origin", "refs/heads/master"], 40).startswith(previous["revision"] + "\t"):
                raise ValueError("Extension remote baseline differs")
            state["previous_verified_revision"] = previous["revision"]
        elif repo.exists():
            previous = json.loads((root / "publish-prepare.json").read_text(encoding="utf-8"))
            if previous.get("status") != "FAIL" or not previous.get("assets_verified") or previous.get("error") != "Git command failed":
                raise ValueError("Existing checkout is not the verified interrupted task copy")
            if run(["config", "--get", "remote.origin.url"]) != url or run(["rev-parse", "HEAD"]) != previous["initial_revision"]:
                raise ValueError("Interrupted task baseline differs")
            if sha(package / "ASSET_MANIFEST.json") != previous["manifest_sha256"]:
                raise ValueError("Interrupted manifest differs")
            state["resumed_owned_checkout"] = True
        else:
            try:
                run(["clone", "--depth", "1", url, str(repo)], 50, cwd=data_root / "upload")
            except RuntimeError:
                if repo.exists():
                    raise ValueError("Partial clone preserved")
                run(["clone", "--depth", "1", url, str(repo)], 50, cwd=data_root / "upload", proxy=True)
        state["initial_revision"] = run(["rev-parse", "HEAD"])
        state["manifest_sha256"] = sha(package / "ASSET_MANIFEST.json")
        assets = json.loads((package / "ASSET_MANIFEST.json").read_text(encoding="utf-8-sig"))
        metadata_files = ["README.md", "LICENSE", "ASSET_MANIFEST.json", "RIGHTS_MANIFEST.json", "MOARK_PUBLICATION.json", ".gitattributes"]
        total = 0
        for row in assets:
            check()
            source, target = package / row["path"], repo / row["path"]
            if not source.resolve().is_relative_to(package.resolve()) or not target.resolve().is_relative_to(repo.resolve()):
                raise ValueError("Asset path outside task roots")
            if source.stat().st_size != row["bytes"] or sha(source) != row["sha256"]:
                raise ValueError("Source asset SHA/size differs")
            target.parent.mkdir(parents=True, exist_ok=True)
            if target.exists():
                if target.stat().st_size != row["bytes"] or sha(target) != row["sha256"]:
                    raise ValueError("Interrupted destination SHA/size differs")
            else:
                os.link(source, target)
            total += row["bytes"]
        for relative in metadata_files:
            check()
            (repo / relative).write_bytes((package / relative).read_bytes())
        # This generated metadata file is owned by this task, separate from raw data.
        (repo / ".gitattributes").write_text((package / ".gitattributes").read_text(encoding="utf-8").replace("\r\n", "\n"), encoding="utf-8", newline="\n")
        state.update(asset_files=len(assets), asset_bytes=total, assets_verified=True)
        for command in [["lfs", "install", "--local"], ["config", "core.autocrlf", "false"],
                        ["config", "user.name", "maikebing"], ["config", "user.email", "mysticboy@live.com"]]:
            run(command)
        run(["add", "--all"], 90)
        run(["diff", "--cached", "--check", "--", ".gitattributes"])
        run(["commit", "-m", "Collect public decision datasets with source licenses and verified assets"], 50)
        state["revision"] = run(["rev-parse", "HEAD"])
        gitee_token = read_credential("git:https://gitee.com")
        moark_token = read_credential("Codex:MoArk:maikebing:Sezika")
        secrets.extend([gitee_token, moark_token])
        alias(gitee_token, "gitee-token")
        alias(moark_token, "gitee-ai-token")
        for command in [["config", "lfs.customtransfer.gai.path", str(gai).replace("\\", "/")],
                        ["config", "lfs.customtransfer.gai.args", "--api-addr https://moark.com/api/v1"],
                        ["config", "lfs.standalonetransferagent", "" if args.version == "v2" else "gai"],
                        ["config", "lfs.customtransfer.gai.concurrent", "false"]]:
            run(command)
        if args.version == "v2":
            run(["config", "lfs.basictransfersonly", "true"])
        lfs_check()
    else:
        ready = json.loads(ready_path.read_text(encoding="utf-8"))
        if ready.get("status") != "PASS" or run(["rev-parse", "HEAD"]) != ready["revision"]:
            raise ValueError("Frozen prepared commit mismatch")
        if run(["config", "--get", "remote.origin.url"]) != url:
            raise ValueError("Remote repository differs")
        state["revision"] = ready["revision"]
        state["manifest_sha256"] = ready["manifest_sha256"]
        if args.stage == "lfs-check":
            lfs_check()
        else:
            state["before_refs"] = run(["ls-remote", "origin", "refs/heads/master"], 40)
            if args.stage.startswith("push") and not state["before_refs"].startswith(state["revision"] + "\t"):
                run(["push", "origin", "master"], 1550)
            state["after_refs"] = run(["ls-remote", "origin", "refs/heads/master"], 40)
            state["remote_commit_matches"] = state["after_refs"].startswith(state["revision"] + "\t")
            if args.stage.startswith("push") and not state["remote_commit_matches"]:
                raise ValueError("Remote commit differs")
            if args.stage.startswith("push"):
                lfs_check()
                if state.get("lfs_missing") != 0:
                    raise ValueError("Remote LFS objects still missing")
    state["status"] = "PASS"
except Exception as exc:
    state.update(status="FAIL", error_type=type(exc).__name__, error=safe(str(exc))[:2000])
finally:
    secrets.clear()
    state["elapsed_seconds"] = round(time.monotonic() - started, 3)
    (root / ("publish-" + receipt_stage + suffix + ".json")).write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({k: state.get(k) for k in ["status", "stage", "revision", "asset_files", "asset_bytes", "lfs_missing", "remote_commit_matches", "error", "elapsed_seconds"]}), flush=True)
raise SystemExit(0 if state["status"] == "PASS" else 1)
