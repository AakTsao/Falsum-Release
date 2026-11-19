using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    public static class FalsumSongFont
    {
        private const string PackageRelativePath =
            "Falsum/Fonts/SongInfoCard.fpk";
        private const int SaltLength = 16;
        private const int IvLength = 16;
        private const int MacLength = 32;
        private const int HeaderLength =
            4 + SaltLength + IvLength;
        private const int Pbkdf2Iterations = 120000;
        private const string SecretA =
            "576169bbc659a97b";
        private const string SecretB =
            "5c760b8d1b0dbe3b";
        private const string SecretC =
            "33ba6153c780df2e";
        private const string SecretD =
            "1a3fe2cacd5b12b3";

        private static readonly byte[] Magic =
        {
            0x46,
            0x53,
            0x46,
            0x50,
        };

        private static bool _loadAttempted;
        private static Font _font;
        private static AssetBundle _bundle;
        private static MemoryStream _bundleStream;

        public static Font CardFont
        {
            get
            {
                if (!_loadAttempted)
                {
                    _loadAttempted = true;
                    _font = LoadProtectedFont();
                }
                return _font;
            }
        }

        public static bool IsLoaded
        {
            get { return _font != null; }
        }

#if UNITY_EDITOR
        public static byte[] ProtectAssetBundle(
            byte[] assetBundleBytes)
        {
            if (assetBundleBytes == null ||
                assetBundleBytes.Length == 0)
            {
                throw new ArgumentException(
                    L.Get("falsum.error.songFont.bundleEmpty"),
                    "assetBundleBytes");
            }

            var compressed = Compress(assetBundleBytes);
            var salt = new byte[SaltLength];
            var iv = new byte[IvLength];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(salt);
                random.GetBytes(iv);
            }

            byte[] encryptionKey;
            byte[] macKey;
            DeriveKeys(salt, out encryptionKey, out macKey);

            byte[] cipherText;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encryptionKey;
                aes.IV = iv;
                using (var encryptor = aes.CreateEncryptor())
                {
                    cipherText = encryptor.TransformFinalBlock(
                        compressed,
                        0,
                        compressed.Length);
                }
            }

            var package = new byte[
                HeaderLength +
                cipherText.Length +
                MacLength];
            Buffer.BlockCopy(Magic, 0, package, 0, Magic.Length);
            Buffer.BlockCopy(
                salt,
                0,
                package,
                Magic.Length,
                salt.Length);
            Buffer.BlockCopy(
                iv,
                0,
                package,
                Magic.Length + salt.Length,
                iv.Length);
            Buffer.BlockCopy(
                cipherText,
                0,
                package,
                HeaderLength,
                cipherText.Length);

            using (var hmac = new HMACSHA256(macKey))
            {
                var mac = hmac.ComputeHash(
                    package,
                    0,
                    package.Length - MacLength);
                Buffer.BlockCopy(
                    mac,
                    0,
                    package,
                    package.Length - MacLength,
                    mac.Length);
            }

            Array.Clear(compressed, 0, compressed.Length);
            Array.Clear(cipherText, 0, cipherText.Length);
            Array.Clear(encryptionKey, 0, encryptionKey.Length);
            Array.Clear(macKey, 0, macKey.Length);
            return package;
        }
#endif

        private static Font LoadProtectedFont()
        {
            var path = Path.Combine(
                Application.streamingAssetsPath,
                PackageRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                Debug.LogWarning(
                    "[Falsum] Song card font package is missing: " +
                    path);
                return null;
            }

            try
            {
                var package = File.ReadAllBytes(path);
                if (package.Length <
                    HeaderLength + MacLength)
                {
                    throw new InvalidDataException(
                        L.Get("falsum.error.songFont.truncated"));
                }
                for (var i = 0; i < Magic.Length; i++)
                {
                    if (package[i] != Magic[i])
                    {
                        throw new InvalidDataException(
                            L.Get(
                                "falsum.error.songFont.headerInvalid"));
                    }
                }

                byte[] encryptionKey;
                byte[] macKey;
                var salt = new byte[SaltLength];
                Buffer.BlockCopy(
                    package,
                    Magic.Length,
                    salt,
                    0,
                    SaltLength);
                DeriveKeys(
                    salt,
                    out encryptionKey,
                    out macKey);

                using (var hmac = new HMACSHA256(macKey))
                {
                    var expected = hmac.ComputeHash(
                        package,
                        0,
                        package.Length - MacLength);
                    if (!FixedTimeEquals(
                            expected,
                            package,
                            package.Length - MacLength))
                    {
                        throw new CryptographicException(
                            L.Get(
                                "falsum.error.songFont.authenticationFailed"));
                    }
                }

                var cipherLength =
                    package.Length - HeaderLength - MacLength;
                var cipherText = new byte[cipherLength];
                Buffer.BlockCopy(
                    package,
                    HeaderLength,
                    cipherText,
                    0,
                    cipherLength);
                var iv = new byte[IvLength];
                Buffer.BlockCopy(
                    package,
                    Magic.Length + SaltLength,
                    iv,
                    0,
                    IvLength);

                byte[] compressed;
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.BlockSize = 128;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = encryptionKey;
                    aes.IV = iv;
                    using (var decryptor = aes.CreateDecryptor())
                    {
                        compressed =
                            decryptor.TransformFinalBlock(
                                cipherText,
                                0,
                                cipherText.Length);
                    }
                }

                var bundleBytes = Decompress(compressed);
                var stream = new MemoryStream(
                    bundleBytes,
                    false);
                var bundle = AssetBundle.LoadFromStream(stream);
                if (bundle == null)
                {
                    stream.Dispose();
                    throw new InvalidDataException(
                        L.Get(
                            "falsum.error.songFont.bundleLoadFailed"));
                }
                var fonts = bundle.LoadAllAssets<Font>();
                if (fonts == null || fonts.Length == 0)
                {
                    bundle.Unload(true);
                    stream.Dispose();
                    throw new InvalidDataException(
                        L.Get(
                            "falsum.error.songFont.noFont"));
                }

                _bundle = bundle;
                _bundleStream = stream;
                Debug.Log(
                    "[Falsum] Protected song card font loaded: " +
                    fonts[0].name);

                Array.Clear(
                    compressed,
                    0,
                    compressed.Length);
                Array.Clear(
                    cipherText,
                    0,
                    cipherText.Length);
                Array.Clear(
                    encryptionKey,
                    0,
                    encryptionKey.Length);
                Array.Clear(macKey, 0, macKey.Length);
                return fonts[0];
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] Song card font package could not be loaded: " +
                    exception.Message);
                return null;
            }
        }

        private static void DeriveKeys(
            byte[] salt,
            out byte[] encryptionKey,
            out byte[] macKey)
        {
            var password = string.Concat(
                SecretA,
                SecretB,
                SecretC,
                SecretD);
            var material = new byte[64];
            using (var derive = new Rfc2898DeriveBytes(
                       password,
                       salt,
                       Pbkdf2Iterations,
                       HashAlgorithmName.SHA256))
            {
                var derived = derive.GetBytes(material.Length);
                Buffer.BlockCopy(
                    derived,
                    0,
                    material,
                    0,
                    material.Length);
                Array.Clear(
                    derived,
                    0,
                    derived.Length);
            }
            encryptionKey = new byte[32];
            macKey = new byte[32];
            Buffer.BlockCopy(material, 0, encryptionKey, 0, 32);
            Buffer.BlockCopy(material, 32, macKey, 0, 32);
            Array.Clear(material, 0, material.Length);
        }

        private static byte[] Compress(byte[] source)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(
                           output,
                           CompressionMode.Compress,
                           true))
                {
                    gzip.Write(source, 0, source.Length);
                }
                return output.ToArray();
            }
        }

        private static byte[] Decompress(byte[] source)
        {
            using (var input = new MemoryStream(source, false))
            using (var gzip = new GZipStream(
                       input,
                       CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[81920];
                int read;
                while ((read = gzip.Read(
                            buffer,
                            0,
                            buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }

        private static bool FixedTimeEquals(
            byte[] expected,
            byte[] actual,
            int actualOffset)
        {
            if (expected == null ||
                actual == null ||
                actualOffset < 0 ||
                actualOffset + expected.Length >
                actual.Length)
            {
                return false;
            }
            var difference = 0;
            for (var i = 0; i < expected.Length; i++)
            {
                difference |=
                    expected[i] ^
                    actual[actualOffset + i];
            }
            return difference == 0;
        }
    }
}
