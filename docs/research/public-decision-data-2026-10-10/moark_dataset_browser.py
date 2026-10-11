"""Use the existing dedicated browser profile; never export session secrets."""
import argparse
import json
import os
import time
from pathlib import Path
from urllib.parse import urlsplit
from playwright.sync_api import sync_playwright

parser = argparse.ArgumentParser()
parser.add_argument("--create", action="store_true")
args = parser.parse_args()
root = Path(r"D:\source\Sezika\.artifacts\public-decision-data-20261010")
profile = Path(r"D:\source\Sezika\.artifacts\model-publishing-20261009-01a12099\moark-current\owned-chrome-profile")
started = time.monotonic()
state = {"pid": os.getpid(), "parent_pid": os.getppid(), "max_seconds": 100,
         "max_actions": 18, "secret_output": False, "network": []}

def boundary():
    if time.monotonic() - started >= 100 or (root / "CANCEL").exists():
        raise TimeoutError("Browser deadline or cancellation")

def metadata(response):
    value = response.json()
    if isinstance(value, dict) and isinstance(value.get("data"), dict):
        value = value["data"]
    keys = ["id", "repoId", "repository_id", "name", "title", "login", "username",
            "path", "full_name", "public", "private", "default_branch", "namespace",
            "namespaceInfo", "git_url", "ssh_url", "html_url", "clone_url", "success"]
    return {k: value[k] for k in keys if k in value} if isinstance(value, dict) else {}

def observe(response):
    parsed = urlsplit(response.url)
    if parsed.hostname == "moark.com" and parsed.path.startswith("/api/") and len(state["network"]) < 80:
        state["network"].append({"method": response.request.method, "path": parsed.path, "status": response.status})

try:
    with sync_playwright() as runtime:
        context = runtime.chromium.launch_persistent_context(str(profile),
            executable_path=r"C:\Users\mysti\AppData\Local\Google\Chrome\Application\chrome.exe",
            headless=True, viewport={"width": 1320, "height": 860},
            args=["--no-first-run", "--no-default-browser-check"])
        try:
            page = context.pages[0] if context.pages else context.new_page()
            page.on("response", observe)
            page.goto("https://moark.com/datasets/new?namespace=maikebing", wait_until="domcontentloaded", timeout=25000)
            page.wait_for_timeout(1500)
            boundary()
            response = context.request.get("https://moark.com/api/base/userinfo", timeout=12000)
            state["identity_status"] = response.status
            state["identity"] = metadata(response) if response.status == 200 else {}
            response = context.request.get("https://moark.com/api/base/maikebing/profile", timeout=12000)
            state["profile_status"] = response.status
            state["profile"] = metadata(response) if response.status == 200 else {}
            pre = context.request.get("https://moark.com/api/datasets/maikebing/public-decision-data", timeout=12000)
            state["existence_status"] = pre.status
            if pre.status == 200:
                state["repository"] = metadata(pre)
                repo = context.request.get("https://moark.com/api/gitee/repos/maikebing/public-decision-data", timeout=12000)
                state["git_repository_status"] = repo.status
                state["git_repository"] = metadata(repo) if repo.status == 200 else {}
                state["status"] = "PASS_existing_repository"
            else:
                state["controls"] = page.locator("select").evaluate_all("els=>els.map(e=>({value:e.value,options:Array.from(e.options).map(o=>({value:o.value,text:o.textContent}))}))")
                state["form_text"] = page.locator("main").inner_text(timeout=5000)[:5000]
                state["status"] = "PASS_inspected"
                if args.create:
                    if state["identity_status"] != 200 or state["identity"].get("username") != "maikebing":
                        raise ValueError("Authenticated owner mismatch")
                    if pre.status != 404:
                        raise ValueError("Existence check ambiguous")
                    # Native select backing the displayed owner control has stable values.
                    owner = page.locator("select").filter(has=page.locator('option[value="maikebing"]'))
                    owner.select_option("maikebing", timeout=7000, force=True)
                    selected = page.locator("select").evaluate_all("els=>els.map(e=>({value:e.value,text:e.selectedOptions[0]?.textContent}))")
                    state["selected_owner"] = selected
                    if not any(row["value"] == "maikebing" for row in selected):
                        raise ValueError("Selected owner mismatch")
                    page.get_by_placeholder("请填写新的名称", exact=True).fill("Sezika Public Decision Data", timeout=5000)
                    page.get_by_placeholder("请填写新数据集的 URL 地址", exact=True).fill("public-decision-data", timeout=5000)
                    page.get_by_role("radio", name="公开（所有人可见）", exact=True).click(timeout=5000)
                    boundary()
                    if time.monotonic() - started > 78:
                        raise TimeoutError("Insufficient creation time remaining")
                    # Exactly one form submission; ambiguous responses require a later GET.
                    with page.expect_response(lambda r: r.request.method == "POST" and urlsplit(r.url).path == "/api/datasets", timeout=15000) as pending:
                        page.get_by_role("button", name="新建数据集", exact=True).click(timeout=5000)
                    response = pending.value
                    state["creation_http_status"] = response.status
                    state["creation"] = metadata(response) if response.status == 200 else {}
                    if response.status != 200:
                        raise ValueError("Creation rejected")
                    post = context.request.get("https://moark.com/api/datasets/maikebing/public-decision-data", timeout=10000)
                    state["post_status"] = post.status
                    if post.status != 200:
                        raise ValueError("Independent existence verification failed")
                    state["repository"] = metadata(post)
                    state["status"] = "PASS_created_verified_identity"
        finally:
            context.close()
except Exception as exc:
    state.update(status="FAIL", error_type=type(exc).__name__, error=str(exc)[:500])
finally:
    state["elapsed_seconds"] = round(time.monotonic() - started, 3)
    (root / ("browser-create.json" if args.create else "browser-inspect.json")).write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({k: state.get(k) for k in ["status", "identity_status", "identity", "profile_status", "existence_status", "creation_http_status", "repository", "git_repository_status", "git_repository", "error_type", "error", "elapsed_seconds"]}, ensure_ascii=True), flush=True)
raise SystemExit(0 if state["status"].startswith("PASS") else 1)
