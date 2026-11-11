using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PersonalFinances.Core;

// v1: fixed 64-byte authenticated header, Brotli(JSON) ciphertext, 16-byte GCM tag.
// Header: magic[8], format uint32, PBKDF2 iterations uint32, salt[32], nonce[12], ciphertext length uint32.
internal static class BackupFile
{
    private const int HeaderLength=64,TagLength=16,Iterations=600_000,MaxFile=64*1024*1024,MaxJson=128*1024*1024;
    private static readonly byte[] Magic="PFINBKP\0"u8.ToArray();
    internal static void Password(string password,bool exporting=false)
    {
        if(string.IsNullOrEmpty(password)||password.Length>1024||(exporting&&password.Length<12))
            throw new RuleException(exporting?"Usá una contraseña de entre 12 y 1.024 caracteres para proteger el respaldo.":"Ingresá la contraseña del respaldo (hasta 1.024 caracteres).");
    }
    internal static async Task<BackupDocument> ReadAsync(string path,string password)
    {
        Password(password);
        await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous|FileOptions.SequentialScan);
        if(file.Length<HeaderLength+TagLength||file.Length>MaxFile)
            throw new RuleException("El archivo de respaldo está incompleto o supera el límite de 64 MB.");
        var data=new byte[(int)file.Length];await file.ReadExactlyAsync(data);
        return await Task.Run(()=>Decode(data,password));
    }
    private static BackupDocument Decode(byte[] data,string password)
    {
        var header=data.AsSpan(0,HeaderLength);
        if(!header[..8].SequenceEqual(Magic))throw new RuleException("Este archivo no es un respaldo .pfinance válido.");
        if(BinaryPrimitives.ReadUInt32LittleEndian(header[8..])!=1)throw new RuleException("La versión de este respaldo no es compatible. Actualizá Personal Finances.");
        if(BinaryPrimitives.ReadUInt32LittleEndian(header[12..])!=Iterations||BinaryPrimitives.ReadUInt32LittleEndian(header[60..])!=data.Length-HeaderLength-TagLength)
            throw new RuleException("El respaldo está dañado: cabecera o longitud incorrectas.");
        var key=Rfc2898DeriveBytes.Pbkdf2(password,header.Slice(16,32),Iterations,HashAlgorithmName.SHA256,32);
        var compressed=new byte[data.Length-HeaderLength-TagLength];
        byte[]? plain=null;
        try
        {
            using(var aes=new AesGcm(key,TagLength))aes.Decrypt(header.Slice(48,12),data.AsSpan(HeaderLength,compressed.Length),data.AsSpan(data.Length-TagLength),compressed,header);
            using var input=new MemoryStream(compressed,false);using var brotli=new BrotliStream(input,CompressionMode.Decompress);using var output=new MemoryStream();
            var buffer=new byte[81920];try{int read;while((read=brotli.Read(buffer))>0){if(output.Length+read>MaxJson)throw new RuleException("El contenido descomprimido excede el límite permitido.");output.Write(buffer,0,read);}}finally{CryptographicOperations.ZeroMemory(buffer);}
            plain=output.ToArray();
            if(output.TryGetBuffer(out var segment))CryptographicOperations.ZeroMemory(segment.AsSpan());
            using(var json=JsonDocument.Parse(plain))
            {
                var e=json.RootElement;
                if(e.TryGetProperty("BackupFormatVersion",out var v)&&v.GetInt32() is 2 or 3)
                    foreach(var collectionName in new[]{"Services","ServiceObligations","ServicePayments"})
                        if(!e.TryGetProperty(collectionName,out var collection)||collection.ValueKind!=JsonValueKind.Array)throw new RuleException("Faltan colecciones de servicios en el respaldo.");
            }
            return JsonSerializer.Deserialize<BackupDocument>(plain,BackupValidation.Json)??throw new RuleException("El respaldo no contiene datos válidos.");
        }
        catch(AuthenticationTagMismatchException){throw new RuleException("La contraseña es incorrecta o el respaldo fue alterado. No se modificó la base de datos.");}
        catch(Exception e) when(e is JsonException or InvalidDataException){throw new RuleException("El respaldo está dañado o su contenido no es compatible.");}
        finally{CryptographicOperations.ZeroMemory(key);CryptographicOperations.ZeroMemory(compressed);if(plain!=null)CryptographicOperations.ZeroMemory(plain);}
    }
    private static byte[] Encode(BackupDocument document,string password)
    {
        var plain=JsonSerializer.SerializeToUtf8Bytes(document,BackupValidation.Json);byte[]? key=null;byte[]? compressed=null;
        try
        {
            if(plain.Length>MaxJson)throw new RuleException("El respaldo excede el límite de 128 MB de datos sin comprimir.");
            using var output=new MemoryStream();using(var brotli=new BrotliStream(output,CompressionLevel.Optimal,true))brotli.Write(plain);
            compressed=output.ToArray();
            if(output.TryGetBuffer(out var segment))CryptographicOperations.ZeroMemory(segment.AsSpan());
            if(compressed.Length>MaxFile-HeaderLength-TagLength)throw new RuleException("El respaldo comprimido excede el límite de 64 MB.");
            var result=new byte[HeaderLength+compressed.Length+TagLength];var header=result.AsSpan(0,HeaderLength);
            Magic.CopyTo(header);BinaryPrimitives.WriteUInt32LittleEndian(header[8..],1);BinaryPrimitives.WriteUInt32LittleEndian(header[12..],Iterations);
            RandomNumberGenerator.Fill(header.Slice(16,32));RandomNumberGenerator.Fill(header.Slice(48,12));BinaryPrimitives.WriteUInt32LittleEndian(header[60..],(uint)compressed.Length);
            key=Rfc2898DeriveBytes.Pbkdf2(password,header.Slice(16,32),Iterations,HashAlgorithmName.SHA256,32);
            using var aes=new AesGcm(key,TagLength);aes.Encrypt(header.Slice(48,12),compressed,result.AsSpan(HeaderLength,compressed.Length),result.AsSpan(result.Length-TagLength),header);
            return result;
        }
        finally{CryptographicOperations.ZeroMemory(plain);if(key!=null)CryptographicOperations.ZeroMemory(key);if(compressed!=null)CryptographicOperations.ZeroMemory(compressed);}
    }
    internal static async Task WriteVerifiedAsync(string path,BackupDocument document,string password)
    {
        Password(password,true);var data=await Task.Run(()=>Encode(document,password));
        var full=Path.GetFullPath(path);var temp=Path.Combine(Path.GetDirectoryName(full)!,".pfinance-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            await using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous|FileOptions.WriteThrough))
            {await file.WriteAsync(data);await file.FlushAsync();file.Flush(true);}
            var verified=await ReadAsync(temp,password);
            if(!await Task.Run(()=>BackupValidation.CanonicalJson(JsonSerializer.Serialize(verified,BackupValidation.Json))==BackupValidation.CanonicalJson(JsonSerializer.Serialize(document,BackupValidation.Json))))
                throw new RuleException("No se pudo verificar la copia escrita. Se conservó el archivo anterior.");
            if(File.Exists(full))File.Replace(temp,full,null);else File.Move(temp,full);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
