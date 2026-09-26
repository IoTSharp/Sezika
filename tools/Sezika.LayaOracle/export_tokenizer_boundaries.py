"""Independent, offline tokenizer boundary oracle; never loads model weights."""
import argparse
import hashlib
import json
import time
from pathlib import Path

from tokenizers import Tokenizer, __version__


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--tokenizer", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--max-cases", type=int, default=1)
    args = parser.parse_args()
    if not 1 <= args.max_cases <= 300:
        raise ValueError("max-cases must be 1..300")
    started = time.monotonic()
    path = Path(args.tokenizer)
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if digest != "609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f":
        raise ValueError("Pinned tokenizer identity differs")
    source = json.loads(path.read_text(encoding="utf-8"))
    added = source["added_tokens"]
    if len(added) != 249:
        raise ValueError("Unexpected added-token inventory")
    texts = ["Alpha\n\nBefore Beta", "Alpha\nPolicy", "Alpha\tBeta", "Alpha\r\nBeta",
             "Alpha  Beta", "Alpha\n\n Before Beta", "中文\n\n后续", "A\n\n\n\nB",
             "A <mask>B", "A\t\n<mask>B", "<bos>Alpha<eos>", "\n\n", "\t", "  ",
             "A\u00a0<mask>B", "A\u0085<mask>B", "A\u2003<mask>B", "A\u001c<mask>B"]
    texts.extend("Alpha" + item["content"] + "Beta" for item in added)
    tokenizer = Tokenizer.from_file(str(path))
    rows = []
    for index, text in enumerate(texts[:args.max_cases]):
        if time.monotonic() - started > 30:
            raise TimeoutError("Tokenizer oracle exceeded 30 seconds")
        rows.append({"id": f"boundary-{index + 1:03}", "text": text,
                     "ids": tokenizer.encode(text).ids,
                     "without_special_tokens": tokenizer.encode(text, add_special_tokens=False).ids})
        if index == 0 or (index + 1) % 32 == 0:
            print(f"Tokenizer oracle {index + 1}/{min(len(texts), args.max_cases)}", flush=True)
    if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
        raise ValueError("Tokenizer changed during capture")
    report = {"schema_version": "sezika.tokenizer-boundaries.v1", "tokenizer_sha256": digest,
              "reference": f"tokenizers {__version__}", "authorship": "Original minimal regression text; Apache-2.0",
              "planned": len(texts), "processed": len(rows), "cases": rows}
    with Path(args.output).open("x", encoding="utf-8", newline="\n") as output:
        json.dump(report, output, ensure_ascii=False, indent=2)
        output.write("\n")


if __name__ == "__main__":
    main()
