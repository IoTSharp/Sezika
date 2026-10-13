using System.Text.Json.Serialization;

internal sealed record TensorEntry(string Name, string File, string Sha256, string Dtype, int[] Shape, ulong[] Offsets);
internal sealed record LayerEntry(int Index, string Qkv, string AttentionOutput, string? AttentionNorm, string MlpInput, string MlpOutput, string MlpNorm);
internal sealed record TokenizerEntry(string Pipeline, string Normalizer, string PreTokenizer, string Model, string PostProcessor);
internal sealed record EncoderEntry(int VocabSize, int HiddenSize, int IntermediateSize, int LayerCount, int HeadCount, int MaxTokens, int GlobalAttentionEvery, int LocalAttention, int GlobalRopeTheta, int LocalRopeTheta, double NormEpsilon);
internal sealed record HeadEntry(int HiddenSize, int Layers, int IntermediateSize, int MaxTokens);
internal sealed record CalibrationEntry(string Status, double[] Temperature);
internal sealed record TensorMappingEntry(string Embedding, string EmbeddingNorm, string FinalNorm, LayerEntry[] Layers);
internal sealed record ModelManifest(int SchemaVersion, string ModelId, string Revision, string Architecture, string License, string SourceUrl, string SourceRevision, string TokenizerRevision, string TokenizerFile, string WeightsFile, string WeightsSha256, string TokenizerSha256, string Dtype, string TensorLayout, TensorEntry[] Tensors, TokenizerEntry Tokenizer, Dictionary<string, int> SpecialTokens, EncoderEntry Encoder, HeadEntry Head, CalibrationEntry Calibration, TensorMappingEntry TensorMapping);
internal sealed record ModelSource(string Id, string Revision, string License, string SourceUrl, string WeightsSha256, string TokenizerSha256);
internal sealed record EncoderSource(string Id, string Revision, string License, string SourceUrl);
internal sealed record ModelSourceLock(ModelSource Model, EncoderSource BaseEncoder, Dictionary<string, string> Files, string GeneratedBy);

[JsonSerializable(typeof(ModelManifest))]
[JsonSerializable(typeof(ModelSourceLock))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal partial class ManifestJson : JsonSerializerContext;
