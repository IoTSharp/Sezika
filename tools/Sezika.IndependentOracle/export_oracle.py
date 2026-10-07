"""Development-only, offline upstream capture. Standard-library self-tests never import a model backend."""
from __future__ import annotations

import argparse
import array
import contextlib
import ctypes
import hashlib
import importlib.metadata
import importlib.machinery
import importlib.util
import json
import math
import os
import platform
from pathlib import Path
import signal
import struct
import sys
import tempfile
import time
import types
import uuid

SCHEMA = "sezika.independent-reference.v1"
NAMES = ["embedding/raw", "embedding/norm"] + [f"layer/{i}/hidden" for i in range(22)] + ["encoder/final"]
MODEL_ID = "jhu-clsp/mmBERT-base"
REVISION = "c5955035435e2bf121cde7f3c8863ef52ff35d82"
MAX_METADATA = 1_048_576
MODERNBERT_MODULE = "transformers.models.modernbert.modeling_modernbert"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def same_sha(actual, expected):
    return isinstance(expected, str) and len(expected) == 64 and actual.upper() == expected.upper()


def safe_path(path):
    started = time.monotonic()
    full = Path(os.path.abspath(path))
    for depth, component in enumerate([full, *full.parents]):
        require(time.monotonic() - started < 5, "Path ancestry inspection deadline exceeded")
        require(depth < 64, "Path ancestry exceeds 64 components")
        require(not component.is_symlink() and not (hasattr(component, "is_junction") and component.is_junction()), "Links/junctions are unsupported")
    return full


def within(path, directory):
    return path == directory or directory in path.parents


class Budget:
    def __init__(self, seconds, cancel_file=None, resident_limit=6_442_450_944):
        require(0 < seconds <= 180, "Deadline must be in (0,180]")
        self.started = time.monotonic()
        self.seconds = seconds
        self.cancel_file = safe_path(cancel_file) if cancel_file else None
        self.cancelled = False
        self.resident_limit = resident_limit
        self.last_sample = -1.0
        self.peak_bytes = 0

    def check(self):
        require(not self.cancelled and not (self.cancel_file and self.cancel_file.exists()), "Capture cancelled")
        if time.monotonic() - self.started >= self.seconds:
            raise TimeoutError("Capture wall-clock deadline exceeded")
        if time.monotonic() - self.last_sample >= 0.1:
            self.last_sample = time.monotonic()
            if os.name == "nt":
                class Counters(ctypes.Structure):
                    _fields_ = [("cb", ctypes.c_ulong), ("PageFaultCount", ctypes.c_ulong)] + [(name, ctypes.c_size_t) for name in
                        ("PeakWorkingSetSize", "WorkingSetSize", "QuotaPeakPagedPoolUsage", "QuotaPagedPoolUsage", "QuotaPeakNonPagedPoolUsage", "QuotaNonPagedPoolUsage", "PagefileUsage", "PeakPagefileUsage", "PrivateUsage")]
                counters = Counters(); counters.cb = ctypes.sizeof(counters)
                kernel = ctypes.WinDLL("kernel32", use_last_error=True)
                kernel.GetCurrentProcess.restype = ctypes.c_void_p
                api = ctypes.WinDLL("psapi", use_last_error=True).GetProcessMemoryInfo
                api.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ulong]; api.restype = ctypes.c_int
                require(api(kernel.GetCurrentProcess(), ctypes.byref(counters), counters.cb), "Cannot sample process memory")
                observed = max(counters.PeakWorkingSetSize, counters.WorkingSetSize)
            else:
                import resource
                observed = resource.getrusage(resource.RUSAGE_SELF).ru_maxrss * (1 if sys.platform == "darwin" else 1024)
            self.peak_bytes = max(self.peak_bytes, observed)
        require(self.peak_bytes <= self.resident_limit, "Observed process peak exceeds resident budget")


@contextlib.contextmanager
def read_lease(path):
    """Windows denies writes/deletion while all actual consumers read the same source. POSIX is advisory only."""
    path = safe_path(path)
    if os.name == "nt":
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        create = kernel.CreateFileW
        create.argtypes = [ctypes.c_wchar_p, ctypes.c_ulong, ctypes.c_ulong, ctypes.c_void_p, ctypes.c_ulong, ctypes.c_ulong, ctypes.c_void_p]
        create.restype = ctypes.c_void_p
        handle = create(str(path), 0x80000000, 1, None, 3, 0x80, None)
        if handle == ctypes.c_void_p(-1).value:
            raise OSError(ctypes.get_last_error(), "Read-only source lease failed", str(path))
        try:
            yield path
        finally:
            close = kernel.CloseHandle; close.argtypes = [ctypes.c_void_p]; close.restype = ctypes.c_int
            require(close(handle), "Read-only source lease release failed")
    else:
        import fcntl
        with path.open("rb") as stream:
            fcntl.flock(stream.fileno(), fcntl.LOCK_SH | fcntl.LOCK_NB)
            try:
                yield path
            finally:
                fcntl.flock(stream.fileno(), fcntl.LOCK_UN)


def read_small(path, budget):
    budget.check(); path = safe_path(path)
    size = path.stat().st_size
    require(0 < size <= MAX_METADATA, "Metadata must be 1..1MiB")
    with path.open("rb") as stream:
        data = stream.read(MAX_METADATA + 1)
    require(len(data) == size, "Metadata changed while reading")
    budget.check()
    return data


def strict_json(data, budget):
    require(0 < len(data) <= MAX_METADATA, "JSON size exceeds metadata bound")
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, "Duplicate JSON property")
            result[key] = value
        return result
    def invalid_number(value):
        raise ValueError("Non-finite JSON number: " + value)
    value = json.loads(data, object_pairs_hook=pairs, parse_constant=invalid_number)
    stack = [(value, 0)]
    for index in range(100_000):
        if not stack:
            return value
        if index % 256 == 0: budget.check()
        item, depth = stack.pop()
        require(depth <= 16, "JSON nesting exceeds 16")
        if isinstance(item, dict): stack.extend((child, depth + 1) for child in item.values())
        elif isinstance(item, list): stack.extend((child, depth + 1) for child in item)
        elif isinstance(item, float): require(math.isfinite(item), "Non-finite parsed JSON number")
    raise ValueError("JSON node limit exceeded")


def identity(path, budget, maximum, expected=None, expected_bytes=None):
    path = safe_path(path); budget.check()
    size = path.stat().st_size
    require(0 < size <= maximum and (expected_bytes is None or size == expected_bytes), "Asset byte extent differs")
    digest = hashlib.sha256()
    blocks = (size + 1_048_575) // 1_048_576
    require(blocks <= 2048, "Asset hash block bound exceeded")
    with path.open("rb") as stream:
        remaining = size
        for _ in range(blocks):
            budget.check(); data = stream.read(min(1_048_576, remaining))
            require(data, "Asset shortened during hashing"); digest.update(data); remaining -= len(data)
        require(remaining == 0 and not stream.read(1), "Asset byte extent changed")
    value = digest.hexdigest().upper()
    require(expected is None or same_sha(value, expected), "Asset SHA differs: " + str(path))
    return {"path": str(path), "sha256": value, "bytes": size}


def relative_asset(root, name):
    require(isinstance(name, str) and name and not Path(name).is_absolute(), "Input artifact must use a relative path")
    path = safe_path(root / name)
    require(within(path, root) and path != root, "Artifact escaped input root")
    return path


def fresh_bytecode_policy(parent, budget):
    budget.check()
    require(MODERNBERT_MODULE not in sys.modules, "ModernBERT was imported before source verification")
    require(len(sys.modules) <= 4096, "Pre-import module inventory bound exceeded")
    for index, name in enumerate(tuple(sys.modules)):
        require(index < 4096, "Pre-import module inventory bound exceeded")
        if index % 64 == 0: budget.check()
        require(not any(name == item or name.startswith(item + ".") for item in ("torch", "transformers", "numpy")), "Backend was imported before source verification")
    prefix = safe_path(parent / (".oracle-pycache-uncreated-" + uuid.uuid4().hex))
    require(within(prefix, parent) and prefix != parent and not prefix.exists(), "Bytecode isolation prefix must be fresh and absent")
    sys.dont_write_bytecode = True; sys.pycache_prefix = str(prefix)
    return {"strategy": "fresh_absent_pycache_prefix_and_dont_write_bytecode_before_backend_imports", "prefix": str(prefix), "created": False}


def verify_bytecode_policy(policy, budget):
    budget.check()
    require(sys.dont_write_bytecode is True and sys.pycache_prefix == policy["prefix"] and not safe_path(policy["prefix"]).exists(), "Bytecode isolation policy changed during import")


def leased_upstream_source(distribution, leases, budget, expected_sha256):
    budget.check()
    require(MODERNBERT_MODULE not in sys.modules, "ModernBERT was imported before its source lease")
    path = safe_path(distribution.locate_file("transformers/models/modernbert/modeling_modernbert.py"))
    require(path.suffix == ".py", "Upstream source must be a Python source file")
    leases.enter_context(read_lease(path))
    result = identity(path, budget, MAX_METADATA, expected_sha256)
    result.update(package="transformers", version=distribution.version)
    return result


def verify_imported_source(module, upstream, policy, budget):
    verify_bytecode_policy(policy, budget)
    require(safe_path(module.__file__) == safe_path(upstream["path"]), "Imported ModernBERT source path differs from leased source")
    require(isinstance(module.__loader__, importlib.machinery.SourceFileLoader), "ModernBERT requires the standard CPython source loader")
    cached = safe_path(module.__cached__)
    require(within(cached, safe_path(policy["prefix"])) and not cached.exists(), "ModernBERT cached bytecode was not isolated")


def integers(values):
    require(isinstance(values, list) and 0 < len(values) <= 46 and all(type(v) is int and 0 <= v <= 2_147_483_647 for v in values), "Invalid bounded I32 input")
    return struct.pack("<" + "i" * len(values), *values)


def validate_contract(contract):
    require(contract["schema_version"] == "sezika.independent-oracle-contract.v1" and contract["status"] == "frozen_before_upstream_measurement", "Unexpected oracle contract")
    require(contract["source_model_id"] == MODEL_ID and contract["source_revision"] == REVISION, "Unfixed source model identity")
    require(contract["case_id"] == "independent-smoke-46-v1" and contract["token_count"] == 46 and contract["hidden_size"] == 768 and contract["layer_count"] == 22,
        "Only the fixed single 46-token encoder case is supported")
    require(contract["checkpoints"] == NAMES and contract["checkpoint_count"] == 25 and contract["comparison_count"] == 26, "Checkpoint selection/order differs")
    execution = contract["execution"]
    require(execution["backend"] == "pytorch_cpu_fp32" and execution["attention"] == "eager" and execution["threads"] == 4 and
        execution["compile"] is False and execution["autocast"] is False and execution["network"] == "offline_only", "Execution policy differs")
    limits = contract["limits"]
    require(limits["maximum_cases"] == 1 and limits["maximum_tokens"] == 46 and limits["maximum_checkpoints"] == 25 and limits["maximum_comparisons"] == 26 and
        limits["maximum_oracle_seconds"] <= 180 and limits["maximum_resident_bytes"] <= 6_442_450_944 and limits["maximum_output_bytes"] <= 20_971_520, "Limits exceed approved scope")


def validate_input(actual, contract, root, budget):
    require(actual["schema_version"] == 1 and actual["status"] == "cpu_capture_complete" and actual["kind"] == "csharp_cpu_capture" and actual["fixture_only"] is False,
        "Input must be a complete actual C# capture, never a fixture or oracle")
    for field in ("case_id", "source_model_id", "source_revision", "protocol", "length_policy", "integer_dtype", "integer_layout"):
        require(actual[field] == contract[field], "Realized input differs: " + field)
    require(len(actual["token_ids"]) == 46 and actual["marker_positions"] == contract["marker_positions"] == [25, 35], "Input token/marker count differs")
    for field, checksum in (("token_ids", "token_ids_sha256"), ("marker_positions", "marker_positions_sha256")):
        data = integers(actual[field]); require(same_sha(sha(data), contract[checksum]), "Realized integer identity differs")
        artifact = actual[field + "_artifact"]
        path = relative_asset(root, artifact["path"])
        identity(path, budget, 46 * 4, contract[checksum], len(data))
        require(path.read_bytes() == data and same_sha(artifact["sha256"], contract[checksum]) and artifact["bytes"] == len(data), "Integer artifact differs from realized values")
    request = relative_asset(root, actual["request"]["path"])
    require(same_sha(sha(read_small(request, budget)), contract["request_sha256"]), "Request identity differs")
    require(len(actual["tensors"]) == 267 and actual["expected_checkpoints"] == 267, "Actual capture must retain its complete 267-checkpoint denominator")
    # Actual tensor values are never read or copied into the upstream reference.


class Stage:
    def __init__(self, destination, budget, output_limit):
        self.destination = safe_path(destination)
        require(not self.destination.exists(), "Output directory already exists")
        require(self.destination.parent.is_dir(), "Output parent must exist")
        self.root = self.destination.with_name(self.destination.name + ".partial-" + uuid.uuid4().hex)
        self.root.mkdir(); self.budget = budget; self.limit = output_limit; self.total = 0
        self.created = []; self.published = False
        self.owned = {}; self.owned_ids = {}
        stat = self.root.stat(); self.directory_identity = (stat.st_dev, stat.st_ino)

    def owned_directory(self, directory):
        directory = safe_path(directory); stat = directory.stat()
        require((stat.st_dev, stat.st_ino) == self.directory_identity, "Task stage directory identity changed")
        return directory

    def write(self, name, data):
        self.budget.check()
        require(len(self.created) < 48 and "/" not in name and "\\" not in name and name not in self.created, "Output name or artifact count invalid")
        require(self.total + len(data) <= self.limit, "Output budget exceeded")
        path = self.root / name
        file_identity = None
        try:
            with path.open("xb") as stream:
                stat = os.fstat(stream.fileno()); file_identity = (stat.st_dev, stat.st_ino)
                stream.write(data); stream.flush(); os.fsync(stream.fileno())
        except BaseException:
            if file_identity is not None and path.exists():
                self.owned_directory(self.root); path = safe_path(path)
                require(within(path, self.root), "Partial-write cleanup escaped owned stage")
                stat = path.stat()
                if (stat.st_dev, stat.st_ino) == file_identity: path.unlink()
            raise
        self.created.append(name); self.total += len(data)
        self.owned[name] = sha(data)
        self.owned_ids[name] = file_identity
        return {"path": name, "sha256": sha(data), "bytes": len(data), "verification": "captured_upstream_bytes", "source_path": None}

    def progress(self, metadata, terminal=False):
        if not terminal: self.budget.check()
        data = json.dumps(metadata, ensure_ascii=True, allow_nan=False, indent=2).encode()
        require(len(data) <= MAX_METADATA and self.total + len(data) <= self.limit, "Partial metadata exceeds budget")
        directory = self.owned_directory(self.destination if self.published else self.root)
        temp = directory / ("capture.partial.json.tmp-" + uuid.uuid4().hex)
        temp_identity = None
        try:
            with temp.open("xb") as stream:
                stat = os.fstat(stream.fileno()); temp_identity = (stat.st_dev, stat.st_ino)
                stream.write(data); stream.flush(); os.fsync(stream.fileno())
            target = safe_path(directory / "capture.partial.json")
            if target.exists():
                stat = target.stat()
                require((stat.st_dev, stat.st_ino) == self.owned_ids.get("capture.partial.json") and stat.st_size <= MAX_METADATA and sha(target.read_bytes()) == self.owned.get("capture.partial.json"), "Partial manifest ownership changed; retained")
            os.replace(temp, target)
            self.owned["capture.partial.json"] = sha(data)
            self.owned_ids["capture.partial.json"] = temp_identity
        finally:
            # Only this known task-created temporary file; the partial directory is retained as evidence.
            if temp_identity is not None and temp.exists():
                self.owned_directory(directory); temp = safe_path(temp)
                require(within(temp, directory), "Progress cleanup escaped owned stage")
                stat = temp.stat()
                require((stat.st_dev, stat.st_ino) == temp_identity, "Progress temporary identity changed; retained")
                temp.unlink()

    def tensor(self, name, values, shape):
        self.budget.check()
        require(shape in ([46, 768], [2, 768]) and len(values) == math.prod(shape), "Captured tensor shape differs")
        for index in range(len(values)):
            if index % 256 == 0: self.budget.check()
            require(math.isfinite(values[index]), "Non-finite upstream tensor")
        data = array.array("f", values)
        require(data.itemsize == 4, "Host float is not F32")
        if sys.byteorder != "little": data.byteswap()
        file_name = "marker-vectors.f32" if name == "marker-vectors.f32" else f"tensor-{len([f for f in self.created if f.startswith('tensor-')]):03}.f32"
        item = self.write(file_name, data.tobytes())
        return {"name": name, "path": item["path"], "dtype": "F32", "shape": shape, "layout": "little_endian_row_major", "bytes": item["bytes"], "sha256": item["sha256"]}

    def publish(self, metadata):
        require(os.name == "nt", "No-clobber directory publication is currently Windows-only")
        self.budget.check()
        self.write("capture.json", json.dumps(metadata, ensure_ascii=True, allow_nan=False, indent=2).encode())
        try:
            self.budget.check()
            require(not self.destination.exists(), "Competing output directory appeared")
            os.rename(self.root, self.destination); self.published = True
            self.budget.check()
        except BaseException:
            directory = self.owned_directory(self.destination if self.published else self.root)
            complete = safe_path(directory / "capture.json")
            require(within(complete, directory), "Cleanup escaped owned stage")
            stat = complete.stat()
            require((stat.st_dev, stat.st_ino) == self.owned_ids.get("capture.json") and stat.st_size <= MAX_METADATA and sha(complete.read_bytes()) == self.owned.get("capture.json"), "Complete manifest identity changed; retained")
            if self.published and not self.root.exists():
                os.rename(self.destination, self.root); self.published = False
                complete = safe_path(self.root / "capture.json")
            complete.unlink(); self.owned.pop("capture.json", None); self.owned_ids.pop("capture.json", None)
            raise

    def cleanup_fixture(self, parent, cleanup_budget):
        directory = self.owned_directory(self.destination if self.published else self.root)
        require(within(directory, parent) and directory != parent and len(self.owned) <= 48, "Fixture cleanup ownership differs")
        for index, (name, expected) in enumerate(self.owned.items()):
            cleanup_budget.check(); require(index < 48, "Fixture cleanup item limit")
            path = safe_path(directory / name)
            require(within(path, directory) and path.is_file(), "Fixture cleanup path differs")
            stat = path.stat(); require((stat.st_dev, stat.st_ino) == self.owned_ids[name], "Fixture cleanup file identity changed; retained")
            identity(path, cleanup_budget, 20_971_520, expected)
            path.unlink()
        directory.rmdir()


def capture(args, budget):
    require(os.name == "nt", "The fixed production oracle is Windows-only; POSIX no-replace publication remains unsupported")
    source = safe_path(args.source_model_dir); actual_path = safe_path(args.actual_capture); actual_root = actual_path.parent
    output = safe_path(args.output_dir)
    require(not within(output, source) and not within(source, output) and not within(output, actual_root) and not within(actual_root, output), "Output collides with source/input tree")
    contract_path = safe_path(args.contract)
    stage = None
    with contextlib.ExitStack() as leases:
        for path in (contract_path, actual_path, source / "pytorch_model.bin", source / "config.json", source / "tokenizer.json"):
            leases.enter_context(read_lease(path))
        contract_bytes = read_small(contract_path, budget)
        require(same_sha(sha(contract_bytes), args.expected_contract_sha256), "External contract SHA differs")
        contract = strict_json(contract_bytes, budget); validate_contract(contract)
        require(args.timeout_seconds <= contract["limits"]["maximum_oracle_seconds"], "Requested deadline exceeds contract")
        budget.resident_limit = contract["limits"]["maximum_resident_bytes"]
        actual_bytes = read_small(actual_path, budget); actual = strict_json(actual_bytes, budget)
        for name in (actual["token_ids_artifact"]["path"], actual["marker_positions_artifact"]["path"], actual["request"]["path"]):
            leases.enter_context(read_lease(relative_asset(actual_root, name)))
        validate_input(actual, contract, actual_root, budget)
        os.environ.update(HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1", HF_HUB_DISABLE_TELEMETRY="1", TOKENIZERS_PARALLELISM="false")
        if args.dependency_directory:
            deps = safe_path(args.dependency_directory); require(deps.is_dir(), "Explicit dependency directory is missing"); sys.path.insert(0, str(deps))
        expected = contract["execution"]
        require(same_sha(args.expected_upstream_source_sha256, expected["modernbert_source_sha256"]), "External upstream source SHA differs from frozen execution")
        require(platform.python_version() == expected["python"], "Interpreter version differs from frozen execution")
        dependencies = []
        for index, distribution in enumerate(importlib.metadata.distributions()):
            budget.check(); require(index < 512, "Installed package inventory bound exceeded")
            dependencies.append({"name": distribution.metadata["Name"], "version": distribution.version})
        for name in ("torch", "transformers", "numpy"):
            require(importlib.metadata.version(name).split("+")[0] == expected[name], "Package version differs: " + name)
        bytecode_policy = fresh_bytecode_policy(output.parent, budget)
        upstream = leased_upstream_source(importlib.metadata.distribution("transformers"), leases, budget, args.expected_upstream_source_sha256)
        import torch
        import transformers
        import numpy
        from transformers.models.modernbert import modeling_modernbert
        from transformers.models.modernbert.configuration_modernbert import ModernBertConfig
        verify_imported_source(modeling_modernbert, upstream, bytecode_policy, budget)
        budget.check()
        require(torch.__version__.split("+")[0] == expected["torch"] and transformers.__version__ == expected["transformers"] and numpy.__version__ == expected["numpy"], "Imported backend differs from metadata")
        leases.enter_context(read_lease(__file__))
        program = identity(__file__, budget, MAX_METADATA)
        weights = identity(source / "pytorch_model.bin", budget, 2_147_483_648, contract["source_weights_sha256"], contract["source_weights_bytes"])
        config_identity = identity(source / "config.json", budget, MAX_METADATA, contract["config_sha256"])
        tokenizer = identity(source / "tokenizer.json", budget, 20_971_520, contract["tokenizer_sha256"])
        raw_config = strict_json(read_small(source / "config.json", budget), budget)
        require(raw_config["hidden_size"] == 768 and raw_config["num_hidden_layers"] == 22 and raw_config["num_attention_heads"] == 12 and raw_config["hidden_activation"] == "gelu", "Original config architecture differs")
        stage = Stage(output, budget, contract["limits"]["maximum_output_bytes"])
        metadata = {"schema_version": SCHEMA, "status": "partial", "kind": "upstream_original_weights_capture", "fixture_only": False, "original_weights_verified": False}
        try:
            metadata.update({"schema_version": SCHEMA, "status": "partial", "kind": "upstream_original_weights_capture", "fixture_only": False,
            "case_id": contract["case_id"], "model_id": actual["model_id"], "source_model_id": MODEL_ID, "source_revision": REVISION,
            "protocol": contract["protocol"], "length_policy": contract["length_policy"], "integer_dtype": contract["integer_dtype"], "integer_layout": contract["integer_layout"],
            "source_weights_sha256": weights["sha256"], "source_weights_bytes": weights["bytes"], "config_sha256": config_identity["sha256"], "tokenizer_sha256": tokenizer["sha256"],
            "contract_sha256": sha(contract_bytes), "actual_capture_sha256": sha(actual_bytes), "oracle_program": program, "upstream_source": upstream,
            "execution": expected, "original_weights_verified": False, "token_ids": actual["token_ids"], "marker_positions": actual["marker_positions"],
            "token_ids_artifact": stage.write("token-ids.i32", integers(actual["token_ids"])), "marker_positions_artifact": stage.write("marker-positions.i32", integers(actual["marker_positions"])),
            "request": stage.write("request.json", read_small(relative_asset(actual_root, actual["request"]["path"]), budget)),
            "assets": [dict(item, verification="verified_actual_source_bytes", source_path=item["path"]) for item in (weights, config_identity, tokenizer, program, upstream)],
            "tensors": [], "marker_vectors": None, "started_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "completed_utc": None, "elapsed_seconds": None,
            "process": {"pid": os.getpid(), "parent_pid": os.getppid(), "arguments": sys.argv, "parent_chain_evidence": "Required external bounded runner records Windows tree; Linux requires timeout wrapper."},
            "environment": {"torch_build": torch.__version__, "dependencies": dependencies, "platform": platform.platform(), "python_executable": sys.executable,
                "bytecode_policy": bytecode_policy, "upstream_source_leased_before_backend_imports": True,
                "observed_peak_bytes": budget.peak_bytes, "source_lease_policy": "Windows FILE_SHARE_READ denies write/delete; POSIX flock is advisory; ancestor mutation unsupported."},
            "represents_upstream_parity": False, "represents_model_quality": False, "represents_calibration": False, "represents_benchmark": False, "represents_license_approval": False})
            stage.progress(metadata)
            torch.set_num_threads(4); torch.set_num_interop_threads(1); torch.manual_seed(0)
            torch.set_default_dtype(torch.float32); torch.use_deterministic_algorithms(True)
            config = ModernBertConfig.from_dict(raw_config); config._attn_implementation = "eager"; config.reference_compile = False
            model = modeling_modernbert.ModernBertModel(config).to(device="cpu", dtype=torch.float32)
            budget.check()
            original = torch.load(str(source / "pytorch_model.bin"), map_location="cpu", weights_only=True, mmap=True)
            require(isinstance(original, dict) and len(original) <= 300, "Original tensor metadata exceeds 300")
            encoder = {}
            for index, (name, value) in enumerate(original.items()):
                budget.check(); require(index < 300 and isinstance(name, str) and isinstance(value, torch.Tensor), "Unsupported original state item")
                if name.startswith("model."):
                    require(value.dtype == torch.float32 and value.device.type == "cpu", "Original encoder tensor is not CPU F32")
                    encoder[name[6:]] = value
            require(len(encoder) == 134 and set(encoder) == set(model.state_dict()), "Original model.* encoder tensor names differ from upstream")
            model.load_state_dict(encoder, strict=True); model.eval(); budget.check()
            metadata["original_weights_verified"] = True
            handles = []
            def accept(name, result):
                budget.check()
                require(len(metadata["tensors"]) < 25 and name == NAMES[len(metadata["tensors"])], "Unknown/duplicate/reordered upstream hook")
                require(isinstance(result, torch.Tensor) and list(result.shape) == [1, 46, 768] and result.dtype == torch.float32 and result.device.type == "cpu", "Upstream hook shape/dtype/device differs")
                tensor = stage.tensor(name, result.detach().reshape(-1).tolist(), [46, 768]); metadata["tensors"].append(tensor)
                stage.progress(metadata); print(f"Captured {len(metadata['tensors'])}/25: {name}", flush=True)
            def hook(name):
                return lambda _module, _inputs, output: accept(name, output[0] if isinstance(output, tuple) else output)
            try:
                handles.append(model.embeddings.tok_embeddings.register_forward_hook(hook("embedding/raw")))
                handles.append(model.embeddings.norm.register_forward_hook(hook("embedding/norm")))
                require(len(model.layers) == 22, "Upstream layer count differs")
                for index in range(22): budget.check(); handles.append(model.layers[index].register_forward_hook(hook(f"layer/{index}/hidden")))
                handles.append(model.final_norm.register_forward_hook(hook("encoder/final")))
                ids = torch.tensor([actual["token_ids"]], dtype=torch.long, device="cpu")
                mask = torch.ones_like(ids)
                with torch.inference_mode(), torch.autocast(device_type="cpu", enabled=False):
                    result = model(input_ids=ids, attention_mask=mask, return_dict=True).last_hidden_state
                budget.check(); require(len(metadata["tensors"]) == 25, "Upstream capture is incomplete")
                metadata["marker_vectors"] = stage.tensor("marker-vectors.f32", result[0, actual["marker_positions"], :].detach().reshape(-1).tolist(), [2, 768])
            finally:
                for index, handle in enumerate(handles): require(index < 25, "Hook cleanup bound exceeded"); handle.remove()
            metadata.update(status="complete", completed_utc=time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), elapsed_seconds=time.monotonic() - budget.started)
            metadata["environment"]["observed_peak_bytes"] = budget.peak_bytes
            stage.publish(metadata)
            print(f"Upstream complete: 25 checkpoints + markers; output={output}; parity=false", flush=True)
        except BaseException as error:
            metadata.update(status="partial", terminal_error=type(error).__name__, terminal_detail=str(error)[:2048])
            # Capture data already flushed remains even when cancellation prevents another metadata refresh.
            try: stage.progress(metadata, terminal=True)
            except BaseException as terminal_error:
                print(f"Terminal partial metadata could not be updated: {type(terminal_error).__name__}: {terminal_error}", file=sys.stderr)
            print(f"Incomplete upstream evidence retained: {stage.destination if stage.published else stage.root}", file=sys.stderr)
            raise


def self_tests(maximum, budget):
    require(1 <= maximum <= 16, "Self-test bound must be 1..16")
    def rejects(action):
        try: action()
        except (ValueError, TimeoutError): return
        raise AssertionError("Expected fixture rejection")
    root = safe_path(tempfile.mkdtemp(prefix="sezika-independent-oracle-selftest-"))
    root_stat = root.stat(); fixture_root_identity = (root_stat.st_dev, root_stat.st_ino)
    require(root.parent == safe_path(tempfile.gettempdir()) and root.name.startswith("sezika-independent-oracle-selftest-"), "Temporary fixture root differs")
    stages = []
    try:
        def fresh_policy_test():
            old_prefix, old_write = sys.pycache_prefix, sys.dont_write_bytecode
            try:
                policy = fresh_bytecode_policy(root, budget)
                require(not Path(policy["prefix"]).exists() and policy["created"] is False, "Isolation prefix was created")
                verify_bytecode_policy(policy, budget)
                sys.modules[MODERNBERT_MODULE] = object()
                rejects(lambda: fresh_bytecode_policy(root, budget))
            finally:
                sys.modules.pop(MODERNBERT_MODULE, None)
                sys.pycache_prefix, sys.dont_write_bytecode = old_prefix, old_write

        def leased_source_test():
            stage = Stage(root / "source-policy", budget, 20_971_520); stages.append(stage)
            data = b"# standard-library fixture only; never imported\n"
            stage.write("modeling_modernbert.py", data); source_path = stage.root / "modeling_modernbert.py"
            class FixtureDistribution:
                version = "fixture-only"
                def locate_file(self, name):
                    require(name == "transformers/models/modernbert/modeling_modernbert.py", "Source locate path differs")
                    return source_path
            old_prefix, old_write = sys.pycache_prefix, sys.dont_write_bytecode
            try:
                policy = fresh_bytecode_policy(root, budget)
                with contextlib.ExitStack() as leases:
                    upstream = leased_upstream_source(FixtureDistribution(), leases, budget, sha(data))
                    require(upstream["sha256"] == sha(data) and MODERNBERT_MODULE not in sys.modules, "Source lease/hash must precede backend import")
                    if os.name == "nt":
                        try:
                            with source_path.open("r+b"): pass
                        except PermissionError: pass
                        else: raise AssertionError("Source write was allowed while pre-import lease is active")
                    module = types.SimpleNamespace(__file__=str(source_path),
                        __loader__=importlib.machinery.SourceFileLoader("fixture-only", str(source_path)),
                        __cached__=importlib.util.cache_from_source(str(source_path)))
                    verify_imported_source(module, upstream, policy, budget)
                    module.__file__ = str(root / "different-source.py")
                    rejects(lambda: verify_imported_source(module, upstream, policy, budget))
                with contextlib.ExitStack() as leases:
                    rejects(lambda: leased_upstream_source(FixtureDistribution(), leases, budget, "0" * 64))
            finally:
                sys.pycache_prefix, sys.dont_write_bytecode = old_prefix, old_write

        tests = [
            lambda: require(strict_json(b'{"a":1}', budget) == {"a": 1}, "JSON roundtrip"),
            lambda: (rejects(lambda: strict_json(b'{"a":1,"a":2}', budget)), rejects(lambda: strict_json(b'{"a":{"x":1,"x":2}}', budget))),
            fresh_policy_test,
            lambda: (rejects(lambda: strict_json(b'{"a":NaN}', budget)), rejects(lambda: strict_json(b'{"a":1e400}', budget))),
            lambda: rejects(lambda: strict_json(b'[' * 18 + b'0' + b']' * 18, budget)),
            lambda: require(integers([25, 35]) == struct.pack("<ii", 25, 35), "I32 layout"),
            lambda: rejects(lambda: integers([True])),
            lambda: (rejects(lambda: relative_asset(root, "../escape")), rejects(lambda: relative_asset(root, str(root)))),
            leased_source_test,
            lambda: require(len(NAMES) == 25 and len(set(NAMES)) == 25 and NAMES[-1] == "encoder/final", "Exact checkpoint selection")]
        def output_tests():
            stage = Stage(root / "published", budget, 20_971_520)
            stages.append(stage)
            tensor = stage.tensor("embedding/raw", [0.25] * (46 * 768), [46, 768])
            require(tensor["bytes"] == 46 * 768 * 4 and tensor["layout"] == "little_endian_row_major", "F32 fixture serialization")
            stage.publish({"fixture_only": True, "kind": "self_test_fixture"})
            rejects(lambda: Stage(root / "published", budget, 20_971_520))
        def finite_test():
            stage = Stage(root / "finite", budget, 20_971_520)
            stages.append(stage)
            rejects(lambda: stage.tensor("marker-vectors.f32", [math.inf] * (2 * 768), [2, 768]))
        def budget_test():
            stage = Stage(root / "limit", budget, 8)
            stages.append(stage)
            rejects(lambda: stage.write("oversized", b"123456789"))
        def cancelled_publish():
            isolated = Budget(5); stage = Stage(root / "cancelled", isolated, 20_971_520); stages.append(stage)
            write = stage.write
            def cancel_after_write(name, data):
                result = write(name, data); isolated.cancelled = True; return result
            stage.write = cancel_after_write
            rejects(lambda: stage.publish({"status": "complete", "fixture_only": True}))
            require(not stage.destination.exists() and not (stage.root / "capture.json").exists(), "Cancelled publication retained complete marker")
            stage.progress({"status": "partial", "fixture_only": True}, terminal=True)
        def cancelled_after_move():
            isolated = Budget(5); stage = Stage(root / "moved", isolated, 20_971_520); stages.append(stage)
            original_rename = os.rename; observed = []
            def cancel_after_move(source, destination):
                original_rename(source, destination)
                if destination == stage.destination: observed.append(True); isolated.cancelled = True
            try:
                os.rename = cancel_after_move
                rejects(lambda: stage.publish({"status": "complete", "fixture_only": True}))
            finally:
                os.rename = original_rename
            require(observed == [True] and not stage.destination.exists() and stage.root.is_dir() and not (stage.root / "capture.json").exists(), "Actual Move followed by cancellation did not roll back")
            stage.progress({"status": "partial", "fixture_only": True}, terminal=True)
        def collision_progress():
            stage = Stage(root / "progress", budget, 20_971_520); stages.append(stage)
            stage.write("capture.partial.json.tmp", b"unrelated-file")
            stage.progress({"status": "partial", "fixture_only": True})
            require((stage.root / "capture.partial.json.tmp").read_bytes() == b"unrelated-file", "Progress deleted a non-owned temporary path")
        tests += [output_tests, finite_test, budget_test, cancelled_publish, cancelled_after_move, collision_progress]
        for index in range(min(maximum, len(tests))):
            budget.check(); tests[index](); print(f"PASS {index + 1}/{min(maximum,len(tests))}: fixture_only=true", flush=True)
        print(f"Self-tests complete: {min(maximum,len(tests))}; model_imported=false; fixture_only=true", flush=True)
    finally:
        cleanup = Budget(5)
        require(len(stages) <= 8, "Fixture cleanup stage limit")
        for stage in stages: cleanup.check(); stage.cleanup_fixture(root, cleanup)
        root_stat = root.stat(); require((root_stat.st_dev, root_stat.st_ino) == fixture_root_identity, "Fixture root identity changed; retained")
        root.rmdir()


def validate_options(arguments):
    require(len(arguments) <= 32, "CLI argument item limit exceeded")
    seen = set()
    for token in arguments:
        if token.startswith("--"):
            name = token.partition("=")[0]
            require(name not in seen, "Repeated CLI option: " + name); seen.add(name)


def main():
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    commands = parser.add_subparsers(dest="command", required=True)
    test = commands.add_parser("self-test", allow_abbrev=False)
    test.add_argument("--max-tests", type=int, default=16); test.add_argument("--timeout-seconds", type=int, default=20); test.add_argument("--cancel-file")
    run = commands.add_parser("capture", allow_abbrev=False)
    for option in ("source-model-dir", "actual-capture", "contract", "output-dir", "expected-contract-sha256", "expected-upstream-source-sha256"):
        run.add_argument("--" + option, required=True)
    run.add_argument("--dependency-directory"); run.add_argument("--timeout-seconds", type=int, default=180); run.add_argument("--cancel-file")
    try: validate_options(sys.argv[1:])
    except ValueError as error: parser.error(str(error))
    args = parser.parse_args()
    require(1 <= args.timeout_seconds <= (30 if args.command == "self-test" else 180), "Argument timeout exceeds scope")
    budget = Budget(args.timeout_seconds, args.cancel_file)
    original_handlers = {}
    for sig in (signal.SIGINT, signal.SIGTERM):
        original_handlers[sig] = signal.signal(sig, lambda _sig, _frame: setattr(budget, "cancelled", True))
    try:
        if args.command == "self-test": self_tests(args.max_tests, budget)
        else: capture(args, budget)
        return 0
    except BaseException as error:
        print(f"independent-oracle: {type(error).__name__}: {error}", file=sys.stderr)
        return 124 if isinstance(error, TimeoutError) or budget.cancelled else 1
    finally:
        for sig, handler in original_handlers.items(): signal.signal(sig, handler)


if __name__ == "__main__":
    raise SystemExit(main())
