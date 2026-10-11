using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sezika;
using Sezika.Cuda;

namespace Sezika.RepositoryTrainer;

// Bounded streaming F32 safetensors. Checkpoint moments never coexist in host memory.
internal sealed class TensorFile:IDisposable
{
    private readonly FileStream _stream;
    private readonly Dictionary<string,Descriptor> _descriptors=new(StringComparer.Ordinal);
    private readonly long _payload;
    private sealed record Descriptor(int[] Shape,int Count,long Offset);
    internal static string Write(string path,IReadOnlyList<FullEncoderParameter> parameters,
        Func<FullEncoderParameter,float[]> reader,RunBudget budget)
    {
        if(!BitConverter.IsLittleEndian||parameters.Count is <1 or >405)throw new InvalidDataException("F32 tensor platform/count bound.");
        using var headerStream=new MemoryStream();long offset=0;
        using(var json=new Utf8JsonWriter(headerStream))
        {
            json.WriteStartObject();foreach(var p in parameters)
            {
                budget.Check();json.WritePropertyName(p.Name);json.WriteStartObject();json.WriteString("dtype","F32");json.WriteStartArray("shape");foreach(var size in p.Shape)json.WriteNumberValue(size);json.WriteEndArray();
                json.WriteStartArray("data_offsets");json.WriteNumberValue(offset);offset=checked(offset+(long)p.Elements*4);json.WriteNumberValue(offset);json.WriteEndArray();json.WriteEndObject();
            }
            json.WriteEndObject();
        }
        if(offset>8L*1024*1024*1024||headerStream.Length>1048576)throw new InvalidDataException("Tensor file byte bound.");
        var header=headerStream.ToArray();var padding=(8-header.Length%8)%8;
        using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,1048576))
        {
            Span<byte> length=stackalloc byte[8];BinaryPrimitives.WriteUInt64LittleEndian(length,(ulong)(header.Length+padding));stream.Write(length);stream.Write(header);for(var i=0;i<padding;i++)stream.WriteByte(32);
            foreach(var p in parameters)
            {
                budget.Check();var values=reader(p);
                try
                {
                    if(values.Length!=p.Elements)throw new InvalidDataException("Tensor shape changed during export.");
                    for(var start=0;start<values.Length;start+=262144)
                    {
                        budget.Check();var chunk=values.AsSpan(start,Math.Min(262144,values.Length-start));
                        foreach(var v in chunk)if(!float.IsFinite(v))throw new ArithmeticException("Non-finite exported tensor.");stream.Write(MemoryMarshal.AsBytes(chunk));
                    }
                }
                finally{Array.Clear(values);}
            }
            stream.Flush(true);
        }
        return Data.FileHash(path,budget);
    }
    internal TensorFile(string path,string expectedHash,RunBudget budget)
    {
        if(!BitConverter.IsLittleEndian||expectedHash.Length!=64||Data.FileHash(path,budget)!=expectedHash)throw new InvalidDataException("Tensor file identity mismatch.");
        _stream=new(path,FileMode.Open,FileAccess.Read,FileShare.Read,1048576);
        try
        {
            Span<byte> length=stackalloc byte[8];_stream.ReadExactly(length);var size=BinaryPrimitives.ReadUInt64LittleEndian(length);
            if(size is <1 or >1048576||size>(ulong)(_stream.Length-8))throw new InvalidDataException("Tensor header byte bound.");
            var bytes=new byte[(int)size];_stream.ReadExactly(bytes);_payload=8+(long)size;long end=0;
            using var document=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=8});
            foreach(var p in document.RootElement.EnumerateObject())
            {
                budget.Check();if(_descriptors.Count>=405||p.Value.GetProperty("dtype").GetString()!="F32")throw new InvalidDataException("Tensor count/dtype bound.");
                var shape=p.Value.GetProperty("shape").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
                if(shape.Length is <1 or >2||shape.Any(v=>v<=0))throw new InvalidDataException("Tensor rank/shape invalid.");
                var count=shape.Aggregate(1L,(a,b)=>checked(a*b));if(count>200000000)throw new InvalidDataException("Tensor element bound.");
                var offsets=p.Value.GetProperty("data_offsets").EnumerateArray().Select(v=>v.GetInt64()).ToArray();
                if(offsets.Length!=2||offsets[0]!=end||offsets[1]-offsets[0]!=count*4)throw new InvalidDataException("Tensor offsets must cover the complete payload in header order.");
                _descriptors.Add(p.Name,new(shape,(int)count,offsets[0]));end=offsets[1];
            }
            if(end+_payload!=_stream.Length||_descriptors.Count==0)throw new InvalidDataException("Tensor holes, trailing bytes or empty payload.");
        }
        catch{_stream.Dispose();throw;}
    }
    internal void RequireNames(IEnumerable<string> names)
    {if(!_descriptors.Keys.Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal)))throw new InvalidDataException("Exact tensor set differs.");}
    internal float[] Read(string name,int[] shape,RunBudget budget)
    {
        if(!_descriptors.TryGetValue(name,out var descriptor)||!descriptor.Shape.SequenceEqual(shape))throw new InvalidDataException("Tensor name/shape contract differs: "+name);
        var values=new float[descriptor.Count];_stream.Position=_payload+descriptor.Offset;
        try
        {
            for(var start=0;start<values.Length;start+=262144)
            {
                budget.Check();var chunk=values.AsSpan(start,Math.Min(262144,values.Length-start));_stream.ReadExactly(MemoryMarshal.AsBytes(chunk));
                foreach(var v in chunk)if(!float.IsFinite(v))throw new InvalidDataException("Non-finite saved tensor.");
            }
            return values;
        }
        catch{Array.Clear(values);throw;}
    }
    public void Dispose()=>_stream.Dispose();
}
