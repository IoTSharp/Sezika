"""Exports offline inference captures for development-time comparison.

No network, installation, training, model download, C# runtime integration or fallback.
Run through Invoke-LayaOracle.ps1: Python cancellation cannot interrupt every native call.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import importlib.metadata
import json
import math
import os
from pathlib import Path
import platform
import shutil
import signal
import subprocess
import sys
import time
from datetime import datetime, timezone
from typing import Any, Iterable


class Cancelled(Exception):
    pass


class AdapterError(Exception):
    """An exporter defect or resource limit, distinct from a model execution failure."""


class Budget:
    def __init__(self, seconds: int, cancel_file: Path | None):
        self.started = time.monotonic()
        self.seconds = seconds
        self.cancel_file = cancel_file
        self.cancelled = False
        self.next_progress = 0.0

    def check(self, phase: str) -> None:
        if self.cancelled or (self.cancel_file and self.cancel_file.exists()):
            raise Cancelled("Oracle export cancelled")
        elapsed = time.monotonic() - self.started
        if elapsed >= self.seconds:
            raise TimeoutError(f"Oracle wall-clock budget {self.seconds}s exceeded")
        if elapsed >= self.next_progress:
            print(f"[{elapsed:.1f}s] {phase}", file=sys.stderr, flush=True)
            self.next_progress = elapsed + 15.0

    def items(self, values: Any, maximum: int, phase: str) -> Iterable[Any]:
        # All inputs here are finite sized collections; no unbounded iterators.
        if len(values) > maximum:
            raise ValueError(f"{phase}: item limit {maximum} exceeded")
        for value in values:
            self.check(phase)
            yield value


def now() -> str:
    return datetime.now(timezone.utc).isoformat()


def read_json(path: Path, maximum: int = 1_048_576, budget: Budget | None = None) -> Any:
    if not path.is_file() or path.stat().st_size > maximum:
        raise ValueError(f"Missing or oversized JSON: {path}")
    def reject_constant(value: str) -> None:
        raise ValueError(f"Non-finite JSON number: {value}")
    def finite_float(value: str) -> float:
        parsed = float(value)
        if not math.isfinite(parsed):
            raise ValueError(f"JSON float overflow: {value}")
        return parsed
    def unique_properties(pairs: list[tuple[str, Any]]) -> dict:
        if len(pairs) > 65_536:
            raise ValueError("JSON object exceeds property limit")
        started = time.monotonic()
        result = {}
        for key, value in pairs:
            if budget is not None:
                budget.check("parse JSON properties")
            if time.monotonic() - started >= 5:
                raise TimeoutError("JSON property parsing exceeds five seconds")
            if key in result:
                raise ValueError(f"Duplicate JSON property: {key}")
            result[key] = value
        return result
    return json.loads(path.read_text(encoding="utf-8"), parse_constant=reject_constant, parse_float=finite_float,
                      object_pairs_hook=unique_properties)


def digest(path: Path, budget: Budget) -> str:
    size = path.stat().st_size
    if size > 2_147_483_648:
        raise ValueError(f"Asset exceeds 2 GiB limit: {path}")
    result = hashlib.sha256()
    with path.open("rb") as source:
        # At most 512 chunks plus the terminating read; exactly bounded for <= 2 GiB.
        for _ in budget.items(range(513), 513, f"hash {path.name}"):
            chunk = source.read(4_194_304)
            if not chunk:
                return result.hexdigest()
            result.update(chunk)
    raise ValueError("Hash chunk limit exceeded")


def write_json(path: Path, value: Any) -> None:
    data = json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n"
    if len(data.encode("utf-8")) > 32 * 1_048_576:
        raise ValueError("Capture exceeds 32 MiB output limit")
    # Only called inside the newly created task-owned output directory.
    temporary = path.with_suffix(path.suffix + ".writing")
    temporary.write_text(data, encoding="utf-8")
    temporary.replace(path)


def git_query(source: Path, args: list[str], budget: Budget, children: list[dict]) -> str:
    git = shutil.which("git")
    if git is None:
        raise ValueError("git must be available on PATH to verify the pinned local checkout")
    command = [git, "-C", str(source), *args]
    budget.check("verify upstream checkout")
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    identity = {"pid": process.pid, "parent_pid": os.getpid(), "started_utc": now(),
                "arguments": command, "role": "read_only_git_identity_check"}
    children.append(identity)
    try:
        output, error = process.communicate(timeout=min(10, max(0.1, budget.seconds - (time.monotonic() - budget.started))))
    except BaseException:
        # This handle owns this exact short-lived git process; never kill by name.
        process.kill()
        process.communicate(timeout=2)
        raise
    finally:
        identity["exit_code"] = process.returncode
        identity["ended_utc"] = now()
    if process.returncode != 0 or len(output) > 65_536 or len(error) > 65_536:
        raise ValueError("Upstream checkout verification failed or exceeded output limit")
    return output.decode("utf-8").strip()


def prepare(source: Path, model: Path, output: Path, lock: dict, budget: Budget, children: list[dict]) -> dict:
    if platform.python_version() != lock["python_version"]:
        raise ValueError(f"Expected CPython {lock['python_version']}; found {platform.python_version()}")
    if platform.python_implementation() != "CPython":
        raise ValueError("Only the pinned CPython interpreter is supported")
    if git_query(source, ["rev-parse", "HEAD"], budget, children) != lock["upstream_source_revision"]:
        raise ValueError("Upstream source commit differs from source-lock.json")
    if git_query(source, ["status", "--porcelain", "--untracked-files=all"], budget, children):
        raise ValueError("Upstream checkout must be clean, including untracked source files")
    pyproject = (source / "pyproject.toml").read_text(encoding="utf-8")
    if f'version = "{lock["upstream_version"]}"' not in pyproject:
        raise ValueError("Upstream project version does not match the lock")
    installed = {}
    for name, expected in budget.items(list(lock["dependencies"].items()), 16, "verify dependencies"):
        actual = importlib.metadata.version(name)
        if actual.split("+", 1)[0] != expected:
            raise ValueError(f"Dependency {name}: expected {expected}, found {actual}")
        installed[name] = actual
    stage = output / "model-workspace"
    stage.mkdir()
    for relative, expected in budget.items(list(lock["files"].items()), 16, "verify and stage assets"):
        original = (model / relative).resolve(strict=True)
        if not original.is_relative_to(model):
            raise ValueError("Model asset resolves outside the supplied model directory")
        if digest(original, budget) != expected:
            raise ValueError(f"Asset SHA-256 mismatch: {relative}")
        destination = stage / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        # Configuration updates must affect only the working copy. The two large
        # immutable files may share storage; config files never share an inode.
        if relative in ("model.safetensors", "tokenizer/tokenizer.json"):
            try:
                os.link(original, destination)
            except OSError:
                shutil.copyfile(original, destination)
        else:
            shutil.copyfile(original, destination)
        budget.check(f"staged {relative}")
    return {"direct_dependencies": installed, "model_workspace": str(stage)}


def make_question(spec: dict, manifest: dict, budget: Budget) -> dict:
    if "question" in spec:
        return copy.deepcopy(spec["question"])
    if "question_ref" in spec:
        return copy.deepcopy(manifest["question_templates"][spec["question_ref"]])
    recipe = spec["question_recipe"]
    count = recipe["count"]
    repetitions = recipe.get("description_repeat", 1)
    instruction_repetitions = recipe.get("instruction_repeat", 1)
    if not 0 <= count <= 64 or not 1 <= repetitions <= 512 or not 1 <= instruction_repetitions <= 512:
        raise ValueError("Question recipe exceeds bounded counts")
    values = [" evidence" * repetitions for _ in budget.items(range(count), 64, "materialize criteria")]
    criteria = {f"label_{i:02d}": values[i] for i in budget.items(range(count), 64, "materialize labels")} if recipe["type"] == "choice" else values
    return {"type": recipe["type"], "instructions": " Evaluate the record." * instruction_repetitions,
            "criteria": criteria}


def make_state(spec: dict, manifest: dict, question: dict, agent: Any, common: Any, budget: Budget) -> Any:
    state = copy.deepcopy(manifest["state_templates"][spec["state_ref"]] if "state_ref" in spec else spec["state"])
    if not isinstance(state, dict) or "recipe" not in state:
        return state
    if state["recipe"] == "conversation_repeat":
        count = state["count"]
        if not 1 <= count <= 512:
            raise ValueError("Conversation recipe exceeds 512 turns")
        turns = [{"role": "user", "content": state["old"]} for _ in budget.items(range(count), 512, "materialize conversation")]
        turns.append({"role": "user", "content": state["latest"]})
        return turns
    if state["recipe"] != "target_total_tokens":
        raise ValueError("Unknown state recipe")
    target = state["target"]
    if not 1 <= target <= 2048:
        raise ValueError("Target untruncated sequence length must be 1..2048")
    internal = agent._to_internal(question)
    lower, upper = 0, 4096
    # The interval strictly shrinks: lower=middle+1 or upper=middle-1. At most
    # 13 probes for 4097 counts, with a separate 32-probe/time/cancellation cap.
    # Token count need not be mathematically monotone for arbitrary text: require an
    # exact observed match and fail materialization if this fixed seed cannot hit it.
    for probe in budget.items(range(32), 32, "materialize exact token boundary"):
        if lower > upper:
            break
        middle = (lower + upper) // 2
        candidate = state["prefix"] + state["repeat_text"] * middle + state["suffix"]
        if len(candidate) > 131_072:
            raise ValueError("Generated state exceeds character limit")
        ids, _ = common.build_sequence(agent.tok, candidate, internal, 16_384, 256)
        if len(ids) == target:
            print(f"Boundary {spec['id']}: exact {target} tokens after {probe + 1} probes", file=sys.stderr, flush=True)
            return candidate
        if len(ids) < target:
            lower = middle + 1
        else:
            upper = middle - 1
    raise ValueError(f"No exact upstream-tokenized state for target {target}; no approximate boundary substituted")


def labels_for(question: dict, budget: Budget) -> list[str]:
    if question["t"] == "choice":
        return [str(label) for label in budget.items(list(question["crit"]), 64, "candidate labels")]
    if question["t"] == "score":
        return [str(index) for index in budget.items(range(len(question["crit"])), 64, "score labels")]
    return ["false", "true"]


def sequence_details(agent: Any, common: Any, internal: dict, state: Any, item: dict, budget: Budget) -> dict:
    serialized = common.serialize_state(state)
    sanitized = serialized.replace(agent.tok.mask_token, " ")
    state_ids = common.encode_text(agent.tok, sanitized, add_special_tokens=False)["input_ids"]
    if len(state_ids) > 16_384:
        raise ValueError("State exceeds 16384-token oracle safety limit")
    empty_ids, empty_markers = common.build_sequence(agent.tok, "", internal, 16_384, 256)
    room = max(0, 1024 - len(empty_ids))
    retained = min(len(state_ids), room)
    head_text = f"{internal['t']} question: {str(internal['ins']).replace(agent.tok.mask_token, ' ')}"
    instruction_ids = common.encode_text(agent.tok, head_text, add_special_tokens=False)["input_ids"]
    options = common.render_options(internal)
    original_options = []
    for option in budget.items(options, 64, "inspect rendered options"):
        text = " " + option.replace(agent.tok.mask_token, " ")
        original_options.append(len(common.encode_text(agent.tok, text, add_special_tokens=False)["input_ids"]))
    kept_options = []
    for index in budget.items(range(len(empty_markers)), 64, "inspect option spans"):
        end = empty_markers[index + 1] if index + 1 < len(empty_markers) else len(empty_ids) - 2
        kept_options.append(end - empty_markers[index] - 1)
    kept_instructions = empty_markers[0] - 2 if empty_markers else 0
    return {
        "serialized_state": serialized, "sanitized_state": sanitized,
        "rendered_instruction": head_text, "rendered_options": options,
        "decoded_sequence": agent.tok.decode(item["ids"], skip_special_tokens=False, clean_up_tokenization_spaces=False),
        "original_instruction_tokens": len(instruction_ids), "retained_instruction_tokens": kept_instructions,
        "original_option_tokens": original_options, "retained_option_tokens": kept_options,
        "prefix_tokens_including_specials": len(empty_ids) - 1,
        "untruncated_total_tokens": len(empty_ids) + len(state_ids),
        "original_state_tokens": len(state_ids), "retained_state_tokens": retained,
        "dropped_state_tokens": len(state_ids) - retained,
        "state_retained_start": len(state_ids) - retained if isinstance(state, list) else 0,
        "state_truncation_direction": "left" if isinstance(state, list) else "right",
        "mask_text_removed": serialized != sanitized,
    }


def export_case(spec: dict, manifest: dict, agent: Any, common: Any, torch: Any, np: Any, budget: Budget) -> dict:
    # Input materialization failures abort the export before model evaluation.
    question = make_question(spec, manifest, budget)
    criteria = question.get("criteria") if isinstance(question, dict) else None
    if isinstance(criteria, (dict, list)) and len(criteria) > 64:
        raise AdapterError("Fixture exceeds the 64-candidate oracle safety limit")
    state = make_state(spec, manifest, question, agent, common, budget)
    if len(common.serialize_state(state)) > 131_072:
        raise ValueError("Input state exceeds character limit")
    record = {
        "id": spec["id"], "primitive": spec["primitive"], "status": "failed",
        "input": {"state": state, "question": question, "language": spec["language"], "max_len": 1024, "head_max_len": 256},
        "tags": spec.get("tags", []), "token_ids": None, "marker_positions": None,
        "candidate_labels": None, "raw_logits": None, "probabilities": None,
        "prediction": None, "failure": None,
    }
    stage = "validate"
    try:
        budget.check(f"validate {spec['id']}")
        agent._check_question("q", question)
        internal = agent._to_internal(question)
        expected_primitive = {"choice": "choice", "score": "score", "noul": "boolean"}[internal["t"]]
        if expected_primitive != spec["primitive"]:
            raise AdapterError("Fixture primitive does not match its question")
        stage = "encode"
        items = agent._encode_state(state, ["q"], {"q": internal})
        item = items[0]
        record["token_ids"] = item["ids"]
        record["marker_positions"] = item["markers"]
        try:
            record["candidate_labels"] = labels_for(internal, budget)
            record["sequence_diagnostics"] = sequence_details(agent, common, internal, state, item, budget)
            batch = common.collate_items([items], agent.tok.pad_token_id or 0)
        except (Cancelled, TimeoutError, KeyboardInterrupt):
            raise
        except Exception as error:
            raise AdapterError(f"Oracle diagnostics/collation failed: {error}") from error
        stage = "infer"
        budget.check(f"forward {spec['id']}")
        with torch.no_grad():
            logits, act = agent._forward(batch)
        budget.check(f"decode {spec['id']}")
        if not np.isfinite(logits).all() or not np.isfinite(act).all():
            raise FloatingPointError("Upstream returned NaN or Infinity")
        stage = "decode"
        upstream = agent._decode_answers(logits, act, items, ["q"], {"q": internal}, 0, lang=spec["language"])["q"]
        k = len(item["markers"])
        raw = logits[0, :k]
        # Preserve unrounded softmax probabilities. The checkpoint temperature
        # is verified to be 1 with no bucket or language overrides.
        z = raw / 1.0
        probabilities = np.exp(z - z.max())
        probabilities = probabilities / probabilities.sum()
        if not np.isfinite(probabilities).all():
            raise FloatingPointError("Upstream softmax returned NaN or Infinity")
        prediction = {"choice_label": None, "score": None, "boolean": None, "probability_true": None}
        if internal["t"] == "choice":
            prediction["choice_label"] = record["candidate_labels"][int(probabilities.argmax())]
        elif internal["t"] == "score":
            prediction["score"] = float((np.arange(k) * probabilities).sum())
        else:
            prediction["probability_true"] = float(probabilities[1])
            prediction["boolean"] = bool(probabilities[1] >= 0.5)
        record.update(status="answered", raw_logits=raw.tolist(), probabilities=probabilities.tolist(),
                      prediction=prediction, upstream_answer=upstream, act_probabilities=act[0].tolist())
    except (AdapterError, Cancelled, TimeoutError, KeyboardInterrupt):
        raise
    except Exception as error:
        category = "non_finite_output" if isinstance(error, FloatingPointError) else (
            "resource_exhausted" if isinstance(error, (MemoryError, torch.OutOfMemoryError)) else
            "invalid_question" if stage == "validate" else
            "sequence_budget_exceeded" if stage == "encode" and isinstance(error, ValueError) else "inference_failed")
        record["failure"] = {"stage": stage, "type": category, "message": str(error),
                             "upstream_type": type(error).__name__, "upstream_message": str(error)}
    return record


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--upstream", type=Path, required=True)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--cases", type=Path, required=True)
    parser.add_argument("--contract", type=Path, required=True)
    parser.add_argument("--max-cases", type=int, default=1)
    parser.add_argument("--timeout-seconds", type=int, default=180)
    parser.add_argument("--cancel-file", type=Path)
    args = parser.parse_args()
    if not 1 <= args.max_cases <= 64 or not 1 <= args.timeout_seconds <= 1800:
        parser.error("max-cases must be 1..64 and timeout-seconds must be 1..1800")
    budget = Budget(args.timeout_seconds, args.cancel_file)
    def cancel(_signum: int, _frame: Any) -> None:
        budget.cancelled = True
    signal.signal(signal.SIGINT, cancel)
    signal.signal(signal.SIGTERM, cancel)
    source = args.upstream.resolve(strict=True)
    model = args.model.resolve(strict=True)
    output = args.output.resolve()
    if not source.is_dir() or not model.is_dir():
        raise ValueError("Upstream and model must be existing local directories")
    if output.exists() or output.is_relative_to(source) or output.is_relative_to(model):
        raise ValueError("Output must be a new directory outside source/model assets")
    output.mkdir(parents=False)
    run = {"status": "running", "started_utc": now(), "pid": os.getpid(), "parent_pid": os.getppid(),
           "arguments": sys.argv, "timeout_seconds": args.timeout_seconds, "max_cases": args.max_cases,
           "child_processes": [], "source": str(source), "model": str(model), "output": str(output)}
    write_json(output / "run.json", run)
    capture: dict | None = None
    try:
        # Per-process settings only. Dependencies cannot fetch a model or telemetry.
        os.environ.update(HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1", HF_HUB_DISABLE_TELEMETRY="1",
                          TOKENIZERS_PARALLELISM="false", LAYA_CPU_AMP="", TORCH_COMPILE_DISABLE="1")
        lock_path = Path(__file__).with_name("source-lock.json")
        lock = read_json(lock_path, budget=budget)
        manifest = read_json(args.cases, budget=budget)
        contract = read_json(args.contract, budget=budget)
        if manifest["schema_version"] != "sezika.laya-oracle-inputs.v1" or contract["schema_version"] != "sezika.laya-oracle-contract.v1":
            raise ValueError("Unsupported input/contract schema")
        specs = manifest["cases"]
        if not 1 <= len(specs) <= 64:
            raise ValueError("Input manifest must contain 1..64 cases")
        seen = set()
        for spec in budget.items(specs, 64, "verify case identities"):
            if not isinstance(spec["id"], str) or spec["id"] in seen:
                raise ValueError("Case IDs must be unique strings")
            seen.add(spec["id"])
        prepared = prepare(source, model, output, lock, budget, run["child_processes"])
        stage = Path(prepared["model_workspace"])
        config = read_json(stage / "rl_agent_config.json", budget=budget)
        if config.get("max_len") != 1024 or config.get("head_max_len") != 256:
            raise ValueError("Checkpoint length policy differs from the frozen contract")
        if config.get("temperature") != [1, 1, 1] or config.get("temperature_by_options", {}):
            raise ValueError("Checkpoint temperature differs from fixed-1 policy")
        sys.path.insert(0, str(source))
        budget.check("import pinned offline dependencies")
        import torch
        import numpy as np
        from laya.agent import Agent
        from laya import common
        agent_source = Path(sys.modules[Agent.__module__].__file__).resolve(strict=True)
        common_source = Path(common.__file__).resolve(strict=True)
        if agent_source != (source / "laya/agent.py").resolve(strict=True) or common_source != (source / "laya/common.py").resolve(strict=True):
            raise AdapterError("Imported Laya modules do not belong to the pinned checkout")
        torch.set_num_threads(1)
        torch.set_num_interop_threads(1)
        torch.set_default_dtype(torch.float32)
        torch.manual_seed(0)
        torch.use_deterministic_algorithms(True)
        dependency_inventory = {}
        # Explicitly bounded environment inventory; fail instead of silently dropping entries.
        for index, distribution in enumerate(importlib.metadata.distributions()):
            budget.check("record dependency inventory")
            if index >= 256:
                raise ValueError("Use an isolated environment with at most 256 distributions")
            dependency_inventory[distribution.metadata["Name"]] = distribution.version
        provenance = {
            "model_id": lock["model_id"], "model_revision": lock["model_revision"],
            "weights_sha256": lock["files"]["model.safetensors"],
            "tokenizer_sha256": lock["files"]["tokenizer/tokenizer.json"],
            "upstream_source_revision": lock["upstream_source_revision"], "upstream_version": lock["upstream_version"],
            "cases_sha256": digest(args.cases, budget), "contract_sha256": digest(args.contract, budget),
            "source_lock_sha256": digest(lock_path, budget), "exporter_sha256": digest(Path(__file__), budget),
            "upstream_agent_sha256": digest(agent_source, budget), "upstream_common_sha256": digest(common_source, budget),
            "max_len": 1024, "head_max_len": 256, "temperature_policy": "checkpoint_fixed_1_no_overrides",
            "implementation": "pinned_upstream_laya_agent", "backend": "pytorch_cpu_fp32", "batch_size": 1,
            "python": platform.python_version(), "platform": platform.platform(), "machine": platform.machine(),
            "direct_dependencies": prepared["direct_dependencies"], "dependencies": dependency_inventory,
            "torch_version": torch.__version__, "torch_build": torch.__config__.show(),
            "threads": 1, "seed": 0, "deterministic_algorithms": True,
        }
        capture = {"schema_version": "sezika.laya-oracle.v1", "provenance": provenance,
                   "measurement_status": "in_progress", "total_manifest_cases": len(specs),
                   "selected_case_count": min(args.max_cases, len(specs)), "cases": []}
        write_json(output / "capture.partial.json", capture)
        budget.check("load pinned upstream model")
        with Agent(str(stage), device="cpu", fast=False, compile=False,
                   expected_sha256=lock["files"]) as agent:
            if agent.device.type != "cpu" or agent.amp_enabled or agent.dtype != torch.float32:
                raise ValueError("Upstream silently selected a different numerical backend")
            for index, parameter in enumerate(agent.model.parameters()):
                budget.check("verify FP32 model parameters")
                if index >= 512:
                    raise ValueError("Unexpected model parameter count")
                if parameter.is_floating_point() and parameter.dtype != torch.float32:
                    raise ValueError("Model contains a non-FP32 floating parameter")
            if agent.temperature != [1.0, 1.0, 1.0] or agent.temperature_by_options or agent.lang_temperatures:
                raise ValueError("Loaded model altered the frozen temperature policy")
            provenance["staged_tokenizer_config_sha256_after_upstream_fix"] = digest(stage / "tokenizer/tokenizer_config.json", budget)
            for index, spec in enumerate(budget.items(specs[:args.max_cases], 64, "export cases")):
                print(f"Case {index + 1}/{capture['selected_case_count']}: {spec['id']}", file=sys.stderr, flush=True)
                result = export_case(spec, manifest, agent, common, torch, np, budget)
                capture["cases"].append(result)
                write_json(output / "capture.partial.json", capture)
                print(f"Case {spec['id']}: {result['status']}", file=sys.stderr, flush=True)
        budget.check("complete oracle capture")
        capture["measurement_status"] = "complete" if len(capture["cases"]) == len(specs) else "selected_cases_only"
        capture["completed_utc"] = now()
        write_json(output / "capture.json", capture)
        run["status"] = "succeeded"
        run["captured_cases"] = len(capture["cases"])
        return 0
    except BaseException as error:
        run["status"] = "cancelled" if isinstance(error, (Cancelled, KeyboardInterrupt)) else "timed_out" if isinstance(error, TimeoutError) else "failed"
        run["failure"] = {"type": type(error).__name__, "message": str(error)}
        if capture is not None:
            capture["measurement_status"] = "incomplete"
            write_json(output / "capture.partial.json", capture)
        print(f"Oracle {run['status']}: {error}", file=sys.stderr, flush=True)
        return 2
    finally:
        run["ended_utc"] = now()
        run["elapsed_seconds"] = time.monotonic() - budget.started
        write_json(output / "run.json", run)


if __name__ == "__main__":
    raise SystemExit(main())
