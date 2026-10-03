namespace Sezika;

/// <summary>
/// Reusable rotary-position factors for one encoder configuration.
/// ModernBERT applies the same position/frequency factors to every attention
/// head and every layer, so rebuilding trigonometric values inside each
/// forward pass only adds deterministic work. The tables are immutable and
/// bounded by the model's validated maximum sequence length.
/// </summary>
internal sealed class RopeTables : IDisposable
{
    private int _disposed;
    private RopeTables(int halfDimension, int maxTokens, float[] globalSin, float[] globalCos,
        float[] localSin, float[] localCos)
    {
        HalfDimension = halfDimension;
        MaxTokens = maxTokens;
        GlobalSin = globalSin;
        GlobalCos = globalCos;
        LocalSin = localSin;
        LocalCos = localCos;
    }

    public int HalfDimension { get; }
    public int MaxTokens { get; }
    public float[] GlobalSin { get; }
    public float[] GlobalCos { get; }
    public float[] LocalSin { get; }
    public float[] LocalCos { get; }
    public long StorageBytes => Volatile.Read(ref _disposed) == 0 ? checked((long)GlobalSin.Length * sizeof(float) * 4) : 0;

    public static RopeTables Create(ModernBertConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        var halfDimension = checked(config.HiddenSize / config.HeadCount / 2);
        var length = checked(config.MaxTokens * halfDimension);
        var globalSin = new float[length];
        var globalCos = new float[length];
        var localSin = new float[length];
        var localCos = new float[length];
        for (var row = 0; row < config.MaxTokens; row++)
        {
            if ((row & 31) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (var pair = 0; pair < halfDimension; pair++)
            {
                var angleScale = MathF.Pow(config.GlobalRopeTheta, -2f * pair / (halfDimension * 2));
                var globalAngle = row * angleScale;
                var globalIndex = row * halfDimension + pair;
                globalSin[globalIndex] = MathF.Sin(globalAngle);
                globalCos[globalIndex] = MathF.Cos(globalAngle);

                angleScale = MathF.Pow(config.LocalRopeTheta, -2f * pair / (halfDimension * 2));
                var localAngle = row * angleScale;
                localSin[globalIndex] = MathF.Sin(localAngle);
                localCos[globalIndex] = MathF.Cos(localAngle);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new RopeTables(halfDimension, config.MaxTokens, globalSin, globalCos, localSin, localCos);
    }

    public void Apply(float[] query, float[] key, int rows, int width, int heads, bool global,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (rows < 1 || rows > MaxTokens || width < 1 || heads < 1 || width % heads != 0 ||
            width / heads / 2 != HalfDimension || query.Length != checked(rows * width) ||
            key.Length != query.Length)
        {
            throw new ArgumentException("RoPE table dimensions do not match the encoder request.");
        }

        var sin = global ? GlobalSin : LocalSin;
        var cos = global ? GlobalCos : LocalCos;
        var dimension = width / heads;
        for (var row = 0; row < rows; row++)
        {
            if ((row & 31) == 0) cancellationToken.ThrowIfCancellationRequested();
            var tableOffset = row * HalfDimension;
            for (var head = 0; head < heads; head++)
            {
                var headOffset = row * width + head * dimension;
                for (var pair = 0; pair < HalfDimension; pair++)
                {
                    var a = headOffset + pair;
                    var b = a + HalfDimension;
                    var sine = sin[tableOffset + pair];
                    var cosine = cos[tableOffset + pair];
                    var queryA = query[a];
                    var queryB = query[b];
                    var keyA = key[a];
                    var keyB = key[b];
                    query[a] = queryA * cosine - queryB * sine;
                    query[b] = queryB * cosine + queryA * sine;
                    key[a] = keyA * cosine - keyB * sine;
                    key[b] = keyB * cosine + keyA * sine;
                }
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Array.Clear(GlobalSin);
        Array.Clear(GlobalCos);
        Array.Clear(LocalSin);
        Array.Clear(LocalCos);
    }
}
