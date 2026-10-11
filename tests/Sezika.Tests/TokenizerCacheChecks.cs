using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Sezika;

namespace Sezika.Tests;

internal static class TokenizerCacheChecks
{
    internal static void RunPinnedAsset(string path,string sha,Action<bool,string> check)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        if(new FileInfo(path).Length>64*1024*1024||sha.Length!=64)throw new InvalidDataException("Pinned tokenizer cache fixture identity/bounds.");
        using(var stream=File.OpenRead(path))
            check(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()==sha,"real tokenizer cache asset pinned SHA");
        var reference=new TokenizerJson(path,deadline.Token);var cached=new TokenizerJson(path,32768,deadline.Token);
        using var fixture=JsonDocument.Parse(File.ReadAllBytes("tests/fixtures/tokenizer-mmbert-boundaries.v1.json"));
        var cases=fixture.RootElement.GetProperty("cases");
        if(cases.GetArrayLength() is <1 or >512)throw new InvalidDataException("Real tokenizer cache case bound512.");
        for(var round=0;round<2;round++)
            foreach(var item in cases.EnumerateArray())
            {
                deadline.Token.ThrowIfCancellationRequested();var text=item.GetProperty("text").GetString()!;
                foreach(var special in new[]{true,false})
                {
                    var expected=reference.Encode(text,8192,special,deadline.Token);
                    if(!cached.Encode(text,8192,special,deadline.Token).SequenceEqual(expected))throw new ArithmeticException("Real tokenizer cached IDs differ.");
                }
            }
        check(true,"real tokenizer exact cache parity across multilingual and added-token boundaries in two rounds");
    }

    internal static void Run(Action<bool,string> check,Action<Action,string,string> expectCode)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var directory=Path.Combine(Path.GetTempPath(),"sezika-tokenizer-cache-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);var path=Path.Combine(directory,"tokenizer.json");
        try
        {
            var json=new StringBuilder("{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true,\"fuse_unk\":true,\"unk_token\":\"<unk>\",\"vocab\":{\"<bos>\":1,\"<eos>\":2,\"<unk>\":3,\"<mask>\":4,\"a\":5,\"b\":6,\"ab\":7,\"▁\":8,\"▁ab\":9");
            for(var value=0;value<256;value++)json.Append($",\"<0x{value:X2}>\":{1000+value}");
            json.Append("},\"merges\":[\"a b\",\"▁ ab\"]},\"added_tokens\":[{\"content\":\"<mask>\",\"id\":4,\"single_word\":false,\"normalized\":false,\"rstrip\":false,\"lstrip\":true}]}");
            File.WriteAllText(path,json.ToString(),new UTF8Encoding(false));
            var reference=new TokenizerJson(path,deadline.Token);var cached=new TokenizerJson(path,2,deadline.Token);
            string[] cases=["ab ab","ab  <mask>ab","中文 ab","ab\n ab","🙂 ab","z y x ab",new('a',300)];
            var expected=cases.Select(text=>reference.Encode(text,4096,cancellationToken:deadline.Token)).ToArray();
            check(expected[0].SequenceEqual(new[]{1,9,9,2}),"BPE merge reference IDs for cache checks");
            for(var round=0;round<4;round++)
                for(var index=0;index<cases.Length;index++)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    check(cached.Encode(cases[index],4096,cancellationToken:deadline.Token).SequenceEqual(expected[index]),"bounded tokenizer cache preserves exact IDs after hits/eviction");
                }
            expectCode(()=>cached.Encode("ab ab",3,cancellationToken:deadline.Token),"decision_token_limit_exceeded","cached fragments retain strict token limit");
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();var rejected=false;
            try{cached.Encode("ab ab",4096,cancellationToken:cancelled.Token);}catch(OperationCanceledException){rejected=true;}
            check(rejected,"cached tokenizer preserves cancellation");
            Parallel.For(0,32,new(){MaxDegreeOfParallelism=2,CancellationToken=deadline.Token},index=>
            {
                var item=index%cases.Length;
                if(!cached.Encode(cases[item],4096,cancellationToken:deadline.Token).SequenceEqual(expected[item]))throw new ArithmeticException("Shared bounded tokenizer cache changed IDs.");
            });
            check(true,"shared tokenizer cache preserves IDs under bounded concurrency");
            rejected=false;try{_ =new TokenizerJson(path,65537,deadline.Token);}catch(ArgumentOutOfRangeException){rejected=true;}
            check(rejected,"tokenizer cache rejects unbounded capacity");
        }
        finally
        {
            var absolute=Path.GetFullPath(directory);var prefix=Path.GetFullPath(Path.GetTempPath());
            if(!absolute.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(absolute).StartsWith("sezika-tokenizer-cache-",StringComparison.Ordinal))throw new IOException("Tokenizer fixture ownership differs.");
            if(File.Exists(path))File.Delete(path);Directory.Delete(absolute,false);
        }
    }
}
