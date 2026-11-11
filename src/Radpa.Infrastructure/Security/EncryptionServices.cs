using System.Security.Cryptography;
using System.Text;
using Radpa.Application.Abstractions;

namespace Radpa.Infrastructure.Security;

public sealed class DpapiKeyProtector : IKeyProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Radpa.LocalEncryption.v1");

    public byte[] Protect(ReadOnlySpan<byte> plaintext) =>
        ProtectedData.Protect(plaintext.ToArray(), Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData) =>
        ProtectedData.Unprotect(protectedData.ToArray(), Entropy, DataProtectionScope.CurrentUser);
}

/// <summary>
/// AES-256-GCM field-level encryption for sensitive strings (window titles, remote IPs, domains)
/// with the symmetric data key protected per current Windows user via DPAPI.
/// </summary>
public sealed class AesGcmEncryptionService : IEncryptionService
{
    private const string Prefix = "ENC1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IKeyProtector _protector;
    private readonly Lock _sync = new();
    private byte[]? _key;
    private bool _enabled;

    public AesGcmEncryptionService(IKeyProtector protector)
    {
        _protector = protector;
    }

    public bool IsEnabled
    {
        get
        {
            lock (_sync)
            {
                return _enabled && _key is not null;
            }
        }
    }

    public void Configure(bool enabled, byte[]? protectedKeyBlob)
    {
        lock (_sync)
        {
            _enabled = enabled;
            _key = enabled && protectedKeyBlob is { Length: > 0 }
                ? _protector.Unprotect(protectedKeyBlob)
                : null;
        }
    }

    public byte[] GenerateProtectedKeyBlob()
    {
        Span<byte> rawKey = stackalloc byte[32];
        RandomNumberGenerator.Fill(rawKey);
        var blob = _protector.Protect(rawKey);
        CryptographicOperations.ZeroMemory(rawKey);
        return blob;
    }

    public string? EncryptField(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return plaintext;
        }

        if (plaintext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return plaintext;
        }

        byte[]? key;
        lock (_sync)
        {
            if (!_enabled || _key is null)
            {
                return plaintext;
            }

            key = _key;
        }

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var payload = new byte[NonceSize + TagSize + plainBytes.Length];
        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize, plainBytes.Length);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        return Prefix + Convert.ToBase64String(payload);
    }

    public string? DecryptField(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext) || !ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return ciphertext;
        }

        byte[]? key;
        lock (_sync)
        {
            key = _key;
        }

        if (key is null)
        {
            return "[Encrypted]";
        }

        var payload = Convert.FromBase64String(ciphertext[Prefix.Length..]);
        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Invalid encrypted payload length.");
        }

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        var plainBytes = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
