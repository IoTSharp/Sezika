#!/usr/bin/env python3
"""Asset/Git publication contracts only. No model operators or credentials."""
import hashlib, json, math, os, pathlib, re, shutil, sys, time

HEX = re.compile(r"^[a-f0-9]{64}$")
REV = re.compile(r"^[a-f0-9]{40}$")
NOTICE_NAMES = frozenset(("BoolQ-CC-BY-SA-3.0-legalcode.html", "BoolQ-source-README.md",
    "Gemma-prohibited-use-policy-current.html", "Gemma-terms-current.html",
    "MASSIVE-CC-BY-4.0-LICENSE.txt", "MIT-LICENSE-TEMPLATE.txt", "mmbert-pinned-README.md",
    "SOURCE-README.md", "source-rights.json", "code-LICENSE.txt", "code-NOTICE.txt"))
MODEL_METADATA_NAMES = frozenset(("README.md", "package.json", "run-plan.json", "training-report.json",
    "resources-reclaimed.txt", "LICENSE-COMPONENTS.md", "input-data-manifest.json",
    "base-LICENSE-COMPONENTS.md", "base-license-provenance.json", "validation.json",
    "publication-binding.json", "publication-manifest.json", "files.sha256"))
started = time.monotonic()
mode, root_arg, run_id, kind, repo_arg, derived_arg, trained_arg, validation_arg, data_commit, seconds_arg = sys.argv[1:]
root = pathlib.Path(root_arg)
limit = int(seconds_arg)
assert 5 <= limit <= 900 and kind in ("dataset", "model") and REV.fullmatch(data_commit)
assert re.fullmatch(r"[a-z0-9][a-z0-9-]{2,63}", run_id)
assert root.is_absolute() and root.resolve() == root and root.name == "sezika-repository-" + run_id
assert (root / "OWNED_BY_SEZIKA_REPOSITORY_TASK").is_file()

def check():
    if time.monotonic() - started >= limit or (root / "CANCEL").exists():
        raise TimeoutError("Publication contract deadline/cancel")

def absolute(value):
    check()
    p = pathlib.Path(value)
    if not p.is_absolute() or p.resolve() != p or not p.is_relative_to(root) or p.is_symlink():
        raise ValueError("Publication path is not a plain task-owned path")
    for index, ancestor in enumerate(p.parents):
        check()
        if index >= 64 or ancestor.is_symlink():
            raise ValueError("Linked ancestry/bound64")
        if ancestor == root:
            return p
    raise ValueError("Missing task ancestry")

repo, derived, trained, validation = map(absolute, (repo_arg, derived_arg, trained_arg, validation_arg))
prefix = ("derived/" if kind == "dataset" else "runs/") + run_id + "/"
state_dir = root / "publication"
state_dir.mkdir(exist_ok=True)

def file(base, relative):
    check()
    p = pathlib.PurePosixPath(relative)
    if not re.fullmatch(r"[A-Za-z0-9_./-]+", relative) or p.is_absolute() or ".." in p.parts or str(p) != relative:
        raise ValueError("Unsafe relative asset path")
    result = absolute(str(base / relative))
    if not result.is_file():
        raise ValueError("Asset is not a regular file: " + relative)
    return result

def digest(path):
    check()
    if path.stat().st_size > 16 * 1024**3:
        raise ValueError("Hash asset bound16GiB")
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for index in range(4097):
            check()
            block = stream.read(4 * 1024**2)
            if not block:
                return h.hexdigest()
            h.update(block)
            if index % 64 == 0:
                print(json.dumps({"progress":"full_sha256", "path":str(path.relative_to(root)), "chunks":index + 1}), flush=True)
    raise ValueError("Hash chunk bound4097")

def read(path):
    check()
    if path.stat().st_size > 16 * 1024**2:
        raise ValueError("JSON bound16MiB")
    return json.loads(path.read_text(encoding="utf-8-sig"))

def write_new(path, value):
    check()
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False)
        stream.write("\n")

def entries(base):
    result = []
    for top_index, child in enumerate(base.iterdir()):
        check()
        if top_index >= 1001 or child.is_symlink():
            raise ValueError("Owned top-level bound1000/link")
        if child.is_dir():
            for index, nested in enumerate(child.iterdir()):
                check()
                if index >= 1001 or not nested.is_file() or nested.is_symlink():
                    raise ValueError("Owned depth2/bound1000/link")
                result.append(nested)
        elif child.is_file():
            result.append(child)
        else:
            raise ValueError("Owned file type")
        if len(result) > 1000:
            raise ValueError("Owned file bound1000")
    if not result:
        raise ValueError("Empty owned file set")
    return sorted(result)

def verified_package():
    package = read(file(trained, "package.json")); report = read(file(trained, "training-report.json"))
    plan = read(file(trained, "run-plan.json")); val = read(validation); inputs = package["Inputs"]
    if package["SchemaVersion"] != 1 or package["RunId"] != run_id or package["Method"] != "full_encoder_adamw_fp32":
        raise ValueError("Package run/method identity")
    updates=plan["MaximumUpdates"]
    if not isinstance(updates,int) or not 2<=updates<=1000000 or package["CompletedUpdates"] != updates or plan["RunId"] != run_id:
        raise ValueError("Authorized fixed update budget differs")
    for field in ("SourceCommit", "DatasetCommit", "ModelCommit"):
        if not REV.fullmatch(package[field]) or package[field] != plan[field]:
            raise ValueError("Source/dataset/model commit differs")
    if not plan["ExperimentalOnly"] or package["QualityAccepted"] or package["AotAccepted"] or package["CalibrationStatus"] != "uncalibrated":
        raise ValueError("Experimental quality boundary")
    if report["Status"] != "PASS_experimental_GPU_updates_saved_only" or report["RunId"] != run_id or report["InitialUpdates"] != 0 or report["CompletedUpdates"] != updates or report["Inputs"] != inputs or report["CheckpointReceiptSha256"] != package["CheckpointReceiptSha256"] or report["QualityAccepted"] or report["Calibrated"]:
        raise ValueError("Training report contract")
    if val["Status"] != "PASS_new_package_GPU_experimental_validation_only" or val["RunId"] != run_id or not val["SavedBytesVerified"] or val["QualityAccepted"]:
        raise ValueError("Independent saved-byte validation contract")
    if val["ProcessId"] == report["ProcessId"] and val["ProcessStartUtc"] == report["ProcessStartUtc"]:
        raise ValueError("Validation reused training process")
    if report["GpuUuid"] != val["GpuUuid"] or report["GpuUuid"] != plan["ExpectedGpuUuid"]:
        raise ValueError("GPU identity differs")
    for path, expected in ((file(trained,"package.json"),val["PackageSha256"]),(file(trained,"training-report.json"),package["TrainingReportSha256"]),(file(trained,"run-plan.json"),inputs["PlanSha256"]),(file(trained,"weights.safetensors"),package["WeightsSha256"]),(file(trained,"tokenizer/tokenizer.json"),package["TokenizerSha256"]),(file(derived,"data-manifest.json"),inputs["DataManifestSha256"]),(file(trained,"input-data-manifest.json"),inputs["DataManifestSha256"])):
        if not HEX.fullmatch(expected) or digest(path) != expected:
            raise ValueError("Actual package/input full SHA differs")
    manifest = read(file(derived,"data-manifest.json"))
    if manifest["DatasetCommit"] != package["DatasetCommit"] or manifest["FormalTrainingAdmitted"] or manifest["RecordsSha256"] != inputs["RecordsSha256"]:
        raise ValueError("Derived data training identity")
    for name, field in (("training-records.jsonl","RecordsSha256"),("derivation-ledger.jsonl","LedgerSha256")):
        if digest(file(derived,name)) != manifest[field]:
            raise ValueError("Derived records/ledger SHA")
    marker=file(trained,"resources-reclaimed.txt")
    if marker.read_text(encoding="utf-8") != "PASS_owned_buffers_modules_restored_and_context_disposed\n":
        raise ValueError("Training resources marker differs")
    checkpoints=sorted({p.parent for p in entries(trained) if p.parent.name.startswith("checkpoint-")})
    if not 1<=len(checkpoints)<=2 or (updates==48 and len(checkpoints)!=1):
        raise ValueError("Complete retained checkpoint bound1..2; this48step run requires one")
    latest=checkpoints[-1];checkpoint=read(file(latest,"checkpoint.json"))
    if checkpoint["RunId"] != run_id or checkpoint["CompletedUpdates"] != updates or checkpoint["Inputs"] != inputs or checkpoint["Config"] != package["Config"] or digest(file(latest,"checkpoint.json")) != package["CheckpointReceiptSha256"] or checkpoint["WeightsSha256"] != package["WeightsSha256"]:
        raise ValueError("Complete checkpoint binding differs")
    for name, field in (("weights.safetensors","WeightsSha256"),("moments.safetensors","MomentsSha256")):
        if digest(file(latest,name)) != checkpoint[field]:
            raise ValueError("Complete checkpoint full SHA differs")
    rows=val["Rows"]
    if not 1 <= len(rows) <= 1024:
        raise ValueError("Validation row bound")
    originals=[r for r in rows if not r["Rotated"]]; rotated=[r for r in rows if r["Rotated"]]
    if len(originals)!=val["OriginalRows"] or len(rotated)!=val["RotatedRows"] or len(originals)!=2*plan["ValidationPerSplit"]:
        raise ValueError("Validation denominators differ")
    for split in ("train","development"):
        if sum(r["Split"]==split for r in originals)!=plan["ValidationPerSplit"]:
            raise ValueError("Validation split denominator differs")
    original_map={r["RecordId"]:r for r in originals}
    if len(original_map)!=len(originals) or len(rotated)!=sum(r["Type"]=="Choice" for r in originals) or len({r["RecordId"] for r in rotated})!=len(rotated):
        raise ValueError("Independent choice rotation coverage")
    for row in rows:
        check()
        ids=row["CandidateIds"]; target=row["Target"]; probability=row["Probability"]
        if len(ids)!=len(target) or len(ids)!=len(probability) or not 1<=len(ids)<=128 or len(set(ids))!=len(ids):
            raise ValueError("Validation candidate dimensions")
        for vector in (target,probability):
            if not all(math.isfinite(x) and 0<=x<=1 for x in vector) or abs(sum(vector)-1)>0.001:
                raise ValueError("Validation distribution")
        if not math.isfinite(row["Loss"]) or row["Loss"]<0 or row["Split"] not in ("train","development"):
            raise ValueError("Validation loss/split")
        if row["PredictedCandidateId"] != ids[max(range(len(ids)),key=lambda i:probability[i])] or row["TeacherArgmaxCandidateId"] != ids[max(range(len(ids)),key=lambda i:target[i])]:
            raise ValueError("Validation actual candidate identity")
        if row["Rotated"]:
            original=original_map[row["RecordId"]]
            if row["Type"]!="Choice" or original["Type"]!="Choice" or set(ids)!=set(original["CandidateIds"]) or row["Split"]!=original["Split"] or dict(zip(ids,target))!=dict(zip(original["CandidateIds"],original["Target"])):
                raise ValueError("Choice rotation changed targets/candidate family")
    return package, val, manifest

def prepare():
    package, val, manifest = verified_package()
    owned=repo/prefix
    if owned.exists():
        raise ValueError("Owned destination exists; preserve it")
    owned.parent.mkdir(exist_ok=True)
    owned.mkdir()
    source=trained if kind=="model" else derived
    for path in entries(source):
        check(); relative=str(path.relative_to(source)); target=owned/relative
        target.parent.mkdir(exist_ok=True)
        with path.open("rb") as src, target.open("xb") as dst:
            for block_index in range(4097):
                check(); block=src.read(4*1024**2)
                if not block: break
                dst.write(block)
            else: raise ValueError("Copy chunk bound4097")
    if kind=="model":
        shutil.copyfile(validation,owned/"validation.json")
        binding={"schema_version":1,"run_id":run_id,"published_dataset_commit":data_commit,"original_dataset_commit":package["DatasetCommit"],"derived_manifest_sha256":package["Inputs"]["DataManifestSha256"],"validation_sha256":digest(validation),"experimental_only":True,"formal_quality":False,"calibration":"uncalibrated","aot_and_performance":"NOT_ACCEPTED"}
        write_new(owned/"publication-binding.json",binding)
        agreement=sum(r["PredictedCandidateId"]==r["TeacherArgmaxCandidateId"] for r in val["Rows"] if not r["Rotated"])
        updates=package['CompletedUpdates']; completion='完整一轮尚未训练。' if updates<manifest['Train'] else '仅完成本次固定计划的步数。'
        readme=f"# {run_id}\n\n完整 encoder 和新决策头全参数 AdamW 的{updates}步实验；原 train 共{manifest['Train']}条。{completion}\n\n入口：package.json；完整模型 weights.safetensors，tokenizer/tokenizer.json，完整恢复状态在 checkpoint-{updates:07d}/。训练报告、run-plan、来源许可及 input-data-manifest 均保留原字节。\n\n独立 GPU 验证原题{val['OriginalRows']}条，Choice换序{val['RotatedRows']}条。原题与教师argmax一致{agreement}/{val['OriginalRows']}，它不是实测业务正确率。细目见validation.json。\n\n数据发布commit：{data_commit}；原训练数据commit：{package['DatasetCommit']}。正式质量、校准、新权重Native AOT、性能：NOT_ACCEPTED；uncalibrated，实验用途。许可证按LICENSE-COMPONENTS.md分别处理，整体unknown。\n"
        with (owned/"README.md").open("x",encoding="utf-8",newline="\n") as stream: stream.write(readme)
    publication_files=[]
    for path in entries(owned):
        check(); publication_files.append({"path":path.relative_to(owned).as_posix(),"bytes":path.stat().st_size,"sha256":digest(path)})
    write_new(owned/"publication-manifest.json",{"schema_version":1,"run_id":run_id,"kind":kind,"files":publication_files,"self_excluded":True,"validation_sha256":digest(validation)})
    owned_entries=[]
    for path in entries(owned):
        check(); owned_entries.append({"path":path.relative_to(repo).as_posix(),"bytes":path.stat().st_size,"sha256":digest(path)})
    old=file(repo,".gitattributes").read_bytes(); append=b"\n"
    for row in owned_entries:
        check(); rule=" filter=lfs diff=lfs merge=lfs -text" if row["bytes"]>=10*1024**2 else " -text"
        append+=(row["path"]+rule+"\n").encode("ascii")
    after=old+append
    with file(repo,".gitattributes").open("wb") as stream: stream.write(after)
    receipt={"schema_version":1,"run_id":run_id,"kind":kind,"prefix":prefix,"files":owned_entries,"attributes_before_sha256":hashlib.sha256(old).hexdigest(),"attributes_after_sha256":hashlib.sha256(after).hexdigest(),"original_validation_sha256":digest(validation),"status":"PASS_prepared_assets_only_NOT_PUSHED"}
    write_new(state_dir/("owned-"+kind+".json"),receipt)
    with (state_dir/("stage-"+kind+".paths")).open("x",encoding="ascii",newline="\n") as stream:
        for row in owned_entries: check(); stream.write(row["path"]+"\n")
        stream.write(".gitattributes\n")
    print(json.dumps({"status":receipt["status"],"kind":kind,"files":len(owned_entries)}),flush=True)

def verify_owned():
    manifest=read(file(state_dir,"owned-"+kind+".json"))
    expected={r["path"] for r in manifest["files"]}
    actual={p.relative_to(repo).as_posix() for p in entries(repo/prefix)}
    if actual!=expected or digest(file(repo,".gitattributes"))!=manifest["attributes_after_sha256"]:
        raise ValueError("Owned path set/attributes differs")
    for row in manifest["files"]:
        check(); path=file(repo,row["path"])
        if path.stat().st_size!=row["bytes"] or digest(path)!=row["sha256"]:
            raise ValueError("Owned actual full bytes/SHA differs")
    return manifest

def verify_notices():
    if kind != "model":
        raise ValueError("Notice whitespace exception is restricted to the model run")
    manifest = verify_owned()
    copied = absolute(str(repo / prefix / "notices")); original = absolute(str(trained / "notices"))
    for directory in (copied, original):
        if not directory.is_dir():
            raise ValueError("Missing original/copied notice directory")
        names = set()
        for index, path in enumerate(directory.iterdir()):
            check()
            if index >= len(NOTICE_NAMES) or path.is_symlink() or not path.is_file():
                raise ValueError("Notice file count/type/link or nested directory rejected")
            names.add(path.name)
        if names != NOTICE_NAMES:
            raise ValueError("Notice names differ from the reviewed11 license/source snapshots")
    for name in sorted(NOTICE_NAMES):
        check(); source = file(original, name); target = file(copied, name)
        if source.stat().st_size != target.stat().st_size or digest(source) != digest(target):
            raise ValueError("Notice bytes/SHA differ from trained original: " + name)
    metadata_paths = [".gitattributes"]
    for row in manifest["files"]:
        check(); relative = row["path"].removeprefix(prefix)
        if relative.startswith("notices/"):
            if relative[8:] not in NOTICE_NAMES:
                raise ValueError("Unreviewed notice file cannot bypass whitespace gate")
            continue
        if relative in MODEL_METADATA_NAMES:
            metadata_paths.append(row["path"])
        elif relative in ("weights.safetensors", "tokenizer/tokenizer.json"):
            continue
        else:
            checkpoint = re.fullmatch(r"checkpoint-[0-9]{7}/(checkpoint\.json|weights\.safetensors|moments\.safetensors)", relative)
            if not checkpoint:
                raise ValueError("Unreviewed model run path: " + relative)
            if checkpoint[1] == "checkpoint.json":
                metadata_paths.append(row["path"])
    path = state_dir / "whitespace-model.paths"
    content = "".join(p + "\n" for p in sorted(metadata_paths))
    if path.exists():
        if path.is_symlink() or path.read_text(encoding="ascii") != content:
            raise ValueError("Existing reviewed whitespace path plan differs")
    else:
        with path.open("x", encoding="ascii", newline="\n") as stream:
            stream.write(content)
    print(json.dumps({"status":"PASS_full_owned_SHA_and_11_original_notices_metadata_whitespace_plan_only",
        "notice_files":len(NOTICE_NAMES), "metadata_files":len(metadata_paths), "path_plan":str(path)}),flush=True)

def readback():
    manifest=verify_owned(); old_count=0; old_bytes=0
    if kind=="dataset":
        asset_path=file(repo,"ASSET_MANIFEST.json")
        if digest(asset_path)!="fef8c839922ab676b9552a5fa7e0690345037e76a3f1a8ce81683a9aadd66438":
            raise ValueError("Original dataset manifest changed")
        assets=read(asset_path)
        if len(assets)!=275 or sum(r["bytes"] for r in assets)!=1076726933:
            raise ValueError("Original dataset275 identity")
        for row in assets:
            check(); path=file(repo,row["path"])
            if path.stat().st_size!=row["bytes"] or digest(path)!=row["sha256"]:
                raise ValueError("Original dataset asset full bytes/SHA")
            old_count+=1;old_bytes+=row["bytes"]
    else:
        sums=file(repo,"SHA256SUMS")
        if digest(sums)!="53d51e5904f9a8fc9940536812462339a18d71a42319b832ad7f4d576cbf39c8":
            raise ValueError("Original model checksum manifest changed")
        lines=sums.read_text(encoding="ascii").splitlines()
        if len(lines)!=17:
            raise ValueError("Original model checksum count")
        seen=set()
        for line in lines:
            check(); match=re.fullmatch(r"([a-f0-9]{64})  ([A-Za-z0-9_./-]+)",line)
            if not match or match[2] in seen or digest(file(repo,match[2]))!=match[1]:
                raise ValueError("Original model full SHA/duplicate")
            seen.add(match[2]);old_count+=1;old_bytes+=file(repo,match[2]).stat().st_size
        old_count+=1;old_bytes+=sums.stat().st_size
        if old_count!=18 or old_bytes!=1245737297:
            raise ValueError("Original18 model full bytes")
    receipt={"schema_version":1,"status":"PASS_anonymous_complete_original_and_new_owned_full_bytes_sha256","kind":kind,"run_id":run_id,"publication_commit":data_commit,"old_files":old_count,"old_bytes":old_bytes,"new_files":len(manifest["files"]),"new_bytes":sum(r["bytes"] for r in manifest["files"]),"dataset_original_pointer_status":"FAIL_two_known_original_Git_blobs_preserved" if kind=="dataset" else "not_applicable","elapsed_seconds":time.monotonic()-started}
    write_new(state_dir/("public-readback-"+kind+".json"),receipt)
    print(json.dumps(receipt),flush=True)

def mirror_state():
    if kind != "model":
        raise ValueError("Public mirror is model-only; private dataset is excluded")
    return absolute(str(root / "org-model-mirror"))

def verify_model_transport(run_directory=None):
    if kind != "model":
        raise ValueError("Transport verification is model-only")
    run = absolute(str(run_directory if run_directory is not None else repo/prefix))
    manifest = read(file(run,"transport/manifest.json"))
    package = read(file(run,"package.json"))
    updates = package["CompletedUpdates"]
    if not isinstance(updates,int) or isinstance(updates,bool) or not 2<=updates<=1000000:
        raise ValueError("Transport package update count differs")
    logical = "checkpoint-"+format(updates,"07d")+"/moments.safetensors"
    if (manifest["schema_version"] != 1 or manifest["run_id"] != run_id or package["RunId"] != run_id
        or manifest["logical_path"] != logical or not HEX.fullmatch(manifest["logical_sha256"])
        or not isinstance(manifest["logical_bytes"],int) or isinstance(manifest["logical_bytes"],bool)
        or not 1<=manifest["logical_bytes"]<=16*1024**3):
        raise ValueError("Transport logical model identity/size differs")
    if (run/logical).exists() or (run/logical).is_symlink():
        raise ValueError("Transport export retains the original logical moment; cannot upload oversized history")
    checkpoint = file(run,logical.rsplit("/",1)[0]+"/checkpoint.json")
    checkpoint_value = read(checkpoint)
    if (digest(checkpoint) != package["CheckpointReceiptSha256"]
        or checkpoint_value["RunId"] != run_id or checkpoint_value["CompletedUpdates"] != updates
        or checkpoint_value["MomentsSha256"] != manifest["logical_sha256"]):
        raise ValueError("Transport checkpoint original logical SHA binding differs")
    parts = manifest["parts"]
    if not isinstance(parts,list) or not 1<=len(parts)<=16:
        raise ValueError("Transport part count bound1..16")
    allowed = {"manifest.json","reassemble-optimizer.py"}
    for index,row in enumerate(parts):
        check(); expected="transport/moments.part-"+format(index+1,"04d")+".bin"
        if (row["path"] != expected or not isinstance(row["bytes"],int) or isinstance(row["bytes"],bool)
            or not 1<=row["bytes"]<=1024**3 or not HEX.fullmatch(row["sha256"])):
            raise ValueError("Transport ordered part identity/size/hash differs")
        allowed.add(expected.rsplit("/",1)[1])
    actual=set()
    for index,path in enumerate((run/"transport").iterdir()):
        check()
        if index>=18 or path.is_symlink() or not path.is_file():
            raise ValueError("Transport plain file count/type bound18")
        actual.add(path.name)
    if actual != allowed or sum(row["bytes"] for row in parts) != manifest["logical_bytes"]:
        raise ValueError("Transport missing/foreign parts or logical byte denominator differs")
    script=file(run,"transport/reassemble-optimizer.py")
    if script.stat().st_size > 65536:
        raise ValueError("Transport reviewed reassembly asset byte cap64KiB")
    combined=hashlib.sha256(); total=0
    for part_index,row in enumerate(parts):
        check(); path=file(run,row["path"]); part_hash=hashlib.sha256(); part_bytes=0
        if path.stat().st_size != row["bytes"]:
            raise ValueError("Transport actual part byte length differs")
        with path.open("rb") as stream:
            for chunk_index in range(257):
                check(); block=stream.read(4*1024**2)
                if not block: break
                part_hash.update(block); combined.update(block); part_bytes+=len(block); total+=len(block)
                if chunk_index%64==0:
                    print(json.dumps({"progress":"transport_stream_reassembly_sha256","part":part_index+1,"chunk":chunk_index+1}),flush=True)
            else: raise ValueError("Transport part chunk bound257")
        if part_bytes != row["bytes"] or part_hash.hexdigest() != row["sha256"]:
            raise ValueError("Transport actual part full bytes/SHA differs")
    if total != manifest["logical_bytes"] or combined.hexdigest() != manifest["logical_sha256"]:
        raise ValueError("Transport full stream reassembled logical bytes/SHA differs")
    return {"status":"PASS_transport_parts_and_stream_reassembled_logical_bytes_sha256", "run_id":run_id,
        "logical_path":logical,"logical_bytes":total,"logical_sha256":combined.hexdigest(),"parts":len(parts),
        "logical_reassembly_verified":True,"reassembly_method":"full_stream_concatenation_sha256_no_checkpoint_materialized",
        "reassembly_script_sha256":digest(script),"repository_script_executed":False}

def mirror_source_files():
    # In mirror modes, derived_arg names the verified public source model checkout.
    transport = (derived/prefix/"transport/manifest.json").exists()
    source_state = absolute(str(root/"publication-transport")) if transport else state_dir
    proof = read(file(source_state, "public-readback-model.json"))
    if (proof["status"] != "PASS_authenticated_complete_original_and_new_owned_full_bytes_sha256"
        or proof["kind"] != "model" or proof["run_id"] != run_id
        or proof["publication_commit"] != data_commit or proof["old_files"] != 18
        or proof["old_bytes"] != 1245737297 or proof.get("source_visibility") != "private"
        or proof.get("authentication") != "authorized_credentials"
        or proof.get("explicit_public_model_release_authorized") is not True):
        raise ValueError("Private model source commit/authenticated full readback/public release authorization proof absent or differs")
    sums = file(derived, "SHA256SUMS")
    if digest(sums) != "53d51e5904f9a8fc9940536812462339a18d71a42319b832ad7f4d576cbf39c8":
        raise ValueError("Public source base18 checksum identity differs")
    lines = sums.read_text(encoding="ascii").splitlines()
    if len(lines) != 17:
        raise ValueError("Public source base17 checksum entries required")
    files = [{"path":"SHA256SUMS", "bytes":sums.stat().st_size, "sha256":digest(sums)}]
    seen = {"SHA256SUMS"}
    for line in lines:
        check(); match = re.fullmatch(r"([a-f0-9]{64})  ([A-Za-z0-9_./-]+)", line)
        if not match or match[2] in seen:
            raise ValueError("Public source base duplicate/invalid checksum entry")
        path = file(derived, match[2])
        if digest(path) != match[1]:
            raise ValueError("Public source base full SHA differs")
        files.append({"path":match[2], "bytes":path.stat().st_size, "sha256":match[1]}); seen.add(match[2])
    if sum(row["bytes"] for row in files) != 1245737297:
        raise ValueError("Public source base18 full bytes differ")
    owned = read(file(source_state, "owned-model.json"))
    new_paths = {row["path"] for row in owned["files"]}
    if {p.relative_to(derived).as_posix() for p in entries(derived/prefix)} != new_paths:
        raise ValueError("Public source complete run path set differs")
    for row in owned["files"]:
        check(); relative = row["path"].removeprefix(prefix)
        if not row["path"].startswith(prefix) or not (
            relative in MODEL_METADATA_NAMES or relative in ("weights.safetensors", "tokenizer/tokenizer.json")
            or (transport and relative in ("transport/manifest.json","transport/reassemble-optimizer.py"))
            or (transport and re.fullmatch(r"transport/moments\.part-[0-9]{4}\.bin",relative))
            or (relative.startswith("notices/") and relative[8:] in NOTICE_NAMES)
            or re.fullmatch(r"checkpoint-[0-9]{7}/(checkpoint\.json|weights\.safetensors|moments\.safetensors)", relative)):
            raise ValueError("Public source run contains unreviewed file or private data")
        path = file(derived, row["path"])
        if row["path"] in seen or path.stat().st_size != row["bytes"] or digest(path) != row["sha256"]:
            raise ValueError("Public source complete new run full bytes/SHA differs")
        files.append(row); seen.add(row["path"])
    if proof["new_files"] != len(owned["files"]) or proof["new_bytes"] != sum(row["bytes"] for row in owned["files"]):
        raise ValueError("Public source readback/run manifest denominator differs")
    actual_notices = set()
    for notice_index, notice in enumerate((derived/prefix/"notices").iterdir()):
        check()
        if notice_index >= 11 or notice.is_symlink() or not notice.is_file():
            raise ValueError("Public source notice count/type/link bound11")
        actual_notices.add(notice.name)
    if actual_notices != NOTICE_NAMES:
        raise ValueError("Public source must retain all11 reviewed run notices")
    if transport:
        if proof.get("logical_reassembly_verified") is not True:
            raise ValueError("Transport public source lacks full logical reassembly proof")
        verify_model_transport(derived/prefix)
    return sorted(files, key=lambda row:row["path"])

def mirror_prepare():
    directory = mirror_state(); source_files = mirror_source_files()
    if (repo/prefix).exists():
        raise ValueError("Mirror run destination exists; inspect existing state instead of retrying")
    card_inputs=read(file(root,"org-model-card-inputs.json"))
    if card_inputs["schema_version"] != 1 or card_inputs["run_id"] != run_id:
        raise ValueError("Reviewed organization model card run identity differs")
    cards=[]
    for output_name,input_name in (("README.md","readme"),("MODEL_CARD.md","model_card")):
        check(); value=card_inputs[input_name]; path=file(root,value["path"])
        if path.stat().st_size>1024**2 or not HEX.fullmatch(value["sha256"]) or digest(path)!=value["sha256"]:
            raise ValueError("Reviewed organization model card full SHA differs")
        cards.append({"path":output_name,"bytes":path.stat().st_size,"sha256":value["sha256"],"input_path":value["path"]})
    base_mapping=[]; files=[]
    for row in source_files:
        check(); target_path={"README.md":"BASE_MODEL_README.md","SHA256SUMS":"BASE_MODEL_SHA256SUMS"}.get(row["path"],row["path"])
        mapped={**row,"path":target_path,"source_path":row["path"]}; files.append(mapped)
        if not row["path"].startswith(prefix):
            base_mapping.append({"source_path":row["path"],"target_path":target_path,"bytes":row["bytes"],"sha256":row["sha256"]})
    if len(base_mapping)!=18:
        raise ValueError("Organization base18 preservation mapping differs")
    paths = directory / "target-before.paths"
    if paths.stat().st_size > 1024**2:
        raise ValueError("Target initial tracked path byte cap1MiB")
    before_names = paths.read_text(encoding="ascii").splitlines()
    if not 1 <= len(before_names) <= 1000 or len(before_names) != len(set(before_names)):
        raise ValueError("Target initial tracked path cap1000 or duplicate")
    owned_names = {row["path"] for row in files} | {".gitattributes","README.md","MODEL_CARD.md","SHA256SUMS"}
    untouched = []
    for name in before_names:
        check()
        if name not in owned_names:
            path = file(repo, name)
            with path.open("rb") as stream:
                if stream.read(44).startswith(b"version https://git-lfs.github.com/spec/v1"):
                    raise ValueError("Untouched target LFS pointer requires separate full-content preflight")
            untouched.append({"path":name,"bytes":path.stat().st_size,"sha256":digest(path)})
    total = sum(row["bytes"] for row in files)
    lfs_unique={row["sha256"]:row["bytes"] for row in files if row["bytes"]>=10*1024**2}
    # Target LFS cache is verified and reclaimed only after successful ordinary
    # upload+push/ref proof. Anonymous readback uses a new empty independent cache.
    small_bytes=sum(row["bytes"] for row in files if row["bytes"]<10*1024**2)
    for row in files:
        check()
        if row["bytes"]>=10*1024**2 and file(derived,row["source_path"]).stat().st_dev != repo.stat().st_dev:
            raise ValueError("Mirror immutable large hardlinks require the verified source and new target on the same filesystem")
    required = small_bytes + total + sum(lfs_unique.values()) + 1024**3
    free = shutil.disk_usage(root).free
    if free < required:
        raise ValueError("Insufficient disk for mirror checkout/LFS and independent anonymous full readback: " + str(required))
    attributes = file(repo, ".gitattributes")
    if attributes.stat().st_size > 1024**2:
        raise ValueError("Mirror original attribute byte cap1MiB")
    old = attributes.read_bytes()
    # Narrow repair of the platform's historical invalid attribute token spelling.
    after = old.replace(b"diff:lfs", b"diff=lfs").replace(b"merge:lfs", b"merge=lfs") + b"\n"
    for row in files:
        check()
        if row["bytes"] >= 2*1024**3:
            raise ValueError("Public mirror physical file must remain below the platform2GiB object limit")
        if row["bytes"] >= 10*1024**2:
            rule = " filter=lfs diff=lfs merge=lfs -text\n"
        elif row["path"].endswith((".md", ".json", ".txt", ".html", ".sha256")) or row["path"] == "SHA256SUMS":
            # Set diff explicitly: -diff would hide genuine generated whitespace.
            rule = " -filter diff -merge -text\n"
        else:
            rule = " -filter -diff -merge -text\n"
        after += (row["path"] + rule).encode("ascii")
        source = file(derived,row["source_path"]); target = absolute(str(repo/row["path"]))
        target.parent.mkdir(parents=True,exist_ok=True)
        if target.exists() and (not target.is_file() or target.is_symlink()):
            raise ValueError("Mirror target file type differs")
        if row["bytes"]>=10*1024**2:
            if target.exists():target.unlink()
            os.link(source,target)
            if source.stat().st_ino!=target.stat().st_ino or source.stat().st_dev!=target.stat().st_dev:
                raise ValueError("Mirror large asset hardlink identity differs")
        else:
            with source.open("rb") as src, target.open("wb") as dst:
                for block_index in range(4097):
                    check(); block=src.read(4*1024**2)
                    if not block: break
                    dst.write(block)
                else: raise ValueError("Mirror copy chunk bound4097")
    for row in cards:
        check(); target=absolute(str(repo/row["path"]))
        target.write_bytes(file(root,row["input_path"]).read_bytes())
        files.append({"path":row["path"],"bytes":row["bytes"],"sha256":row["sha256"],"reviewed_card_input":row["input_path"]})
        after+=(row["path"]+" -filter diff -merge -text\n").encode("ascii")
    current_sums="".join(row["sha256"]+"  "+row["path"]+"\n" for row in sorted(files,key=lambda value:value["path"]))
    target_sums=absolute(str(repo/"SHA256SUMS")); target_sums.write_text(current_sums,encoding="ascii",newline="\n")
    files.append({"path":"SHA256SUMS","bytes":target_sums.stat().st_size,"sha256":digest(target_sums),"generated_for_physical_mirror_paths":True})
    after+=b"SHA256SUMS -filter diff -merge -text\n"
    attributes.write_bytes(after)
    target_base = file(directory,"target-before-commit.txt").read_text(encoding="ascii").strip()
    if not REV.fullmatch(target_base):
        raise ValueError("Mirror target initial actual commit differs")
    receipt = {"schema_version":1,"run_id":run_id,"source_model_commit":data_commit,
        "source_model_url":"https://moark.com/maikebing/Sezika-mmBERT-8192-experimental",
        "source_visibility":"private","source_readback_authentication":"authorized_credentials","explicit_public_model_release_authorized":True,
        "target_public_url":"https://moark.com/IoTSharp/Sezika-Decision-Base",
        "target_before_commit":target_base,"base_files":18,"run_files":sum(row["path"].startswith(prefix) for row in files),"files":files,
        "source_base_mapping":base_mapping,"organization_introduction_files":[row["path"] for row in cards],
        "large_asset_transport":"verified_same_filesystem_hardlinks_never_written","hardlinked_large_bytes":total-small_bytes,
        "private_dataset_included":False,"untouched_target_files":untouched,
        "attributes_before_sha256":hashlib.sha256(old).hexdigest(),"attributes_after_sha256":hashlib.sha256(after).hexdigest(),
        "required_free_bytes":required,"observed_free_bytes":free,"status":"PASS_prepared_complete_public_model_only_NOT_PUSHED"}
    write_new(directory/"owned-mirror.json",receipt)
    for name, values in (("stage.paths",sorted(owned_names)),("lfs-object-ids.paths",sorted({row["sha256"] for row in files if row["bytes"]>=10*1024**2})),
        ("whitespace.paths",sorted({".gitattributes","README.md","MODEL_CARD.md","SHA256SUMS"}|{row["path"] for row in files if row["path"].startswith(prefix) and (row["path"].removeprefix(prefix) in MODEL_METADATA_NAMES or row["path"].endswith("/checkpoint.json") or row["path"].removeprefix(prefix) in ("transport/manifest.json","transport/reassemble-optimizer.py"))}))):
        check()
        with (directory/name).open("x",encoding="ascii",newline="\n") as stream:
            stream.write("".join(value+"\n" for value in values))
    mirror_verify(False)
    print(json.dumps({"status":receipt["status"],"base_files":18,"run_files":receipt["run_files"],"full_bytes":total}),flush=True)

def mirror_verify(readback):
    directory = mirror_state(); manifest = read(file(directory,"owned-mirror.json"))
    if manifest["source_model_commit"] != data_commit or manifest["run_id"] != run_id or manifest["private_dataset_included"] or manifest["base_files"] != 18:
        raise ValueError("Mirror source/public-model-only identity differs")
    expected_run = {row["path"] for row in manifest["files"] if row["path"].startswith(prefix)}
    if {p.relative_to(repo).as_posix() for p in entries(repo/prefix)} != expected_run:
        raise ValueError("Mirror complete new run path set differs")
    if digest(file(repo,".gitattributes")) != manifest["attributes_after_sha256"]:
        raise ValueError("Mirror attributes SHA differs")
    if not 19 <= len(manifest["files"]) <= 1000 or len(manifest["untouched_target_files"]) > 1000:
        raise ValueError("Mirror file cap1000")
    for row in manifest["files"] + manifest["untouched_target_files"]:
        check(); path=file(repo,row["path"])
        if path.stat().st_size != row["bytes"] or digest(path) != row["sha256"]:
            raise ValueError("Mirror actual full bytes/SHA or untouched target differs: "+row["path"])
    transport_receipt = verify_model_transport() if (repo/prefix/"transport/manifest.json").exists() else None
    if readback:
        commit = file(directory,"publication-target-commit.txt").read_text(encoding="ascii").strip()
        if not REV.fullmatch(commit):
            raise ValueError("Mirror actual target commit absent")
        receipt = {"schema_version":1,"run_id":run_id,"status":"PASS_anonymous_complete_base18_and_new_run_full_bytes_sha256",
            "source_model_commit":data_commit,"source_visibility":"private","target_public_commit":commit,"base_files":18,"run_files":len(expected_run),
            "full_bytes":sum(row["bytes"] for row in manifest["files"]),"authentication":False,"private_dataset_included":False}
        if transport_receipt:
            receipt.update(logical_reassembly_verified=True,transport=transport_receipt)
        write_new(directory/"public-readback.json",receipt)
        print(json.dumps(receipt),flush=True)
    else:
        print("PASS_complete_base18_and_new_run_owned_and_untouched_full_bytes_sha256",flush=True)

def mirror_prune_lfs():
    directory=mirror_state(); manifest=read(file(directory,"owned-mirror.json"))
    if repo != directory/"checkout" or manifest["source_model_commit"] != data_commit:
        raise ValueError("Mirror cache cleanup is restricted to the new owned target checkout")
    target_commit=file(directory,"publication-target-commit.txt").read_text(encoding="ascii").strip()
    confirmed=file(directory,"target-after-commit.txt").read_text(encoding="ascii").strip()
    if not REV.fullmatch(target_commit) or confirmed != target_commit:
        raise ValueError("Mirror LFS cache cleanup requires actual successful target push/ref proof")
    unique={row["sha256"]:row["bytes"] for row in manifest["files"] if row["bytes"]>=10*1024**2}
    if not 1<=len(unique)<=20:
        raise ValueError("Mirror owned cache cleanup object count bound1..20")
    selected=[]
    for oid,size in sorted(unique.items()):
        check()
        if not HEX.fullmatch(oid):raise ValueError("Mirror cache OID identity")
        path=file(repo,".git/lfs/objects/"+oid[:2]+"/"+oid[2:4]+"/"+oid)
        if path.stat().st_size!=size or path.stat().st_nlink!=1 or digest(path)!=oid:
            raise ValueError("Mirror cache file bytes/SHA or exclusive ownership differs")
        selected.append(path)
    before=shutil.disk_usage(root).free
    # Delete only these verified task-created ordinary LFS cache file entries;
    # never remove directories, shared source cache, checkout assets, or receipts.
    for path in selected:
        check(); path.unlink()
    after=shutil.disk_usage(root).free
    receipt={"status":"PASS_exact_owned_target_lfs_cache_files_reclaimed_after_push","source_model_commit":data_commit,
        "target_public_commit":target_commit,"objects":len(selected),"bytes":sum(unique.values()),
        "free_before":before,"free_after":after,"checkout_model_bytes_preserved":True,"anonymous_cache_reused":False}
    write_new(directory/"lfs-cache-reclaimed.json",receipt)
    print(json.dumps(receipt),flush=True)

if mode=="prepare": prepare()
elif mode=="verify-owned": verify_owned(); print("PASS_owned_complete_full_bytes_sha256",flush=True)
elif mode=="verify-notices": verify_notices()
elif mode=="readback": readback()
elif mode=="mirror-prepare": mirror_prepare()
elif mode=="mirror-verify": mirror_verify(False)
elif mode=="mirror-readback": mirror_verify(True)
elif mode=="mirror-prune-lfs": mirror_prune_lfs()
elif mode=="transport-verify": print(json.dumps(verify_model_transport()),flush=True)
elif mode=="disk-preflight":
    source_bytes=sum(p.stat().st_size for p in entries(trained))+sum(p.stat().st_size for p in entries(derived))
    required=3*source_bytes+2*(1245737297+1076726933)+1024**3
    free=shutil.disk_usage(root).free
    if free<required: raise ValueError("Insufficient disk for publication and independent full readback")
    print(json.dumps({"status":"PASS_disk_preflight","required_bytes":required,"free_bytes":free}),flush=True)
else: raise ValueError("Unknown publication contract mode")
