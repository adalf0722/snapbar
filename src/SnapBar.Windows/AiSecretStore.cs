using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SnapBar.Core;

namespace SnapBar.Windows;

public sealed class AiSecretStore(string directory)
{
    private readonly string directory = Path.GetFullPath(directory);
    private const int MaximumFile = 64 * 1024;
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public nint Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, nint entropy, nint reserved, nint prompt, uint flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);

    private string FilePath(AiProviderProfile profile)
    {
        if (AiPolicy.ValidateProfile(profile, false) != null) throw new ArgumentException("Invalid provider profile.");
        var scope = profile.Id + "\n" + (int)profile.Protocol + "\n" + new Uri(profile.Endpoint).AbsoluteUri;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(scope));
        return Path.Combine(directory, Convert.ToHexStringLower(hash) + ".key");
    }

    public string? Get(AiProviderProfile profile)
    {
        byte[]? clear = null;
        try
        {
            using var stream = new FileStream(FilePath(profile), FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > MaximumFile) return null;
            var encrypted = new byte[(int)stream.Length];
            stream.ReadExactly(encrypted);
            clear = Transform(encrypted, protect: false);
            if (clear.Length > 8192 * 4) return null;
            var value = new UTF8Encoding(false, true).GetString(clear);
            return value.Length is > 0 and <= 8192 ? value : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException or ExternalException) { return null; }
        finally { if (clear != null) CryptographicOperations.ZeroMemory(clear); }
    }

    public void Set(AiProviderProfile profile, string? value)
    {
        if (value?.Length > 8192) throw new ArgumentException("API key exceeds 8192 characters.");
        var path = FilePath(profile);
        try
        {
            if (string.IsNullOrEmpty(value)) { File.Delete(path); return; }
            var clear = Encoding.UTF8.GetBytes(value);
            byte[] encrypted;
            try { encrypted = Transform(clear, protect: true); }
            finally { CryptographicOperations.ZeroMemory(clear); }
            if (encrypted.Length > MaximumFile) throw new CryptographicException();
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, ".key-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(encrypted); stream.Flush(true); }
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or ExternalException)
        { throw new InvalidOperationException("Unable to save the API key."); }
    }

    private static byte[] Transform(byte[] value, bool protect)
    {
        var input = new DataBlob { Length = value.Length, Data = Marshal.AllocHGlobal(value.Length) };
        var output = new DataBlob();
        try
        {
            Marshal.Copy(value, 0, input.Data, value.Length);
            var success = protect ? CryptProtectData(ref input, null, 0, 0, 0, 1, out output)
                : CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!success || output.Length < 0 || output.Length > MaximumFile) throw new CryptographicException("Secret protection failed.");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != 0)
            {
                for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
        }
    }
}
