using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

internal static class BackupEnvelopeCrypto
{
    private static readonly byte[] Magic = "DDCBACKUP"u8.ToArray();

    private const byte KdfPbkdf2Sha256 = 1;
    private const byte EncryptionAes256CbcPkcs7 = 1;
    private const byte AuthenticationHmacSha256 = 1;
    private const int HeaderPrefixLength = 33;
    private const int KeyMaterialLength = 64;
    private const int EncryptionKeyLength = 32;
    private const int MacKeyLength = 32;
    private const int BlockLength = 16;
    private const int MacLength = 32;

    public const int FormatVersion = 1;
    public const int KdfIterations = 600_000;
    public const int SaltLength = 16;
    public const int IvLength = 16;

    public static async Task EncryptAsync(
        string plaintextArchivePath,
        Stream destination,
        string password,
        CancellationToken cancellationToken)
    {
        ValidatePassword(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextArchivePath);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanSeek || !destination.CanWrite)
            throw new ArgumentException("The encrypted destination must be seekable and writable.", nameof(destination));
        if (destination.Position != 0)
            throw new ArgumentException("The encrypted destination must be empty.", nameof(destination));

        var plaintextFile = new FileInfo(plaintextArchivePath);
        if (!plaintextFile.Exists)
            throw new FileNotFoundException("The plaintext backup archive was not created.", plaintextArchivePath);

        long ciphertextLength = GetCiphertextLength(plaintextFile.Length);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        byte[] iv = RandomNumberGenerator.GetBytes(IvLength);
        byte[] header = BuildHeader(ciphertextLength, salt, iv);
        (byte[] encryptionKey, byte[] macKey) = DeriveKeys(password, salt, KdfIterations);

        try
        {
            await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await using (FileStream source = new(
                plaintextArchivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true))
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encryptionKey;
                aes.IV = iv;

                using (var encryptor = aes.CreateEncryptor())
                using (var cryptoStream = new CryptoStream(
                           destination,
                           encryptor,
                           CryptoStreamMode.Write,
                           leaveOpen: true))
                {
                    byte[] buffer = new byte[81920];
                    int bytesRead;
                    while ((bytesRead = await source
                               .ReadAsync(buffer.AsMemory(), cancellationToken)
                               .ConfigureAwait(false)) > 0)
                    {
                        await cryptoStream
                            .WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                            .ConfigureAwait(false);
                    }

                    cryptoStream.FlushFinalBlock();
                }
            }

            long expectedCiphertextEnd = checked(header.Length + ciphertextLength);
            if (destination.Position != expectedCiphertextEnd)
            {
                throw new InvalidDataException(
                    "The encrypted backup ciphertext length did not match the envelope header.");
            }

            await AppendHmacAsync(
                    destination,
                    expectedCiphertextEnd,
                    macKey,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(macKey);
        }
    }

    public static async Task VerifyAsync(
        string encryptedArchivePath,
        string password,
        CancellationToken cancellationToken)
    {
        ValidatePassword(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedArchivePath);

        await using FileStream source = new(
            encryptedArchivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        EnvelopeHeader header = await ReadHeaderAsync(source, cancellationToken).ConfigureAwait(false);
        long authenticatedLength = checked(header.HeaderLength + header.CiphertextLength);
        long expectedFileLength = checked(authenticatedLength + MacLength);
        if (source.Length != expectedFileLength)
            throw new InvalidDataException("The encrypted backup envelope length is invalid.");

        (byte[] encryptionKey, byte[] macKey) = DeriveKeys(
            password,
            header.Salt,
            header.KdfIterations);
        try
        {
            byte[] actualMac = await ComputeHmacAsync(
                    source,
                    authenticatedLength,
                    macKey,
                    cancellationToken)
                .ConfigureAwait(false);
            byte[] expectedMac = new byte[MacLength];
            try
            {
                source.Position = authenticatedLength;
                await ReadExactlyAsync(source, expectedMac, cancellationToken).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(actualMac, expectedMac))
                    throw new CryptographicException("The encrypted backup authentication failed.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actualMac);
                CryptographicOperations.ZeroMemory(expectedMac);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(macKey);
        }
    }

    /// <summary>
    /// Authenticates an encrypted backup before decrypting any payload bytes.
    /// The plaintext destination is created only after the envelope HMAC has
    /// been verified, so callers can safely use this method during restore
    /// preflight without exposing unauthenticated ZIP data to later stages.
    /// </summary>
    public static async Task VerifyAndDecryptAsync(
        string encryptedArchivePath,
        string plaintextOutputPath,
        string password,
        CancellationToken cancellationToken)
    {
        ValidatePassword(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedArchivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextOutputPath);

        bool outputCreated = false;
        try
        {
            await using FileStream source = new(
                encryptedArchivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);
            EnvelopeHeader header = await ReadHeaderAsync(source, cancellationToken)
                .ConfigureAwait(false);
            long authenticatedLength = checked(header.HeaderLength + header.CiphertextLength);
            long expectedFileLength = checked(authenticatedLength + MacLength);
            if (source.Length != expectedFileLength)
                throw new InvalidDataException("The encrypted backup envelope length is invalid.");

            (byte[] encryptionKey, byte[] macKey) = DeriveKeys(
                password,
                header.Salt,
                header.KdfIterations);
            try
            {
                // Authenticate the header and ciphertext before opening the
                // decrypting stream or creating the plaintext destination.
                byte[] actualMac = await ComputeHmacAsync(
                        source,
                        authenticatedLength,
                        macKey,
                        cancellationToken)
                    .ConfigureAwait(false);
                byte[] expectedMac = new byte[MacLength];
                try
                {
                    source.Position = authenticatedLength;
                    await ReadExactlyAsync(source, expectedMac, cancellationToken)
                        .ConfigureAwait(false);
                    if (!CryptographicOperations.FixedTimeEquals(actualMac, expectedMac))
                    {
                        throw new CryptographicException(
                            "The encrypted backup authentication failed.");
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(actualMac);
                    CryptographicOperations.ZeroMemory(expectedMac);
                }

                string? outputDirectory = Path.GetDirectoryName(
                    Path.GetFullPath(plaintextOutputPath));
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                await using FileStream destination = new(
                    plaintextOutputPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);
                outputCreated = true;

                source.Position = header.HeaderLength;
                using Aes aes = Aes.Create();
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encryptionKey;
                aes.IV = header.Iv;

                using ICryptoTransform decryptor = aes.CreateDecryptor();
                using var ciphertext = new LimitedReadStream(source, header.CiphertextLength);
                await using var cryptoStream = new CryptoStream(
                    ciphertext,
                    decryptor,
                    CryptoStreamMode.Read,
                    leaveOpen: false);
                await cryptoStream.CopyToAsync(
                        destination,
                        81920,
                        cancellationToken)
                    .ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encryptionKey);
                CryptographicOperations.ZeroMemory(macKey);
            }
        }
        catch
        {
            if (outputCreated)
            {
                try
                {
                    File.Delete(plaintextOutputPath);
                }
                catch
                {
                    // Preserve the authentication/decryption failure. The
                    // restore operation's bounded cleanup reports leftovers.
                }
            }

            throw;
        }
    }

    private static void ValidatePassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length < DeviceBackupOptions.MinimumBackupPasswordLength)
        {
            throw new ArgumentException(
                $"The backup password must contain at least {DeviceBackupOptions.MinimumBackupPasswordLength} characters.",
                nameof(password));
        }
    }

    private static long GetCiphertextLength(long plaintextLength)
    {
        long blockCount = checked(plaintextLength / BlockLength + 1);
        return checked(blockCount * BlockLength);
    }

    private static byte[] BuildHeader(long ciphertextLength, byte[] salt, byte[] iv)
    {
        if (salt.Length != SaltLength || iv.Length != IvLength)
            throw new ArgumentException("The backup envelope cryptographic parameters have invalid lengths.");

        byte[] header = new byte[HeaderPrefixLength + salt.Length + iv.Length];
        Magic.AsSpan().CopyTo(header);
        int offset = Magic.Length;
        header[offset++] = FormatVersion;
        header[offset++] = KdfPbkdf2Sha256;
        header[offset++] = EncryptionAes256CbcPkcs7;
        header[offset++] = AuthenticationHmacSha256;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(offset, sizeof(int)), KdfIterations);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(offset, sizeof(int)), salt.Length);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(offset, sizeof(int)), iv.Length);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(offset, sizeof(long)), ciphertextLength);
        offset += sizeof(long);
        salt.CopyTo(header, offset);
        offset += salt.Length;
        iv.CopyTo(header, offset);
        return header;
    }

    private static (byte[] EncryptionKey, byte[] MacKey) DeriveKeys(
        string password,
        byte[] salt,
        int iterations)
    {
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] keyMaterial = Rfc2898DeriveBytes.Pbkdf2(
            passwordBytes,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            KeyMaterialLength);
        try
        {
            return (
                keyMaterial.AsSpan(0, EncryptionKeyLength).ToArray(),
                keyMaterial.AsSpan(EncryptionKeyLength, MacKeyLength).ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(keyMaterial);
        }
    }

    private static async Task AppendHmacAsync(
        Stream stream,
        long authenticatedLength,
        byte[] macKey,
        CancellationToken cancellationToken)
    {
        byte[] mac = await ComputeHmacAsync(
                stream,
                authenticatedLength,
                macKey,
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            stream.Position = authenticatedLength;
            await stream.WriteAsync(mac, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(mac);
        }
    }

    private static async Task<byte[]> ComputeHmacAsync(
        Stream stream,
        long length,
        byte[] macKey,
        CancellationToken cancellationToken)
    {
        if (!stream.CanSeek || length < 0 || length > stream.Length)
            throw new InvalidDataException("The encrypted backup stream length is invalid.");

        stream.Position = 0;
        using IncrementalHash hmac = IncrementalHash.CreateHMAC(
            HashAlgorithmName.SHA256,
            macKey);
        byte[] buffer = new byte[81920];
        long remaining = length;
        while (remaining > 0)
        {
            int requested = (int)Math.Min(buffer.Length, remaining);
            int bytesRead = await stream
                .ReadAsync(buffer.AsMemory(0, requested), cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
                throw new InvalidDataException("The encrypted backup stream ended unexpectedly.");

            hmac.AppendData(buffer, 0, bytesRead);
            remaining -= bytesRead;
        }

        return hmac.GetHashAndReset();
    }

    private static async Task<EnvelopeHeader> ReadHeaderAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] prefix = new byte[HeaderPrefixLength];
        await ReadExactlyAsync(stream, prefix, cancellationToken).ConfigureAwait(false);
        if (!prefix.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("The file is not a DeepDroid encrypted backup.");

        int offset = Magic.Length;
        byte formatVersion = prefix[offset++];
        byte kdf = prefix[offset++];
        byte encryption = prefix[offset++];
        byte authentication = prefix[offset++];
        int iterations = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(offset, sizeof(int)));
        offset += sizeof(int);
        int saltLength = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(offset, sizeof(int)));
        offset += sizeof(int);
        int ivLength = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(offset, sizeof(int)));
        offset += sizeof(int);
        long ciphertextLength = BinaryPrimitives.ReadInt64LittleEndian(prefix.AsSpan(offset, sizeof(long)));

        if (formatVersion != FormatVersion
            || kdf != KdfPbkdf2Sha256
            || encryption != EncryptionAes256CbcPkcs7
            || authentication != AuthenticationHmacSha256
            || iterations != KdfIterations
            || saltLength != SaltLength
            || ivLength != IvLength
            || ciphertextLength <= 0
            || ciphertextLength % BlockLength != 0)
        {
            throw new InvalidDataException("The encrypted backup envelope header is invalid.");
        }

        byte[] saltAndIv = new byte[saltLength + ivLength];
        await ReadExactlyAsync(stream, saltAndIv, cancellationToken).ConfigureAwait(false);
        byte[] headerBytes = new byte[prefix.Length + saltAndIv.Length];
        prefix.CopyTo(headerBytes, 0);
        saltAndIv.CopyTo(headerBytes, prefix.Length);
        return new EnvelopeHeader(
            headerBytes,
            saltAndIv[..saltLength],
            saltAndIv[saltLength..],
            iterations,
            ciphertextLength);
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int bytesRead = await stream
                .ReadAsync(buffer.AsMemory(offset), cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
                throw new InvalidDataException("The encrypted backup file ended unexpectedly.");
            offset += bytesRead;
        }
    }

    private sealed record EnvelopeHeader(
        byte[] HeaderBytes,
        byte[] Salt,
        byte[] Iv,
        int KdfIterations,
        long CiphertextLength)
    {
        public int HeaderLength => HeaderBytes.Length;
    }

    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;

        public LimitedReadStream(Stream inner, long length)
        {
            _inner = inner;
            _remaining = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _remaining;
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int requested = (int)Math.Min(count, _remaining);
            if (requested == 0)
                return 0;

            int read = _inner.Read(buffer, offset, requested);
            if (read == 0)
                throw new InvalidDataException("The encrypted backup ciphertext ended unexpectedly.");

            _remaining -= read;
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int requested = (int)Math.Min(buffer.Length, _remaining);
            if (requested == 0)
                return ValueTask.FromResult(0);

            return ReadLimitedAsync(buffer[..requested], cancellationToken);
        }

        private async ValueTask<int> ReadLimitedAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new InvalidDataException("The encrypted backup ciphertext ended unexpectedly.");

            _remaining -= read;
            return read;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override void Flush() => throw new NotSupportedException();
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.FromException(new NotSupportedException());
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
