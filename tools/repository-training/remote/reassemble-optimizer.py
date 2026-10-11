#!/usr/bin/env python3
"""Reconstruct checkpoint assets only; this is not a model or training operator."""
import hashlib, json, pathlib, re, shutil, sys, time


def restore(run_directory, destination, maximum_seconds=300):
    started = time.monotonic()
    run = pathlib.Path(run_directory).resolve()
    output = pathlib.Path(destination).absolute()
    if not 1 <= maximum_seconds <= 900 or output.exists() or output.is_symlink():
        raise ValueError("New destination and bounded time required")
    def check():
        if time.monotonic() - started >= maximum_seconds or (run / "CANCEL").exists():
            raise TimeoutError("Asset restore deadline/cancel")
    def plain(path, directory=False, maximum_bytes=None):
        check()
        if path.is_symlink() or path.resolve() != path or (not path.is_dir() if directory else not path.is_file()):
            raise ValueError("Plain canonical asset path required")
        if maximum_bytes is not None and path.stat().st_size > maximum_bytes:
            raise ValueError("Asset size bound")
        return path
    plain(run, directory=True)
    plain(run / "transport", directory=True)
    if output.parent.resolve() != output.parent or output.parent.is_symlink():
        raise ValueError("Canonical new destination parent required")
    manifest_path = run / "transport/manifest.json"
    plain(manifest_path, maximum_bytes=65536)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    logical = manifest["logical_path"]
    if manifest["schema_version"] != 1 or not re.fullmatch(r"checkpoint-[0-9]{7}/moments\.safetensors", logical):
        raise ValueError("Checkpoint transport identity")
    checkpoint = run / pathlib.PurePosixPath(logical).parent
    plain(checkpoint, directory=True)
    receipt = json.loads(plain(checkpoint / "checkpoint.json", maximum_bytes=65536).read_text(encoding="utf-8-sig"))
    if receipt["RunId"] != manifest["run_id"] or receipt["MomentsSha256"] != manifest["logical_sha256"]:
        raise ValueError("Original checkpoint SHA/run identity")
    parts = manifest["parts"]
    if not 1 <= len(parts) <= 16 or not 1 <= manifest["logical_bytes"] <= 16*1024**3:
        raise ValueError("Transport bounds")
    declared = 0
    for index, row in enumerate(parts):
        check()
        if row["path"] != "transport/moments.part-" + format(index+1,"04d") + ".bin" or not isinstance(row["bytes"],int) or not 0 < row["bytes"] <= 1024**3 or not re.fullmatch(r"[a-f0-9]{64}",row["sha256"]):
            raise ValueError("Part order/size/SHA identity")
        part=plain(run/row["path"])
        if part.stat().st_size != row["bytes"]:
            raise ValueError("Declared part byte count differs")
        declared += row["bytes"]
    if declared != manifest["logical_bytes"]:
        raise ValueError("Logical byte count differs from all parts")
    plain(checkpoint/"weights.safetensors")
    if shutil.disk_usage(output.parent).free < manifest["logical_bytes"] + (checkpoint/"weights.safetensors").stat().st_size + 64*1024**2:
        raise ValueError("Restore capacity")
    output.mkdir()
    aggregate = hashlib.sha256(); count = 0; seen = set()
    # A partial destination is retained for diagnosis and is never called restored.
    with (output / "moments.safetensors").open("xb") as target:
        for row in parts:
            check(); name = row["path"]
            if not re.fullmatch(r"transport/moments\.part-[0-9]{4}\.bin", name) or name in seen:
                raise ValueError("Part name/order/duplicate")
            seen.add(name); part = run / name
            if part.is_symlink() or not part.is_file() or part.stat().st_size != row["bytes"] or not 0 < row["bytes"] < 2*1024**3:
                raise ValueError("Part file size/type")
            hashed = hashlib.sha256(); part_count = 0
            with part.open("rb") as source:
                for index in range(513):
                    check(); block = source.read(4*1024**2)
                    if not block: break
                    if count + len(block) > manifest["logical_bytes"]:
                        raise ValueError("Declared logical capacity exceeded")
                    hashed.update(block); aggregate.update(block); target.write(block)
                    count += len(block); part_count += len(block)
                else: raise ValueError("Part chunk bound")
            if part_count != row["bytes"] or hashed.hexdigest() != row["sha256"]:
                raise ValueError("Part SHA differs")
            print(json.dumps({"part":name,"status":"SHA_PASS"}), flush=True)
    if count != manifest["logical_bytes"] or aggregate.hexdigest() != manifest["logical_sha256"]:
        raise ValueError("Original optimizer full SHA differs")
    for name in ("checkpoint.json", "weights.safetensors"):
        check(); source = checkpoint / name; target = output / name
        if source.is_symlink() or not source.is_file(): raise ValueError("Checkpoint source type")
        # Copy to the new consumer directory, keeping its strict three-file contract.
        with source.open("rb") as src, target.open("xb") as dst:
            hashed = hashlib.sha256()
            for index in range(4097):
                check(); block = src.read(4*1024**2)
                if not block: break
                dst.write(block); hashed.update(block)
            else: raise ValueError("Copy chunk bound")
        if name == "weights.safetensors" and hashed.hexdigest() != receipt["WeightsSha256"]:
            raise ValueError("Restored weights SHA differs")
    return {"status":"PASS_three_checkpoint_files_original_SHA_restored","logical_reassembly_verified":True,
            "logical_bytes":count,"logical_sha256":aggregate.hexdigest(),"destination":str(output)}


if __name__ == "__main__":
    if len(sys.argv) != 4: raise SystemExit("usage: reassemble-optimizer.py run-directory new-checkpoint-directory maximum-seconds")
    print(json.dumps(restore(sys.argv[1], sys.argv[2], int(sys.argv[3]))), flush=True)
