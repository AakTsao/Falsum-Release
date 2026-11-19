using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace InFalsus.Gameplay.Falsum.Core
{
    /// <summary>
    /// Self-contained share code for one bank preset:
    /// <c>FKY1.</c> + base64url(gzip(json + crc32 trailer)).
    ///
    /// <para>WHY THIS SHAPE:</para>
    /// <list type="bullet">
    /// <item><b>No server, no account.</b> The editor has no network access at
    /// all (the tool sandbox forbids it and the app has no account system), so
    /// a shareable preset can only travel as text or as a file.</item>
    /// <item><b>base64url, not base64.</b> Chat apps turn <c>+</c> into a
    /// space and treat <c>/</c> as a URL separator. <c>-</c>/<c>_</c> and no
    /// padding survive copy/paste intact.</item>
    /// <item><b>gzip.</b> The payload is dense numeric JSON; deflate typically
    /// turns a six-segment preset into 300-600 characters, which fits in a
    /// single message.</item>
    /// <item><b>CRC32 trailer.</b> A truncated or edited code fails loudly
    /// instead of silently producing a deformed group (a share code is often
    /// re-typed by hand or forwarded through several apps).</item>
    /// </list>
    ///
    /// <para>Every failure is reported as a localisation key so the caller can
    /// surface it through the normal hint/status channel; this file never
    /// throws at the UI boundary.</para>
    /// </summary>
    public static class FalsumSkyAreaShareCode
    {
        public const string Prefix = "FKY1.";
        public const int MaxDecodedBytes = 64 * 1024;

        private const string CrcMarker = "#crc32:";

        /// <summary>Encodes a validated preset into a pasteable share code.</summary>
        public static bool TryEncode(
            FalsumSkyAreaPreset preset,
            out string code,
            out string errorKey)
        {
            code = string.Empty;
            errorKey = string.Empty;
            if (!FalsumSkyAreaBankModel.Validate(preset, out errorKey))
            {
                return false;
            }

            var json = FalsumJson.Serialize(
                FalsumSkyAreaBankModel.ToJson(preset),
                pretty: false);
            // Trailer format is "<json><MARKER><hex>": no separator, because
            // FalsumJson.Serialize already ends the document and a leading
            // newline would make the JSON text differ between the encoder and
            // the verifier (the CRC is computed over the JSON text exactly).
            var payload = json + CrcMarker +
                          Crc32Hex(Encoding.UTF8.GetBytes(json));
            var compressed = Compress(Encoding.UTF8.GetBytes(payload));
            if (compressed == null)
            {
                errorKey = FalsumSkyAreaBankModel.ErrorBadCode;
                return false;
            }
            code = Prefix + Base64UrlEncode(compressed);
            return true;
        }

        /// <summary>
        /// Decodes a share code back into a preset. Returns false with a
        /// localisation key for every malformed/unsupported input.
        /// </summary>
        public static bool TryDecode(
            string code,
            out FalsumSkyAreaPreset preset,
            out string errorKey)
        {
            preset = null;
            errorKey = string.Empty;
            if (string.IsNullOrEmpty(code))
            {
                errorKey = FalsumSkyAreaBankModel.ErrorBadCode;
                return false;
            }

            var text = TrimToCode(code);
            if (!text.StartsWith(Prefix, StringComparison.Ordinal))
            {
                errorKey = FalsumSkyAreaBankModel.ErrorBadCode;
                return false;
            }
            var body = text.Substring(Prefix.Length);

            byte[] compressed;
            if (!TryBase64UrlDecode(body, out compressed))
            {
                errorKey = FalsumSkyAreaBankModel.ErrorBadCode;
                return false;
            }
            var payloadBytes = Decompress(compressed, MaxDecodedBytes);
            if (payloadBytes == null)
            {
                errorKey = FalsumSkyAreaBankModel.ErrorBadCode;
                return false;
            }

            var payload = Encoding.UTF8.GetString(payloadBytes);
            var markerIndex = payload.LastIndexOf(
                CrcMarker,
                StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                errorKey = FalsumSkyAreaBankModel.ErrorBadCode;
                return false;
            }
            var json = payload.Substring(0, markerIndex);
            var expectedCrc = payload.Substring(markerIndex + CrcMarker.Length)
                .Trim();
            var actualCrc = Crc32Hex(Encoding.UTF8.GetBytes(json));
            if (!string.Equals(
                    expectedCrc,
                    actualCrc,
                    StringComparison.OrdinalIgnoreCase))
            {
                errorKey = FalsumSkyAreaBankModel.ErrorBadCode;
                return false;
            }

            FalsumJsonObject root;
            try
            {
                root = FalsumJson.AsObject(FalsumJson.Parse(json));
            }
            catch (Exception)
            {
                errorKey = FalsumSkyAreaBankModel.ErrorBadCode;
                return false;
            }

            return FalsumSkyAreaBankModel.FromJson(root, out preset, out errorKey);
        }

        /// <summary>Best-effort cleanup of a pasted code (whitespace/line wraps).</summary>
        private static string TrimToCode(string raw)
        {
            var builder = new StringBuilder(raw.Length);
            for (var i = 0; i < raw.Length; i++)
            {
                var c = raw[i];
                if (c == '\r' || c == '\n' || c == '\t' || c == ' ')
                {
                    continue;
                }
                builder.Append(c);
            }
            return builder.ToString();
        }

        private static byte[] Compress(byte[] source)
        {
            try
            {
                using (var output = new MemoryStream())
                {
                    using (var gzip = new GZipStream(
                               output,
                               CompressionMode.Compress,
                               leaveOpen: true))
                    {
                        gzip.Write(source, 0, source.Length);
                    }
                    return output.ToArray();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Inflates while enforcing a hard total-size ceiling, so a malicious
        /// or corrupt code cannot expand into gigabytes.
        /// </summary>
        private static byte[] Decompress(byte[] source, int maximumBytes)
        {
            try
            {
                using (var input = new MemoryStream(source))
                using (var gzip = new GZipStream(
                           input,
                           CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[4096];
                    var total = 0;
                    while (true)
                    {
                        var read = gzip.Read(buffer, 0, buffer.Length);
                        if (read <= 0)
                        {
                            break;
                        }
                        total += read;
                        if (total > maximumBytes)
                        {
                            return null;
                        }
                        output.Write(buffer, 0, read);
                    }
                    return output.ToArray();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            var text = Convert.ToBase64String(bytes);
            var builder = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '+')
                {
                    builder.Append('-');
                }
                else if (c == '/')
                {
                    builder.Append('_');
                }
                else if (c != '=')
                {
                    builder.Append(c);
                }
            }
            return builder.ToString();
        }

        private static bool TryBase64UrlDecode(
            string text,
            out byte[] bytes)
        {
            bytes = null;
            if (text == null)
            {
                return false;
            }
            var builder = new StringBuilder(text.Length + 3);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '-')
                {
                    builder.Append('+');
                }
                else if (c == '_')
                {
                    builder.Append('/');
                }
                else
                {
                    builder.Append(c);
                }
            }
            while (builder.Length % 4 != 0)
            {
                builder.Append('=');
            }
            try
            {
                bytes = Convert.FromBase64String(builder.ToString());
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Crc32Hex(byte[] bytes)
        {
            return Crc32(bytes).ToString("X8");
        }

        private static uint Crc32(byte[] bytes)
        {
            uint crc = 0xFFFFFFFFu;
            for (var i = 0; i < bytes.Length; i++)
            {
                crc ^= bytes[i];
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1u) != 0
                        ? (crc >> 1) ^ 0xEDB88320u
                        : crc >> 1;
                }
            }
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
