"""Anonymous full-content verification at the frozen publication commit."""
import hashlib
import argparse
import json
import os
import re
import subprocess
import time
from pathlib import Path
from urllib.parse import quote, urlsplit
import psutil
import requests

parser = argparse.ArgumentParser()
parser.add_argument("--resume", action="store_true")
parser.add_argument("--version", choices=["v1", "v2"], default="v1")
args = parser.parse_args()
root = Path(r"D:\source\Sezika\.artifacts\public-decision-data-20261010")
data_root = Path(r"C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10")
expected_root = data_root / "upload/moark-git-repository"
suffix = "" if args.version == "v1" else "-v2"
clone = data_root / ("upload/moark-anonymous-verification" + suffix)
auth_home = root / ("anonymous-client-home" + suffix)
ready = json.loads((root / ("publish-prepare" + suffix + ".json")).read_text(encoding="utf-8"))
revision = ready["revision"]
owner = "maikebing/public-decision-data"
started = time.monotonic()
maximum = 1700
state = {"pid": os.getpid(), "parent_pid": os.getppid(), "max_seconds": maximum,
         "max_git_commands": 10, "max_http_attempts": 2 * len(json.loads((expected_root / "ASSET_MANIFEST.json").read_text(encoding="utf-8-sig"))), "anonymous": True,
         "git": [], "files": [], "downloads": [], "revision": revision}
state["batch_requests"] = []
env = {k: v for k, v in os.environ.items() if not k.startswith("GIT_") and k not in ["SSH_ASKPASS", "SSH_AUTH_SOCK", "CURL_HOME"]}
env.update(GIT_TERMINAL_PROMPT="0", GIT_LFS_SKIP_SMUDGE="1", GIT_CONFIG_NOSYSTEM="1",
           GIT_CONFIG_SYSTEM=os.devnull, GIT_CONFIG_GLOBAL=os.devnull,
           HOME=str(auth_home), USERPROFILE=str(auth_home), CURL_HOME=str(auth_home))

def check():
    if time.monotonic() - started >= maximum or (data_root / "CANCEL").exists():
        raise TimeoutError("Verification deadline or cancellation")

def save():
    state["elapsed_seconds"] = round(time.monotonic() - started, 3)
    (root / ("verify-running" + suffix + ".json")).write_text(json.dumps(state, indent=2), encoding="utf-8")

def run(args, cwd=clone, proxy=False):
    check()
    if len(state["git"]) >= 10:
        raise TimeoutError("Verification Git boundary")
    command = [r"C:\Program Files\Git\cmd\git.exe", "-c", "credential.helper=", "-c", "core.askPass=", "-c", "http.extraHeader=", "-c", "core.autocrlf=false"]
    if proxy:
        command += ["-c", "http.proxy=http://127.0.0.1:7890"]
    child = subprocess.Popen(command + args, cwd=cwd, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    proc = psutil.Process(child.pid)
    row = {"args": args, "pid": child.pid, "created": proc.create_time(), "argv": proc.cmdline(), "parent_pid": proc.ppid()}
    state["git"].append(row)
    save()
    try:
        out, err = child.communicate(timeout=75)
    except subprocess.TimeoutExpired:
        current = psutil.Process(child.pid)
        if current.create_time() != row["created"] or current.ppid() != row["parent_pid"] or current.cmdline() != row["argv"]:
            raise ValueError("Timeout child identity differs")
        child.kill()
        child.wait(timeout=5)
        raise TimeoutError("Git timeout; descendant cleanup handled by guardian")
    row.update(exit_code=child.returncode, stderr=err.decode("utf-8", errors="replace")[-2000:])
    if child.returncode:
        raise RuntimeError("Anonymous Git command failed")
    return out

def batch_download_actions(rows):
    body = {"operation": "download", "transfers": ["basic"], "ref": {"name": revision},
            "objects": [{"oid": row["sha256"], "size": row["bytes"]} for row in rows]}
    for mode in ["direct", "proxy"]:
        check()
        session = requests.Session()
        session.trust_env = False
        try:
            response = session.post("https://moark.com/" + owner + ".git/info/lfs/objects/batch", json=body,
                headers={"Accept": "application/vnd.git-lfs+json", "Content-Type": "application/vnd.git-lfs+json"},
                timeout=(6, 25), proxies={"https": "http://127.0.0.1:7890"} if mode == "proxy" else {})
            state["batch_requests"].append({"mode": mode, "http_status": response.status_code,
                "objects": len(rows), "anonymous": True, "operation": "download"})
            if response.status_code != 200:
                raise ValueError("Anonymous standard LFS batch rejected")
            values = response.json().get("objects", [])
            expected = {row["sha256"]: row["bytes"] for row in rows}
            actions = {}
            for value in values:
                check()
                oid = value.get("oid")
                if oid not in expected or value.get("size") != expected[oid] or value.get("error"):
                    raise ValueError("Anonymous LFS batch object differs or rejected")
                action = value.get("actions", {}).get("download", {})
                if urlsplit(action.get("href", "")).scheme != "https":
                    raise ValueError("Expected secure anonymous download action")
                actions[oid] = action
            if set(actions) != set(expected):
                raise ValueError("Anonymous LFS batch object set differs")
            return actions
        except requests.RequestException:
            if mode == "proxy":
                raise RuntimeError("Anonymous LFS batch network failure") from None
        finally:
            session.close()
    raise RuntimeError("No anonymous LFS actions")

def download(path, digest, size, action):
    for mode in ["direct", "proxy"]:
        check()
        if len(state["downloads"]) >= state["max_http_attempts"]:
            raise TimeoutError("Verification HTTP boundary")
        row = {"path": path, "mode": mode, "expected_sha256": digest, "expected_bytes": size, "anonymous": True}
        state["downloads"].append(row)
        session = requests.Session()
        session.trust_env = False
        session.max_redirects = 3
        try:
            url = action["href"]
            with session.get(url, headers=action.get("header", {}), stream=True, timeout=(6, 35), allow_redirects=True,
                proxies={"https": "http://127.0.0.1:7890"} if mode == "proxy" else {}) as response:
                row.update(http_status=response.status_code, final_host=urlsplit(response.url).hostname)
                if response.status_code != 200:
                    raise ValueError("Anonymous object download rejected")
                actual = hashlib.sha256()
                count = 0
                object_started = time.monotonic()
                # iter_content can deliver short reads; a byte-derived iteration
                # bound avoids an artificial file-size or chunk-count limit.
                for index, block in enumerate(response.iter_content(4 * 1024 * 1024)):
                    check()
                    if index >= size + 1 or time.monotonic() - object_started >= 240:
                        raise TimeoutError("Object verification bound")
                    count += len(block)
                    if count > size:
                        raise ValueError("Downloaded object exceeds expected size")
                    actual.update(block)
                    if index % 16 == 0:
                        print(json.dumps({"progress": "anonymous_sha256", "path": path, "bytes": count, "expected_bytes": size}), flush=True)
                row.update(bytes=count, sha256=actual.hexdigest())
                if count != size or actual.hexdigest() != digest:
                    raise ValueError("Anonymous full-content SHA/size mismatch")
                row["status"] = "PASS_full_content"
                return
        except requests.RequestException as exc:
            row.update(status="NETWORK_FAIL", error_type=type(exc).__name__)
            if mode == "proxy":
                raise RuntimeError("Both object download transports failed") from None
        finally:
            session.close()
            save()

try:
    if ready.get("status") != "PASS":
        raise ValueError("No frozen prepared release")
    if args.resume:
        if not clone.is_dir() or not auth_home.is_dir():
            raise ValueError("No prior verification assets to resume")
        if run(["config", "--get", "remote.origin.url"]).decode().strip() != "https://moark.com/" + owner:
            raise ValueError("Prior anonymous remote differs")
        if run(["status", "--porcelain"]).strip():
            raise ValueError("Prior anonymous checkout changed")
        state["resumed_verified_anonymous_clone"] = True
    else:
        if clone.exists() or auth_home.exists():
            raise ValueError("Prior verification assets preserved; inspect before resuming")
        auth_home.mkdir()
        try:
            run(["clone", "--depth", "1", "https://moark.com/" + owner, str(clone)], cwd=data_root / "upload")
        except RuntimeError:
            if clone.exists():
                raise ValueError("Partial anonymous checkout preserved")
            run(["clone", "--depth", "1", "https://moark.com/" + owner, str(clone)], cwd=data_root / "upload", proxy=True)
    actual_revision = run(["rev-parse", "HEAD"]).decode().strip()
    if actual_revision != revision:
        raise ValueError("Public commit mismatch")
    manifest_bytes = (clone / "ASSET_MANIFEST.json").read_bytes()
    if hashlib.sha256(manifest_bytes).hexdigest() != ready["manifest_sha256"]:
        raise ValueError("Public manifest SHA mismatch")
    assets = json.loads(manifest_bytes.decode("utf-8-sig"))
    paths = set(run(["ls-tree", "-r", "--name-only", "HEAD"]).decode().splitlines())
    expected_paths = {row["path"] for row in assets} | {"README.md", "LICENSE", "ASSET_MANIFEST.json", "RIGHTS_MANIFEST.json", "MOARK_PUBLICATION.json", ".gitattributes"}
    if paths != expected_paths:
        state["path_set_difference"] = {"missing": sorted(expected_paths-paths), "extra": sorted(paths-expected_paths)}
        raise ValueError("Public path set mismatch")
    lfs = []
    for row in assets:
        check()
        path = row["path"]
        raw = (clone / path).read_bytes()
        pointer = b"version https://git-lfs.github.com/spec/v1\n"
        if raw.startswith(pointer):
            expected = pointer + ("oid sha256:" + row["sha256"] + "\nsize " + str(row["bytes"]) + "\n").encode("ascii")
            if raw != expected:
                raise ValueError("Public LFS pointer differs")
            lfs.append(row)
            state["files"].append({"path": path, "bytes": row["bytes"], "sha256": row["sha256"], "git_pointer": "PASS", "full_content": "PENDING"})
        else:
            if len(raw) != row["bytes"] or hashlib.sha256(raw).hexdigest() != row["sha256"]:
                raise ValueError("Public small asset differs")
            state["files"].append({"path": path, "bytes": row["bytes"], "sha256": row["sha256"], "full_content": "PASS"})
    for path in expected_paths - {row["path"] for row in assets}:
        check()
        if (clone / path).read_bytes() != (expected_root / path).read_bytes():
            raise ValueError("Public collection metadata differs")
    state.update(asset_files=len(assets), asset_bytes=sum(row["bytes"] for row in assets), public_path_set="PASS", lfs_objects=len(lfs), manifest_sha256=ready["manifest_sha256"])
    save()
    actions = batch_download_actions(lfs)
    for row in lfs:
        download(row["path"], row["sha256"], row["bytes"], actions[row["sha256"]])
        for item in state["files"]:
            if item["path"] == row["path"]:
                item["full_content"] = "PASS"
    state["status"] = "PASS_anonymous_all_assets_full_content_sha256"
except Exception as exc:
    state.update(status="FAIL_or_PARTIAL", error_type=type(exc).__name__, error=str(exc)[:1000])
finally:
    state["elapsed_seconds"] = round(time.monotonic() - started, 3)
    (root / ("verify" + suffix + ".json")).write_text(json.dumps(state, indent=2), encoding="utf-8")
print(json.dumps({k: state.get(k) for k in ["status", "revision", "asset_files", "asset_bytes", "lfs_objects", "elapsed_seconds", "error"]}), flush=True)
raise SystemExit(0 if state["status"].startswith("PASS") else 1)
