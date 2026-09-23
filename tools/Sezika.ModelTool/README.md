# Sezika.ModelTool

This build-time utility manages the pinned Laya multilingual model package. It does not load
Python checkpoints, execute model code, or form part of the Native AOT runtime.

```powershell
dotnet run --project tools/Sezika.ModelTool -- download --timeout-seconds 1200
dotnet run --project tools/Sezika.ModelTool -- manifest --timeout-seconds 1200
dotnet run --project tools/Sezika.ModelTool -- verify --timeout-seconds 1200
```

The model and tokenizer revisions and expected hashes are constants in `Program.cs`. Downloads
resume a partial file once and make at most one proxy retry through `127.0.0.1:7890`. Generated
files remain under `.artifacts/models/laya-mmbert`, which is ignored by Git.
