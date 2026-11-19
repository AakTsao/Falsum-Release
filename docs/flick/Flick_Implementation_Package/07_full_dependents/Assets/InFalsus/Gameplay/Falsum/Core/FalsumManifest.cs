using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Core
{
    public sealed class FalsumDifficultyInfo
    {
        internal FalsumJsonObject Node;

        public int Index
        {
            get { return FalsumJson.GetInt(Node, "index", -1); }
        }

        public string Name
        {
            get { return FalsumJson.GetString(Node, "name", string.Empty); }
        }

        public bool Available
        {
            get { return FalsumJson.GetBool(Node, "available", false); }
        }

        public string ChartPath
        {
            get { return FalsumJson.GetString(Node, "chart", string.Empty); }
        }

        public int Rating
        {
            get { return FalsumJson.GetInt(Node, "rating", 0); }
        }

        public string ChartDesigner
        {
            get
            {
                return FalsumJson.GetString(
                    Node,
                    "chartDesigner",
                    string.Empty);
            }
        }

        public string JacketDesigner
        {
            get
            {
                return FalsumJson.GetString(
                    Node,
                    "jacketDesigner",
                    string.Empty);
            }
        }

        public void SetAvailable(bool value)
        {
            FalsumJson.Set(Node, "available", value);
        }

        public void SetRating(int value)
        {
            FalsumJson.Set(Node, "rating", value);
        }

        public void SetChartDesigner(string value)
        {
            FalsumJson.Set(Node, "chartDesigner", value ?? string.Empty);
        }

        public void SetJacketDesigner(string value)
        {
            FalsumJson.Set(Node, "jacketDesigner", value ?? string.Empty);
        }
    }

    public sealed class FalsumManifest
    {
        public const string FormatName = "falsum";
        public const int CurrentVersion = 1;
        public const string DefaultBackgroundFile = "31_Space-Positive.png";
        private static readonly Regex SongIdPattern = new Regex(
            "^[a-z0-9][a-z0-9_-]{0,63}$",
            RegexOptions.CultureInvariant);
        /// <summary>
        /// The rule applied when a NEW package is created: a song id must
        /// begin with a lowercase letter and may then contain only
        /// lowercase letters and digits.
        /// <para>
        /// This is deliberately NOT the rule used to read packages. The
        /// permissive <see cref="SongIdPattern"/> above still gates
        /// <see cref="Validate"/>, so shipped and legacy charts whose ids
        /// carry <c>_</c> or <c>-</c> keep loading. The two rules must
        /// never be swapped: tightening the read side rejects existing
        /// content, relaxing the create side silently accepts uppercase
        /// ids the editor promised to refuse.
        /// </para>
        /// </summary>
        private static readonly Regex NewSongIdPattern = new Regex(
            "^[a-z][a-z0-9]{0,63}$",
            RegexOptions.CultureInvariant);
        public static readonly string[] DifficultyNames =
        {
            "MIN",
            "EVO",
            "ULT",
            "FBD",
        };

        private readonly FalsumJsonObject _root;
        private readonly List<FalsumDifficultyInfo> _difficulties =
            new List<FalsumDifficultyInfo>();

        public FalsumJsonObject Root
        {
            get { return _root; }
        }

        public string SongId
        {
            get { return FalsumJson.GetString(_root, "songId", string.Empty); }
        }

        public string DefaultLocale
        {
            get
            {
                return FalsumJson.GetString(
                    _root,
                    "defaultLocale",
                    "und");
            }
        }

        public string Title
        {
            get { return FalsumJson.GetString(_root, "title", string.Empty); }
        }

        public string Artist
        {
            get { return FalsumJson.GetString(_root, "artist", string.Empty); }
        }

        public string JacketDesigner
        {
            get
            {
                var value = FalsumJson.GetString(
                    _root,
                    "jacketDesigner",
                    string.Empty);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
                for (var i = 0; i < _difficulties.Count; i++)
                {
                    value = _difficulties[i].JacketDesigner;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
                return string.Empty;
            }
        }

        public IList<FalsumDifficultyInfo> Difficulties
        {
            get { return _difficulties; }
        }

        public string AudioFile
        {
            get
            {
                var audio = FalsumJson.GetObject(_root, "audio");
                return audio != null
                    ? FalsumJson.GetString(audio, "file", "audio.ogg")
                    : "audio.ogg";
            }
        }

        public int AudioDurationMs
        {
            get
            {
                var audio = FalsumJson.GetObject(_root, "audio");
                return audio != null
                    ? FalsumJson.GetInt(audio, "durationMs", 0)
                    : 0;
            }
        }

        public string CoverFile
        {
            get
            {
                var cover = FalsumJson.GetObject(_root, "cover");
                return cover != null
                    ? FalsumJson.GetString(cover, "file", string.Empty)
                    : string.Empty;
            }
        }

        public int CoverWidth
        {
            get
            {
                var cover = FalsumJson.GetObject(_root, "cover");
                return cover != null
                    ? FalsumJson.GetInt(cover, "width", 0)
                    : 0;
            }
        }

        public int CoverHeight
        {
            get
            {
                var cover = FalsumJson.GetObject(_root, "cover");
                return cover != null
                    ? FalsumJson.GetInt(cover, "height", 0)
                    : 0;
            }
        }

        public string BackgroundFile
        {
            get
            {
                return FalsumJson.GetString(
                    _root,
                    "background",
                    string.Empty).Trim();
            }
        }

        /// <summary>
        /// The background name exactly as written into <c>song.json</c>
        /// (or the default when the field is missing). The shipped library
        /// is JPEG to save space, but chart data always keeps the
        /// historical <c>.png</c> spelling so old and new packages stay
        /// interchangeable.
        /// </summary>
        public string CanonicalBackgroundFile
        {
            get
            {
                return NormalizeBackgroundName(EffectiveBackgroundFile);
            }
        }

        public string EffectiveBackgroundFile
        {
            get
            {
                return string.IsNullOrWhiteSpace(BackgroundFile)
                    ? DefaultBackgroundFile
                    : BackgroundFile;
            }
        }

        private FalsumManifest(FalsumJsonObject root)
        {
            _root = root;
            var difficulties = FalsumJson.GetArray(root, "difficulties");
            if (difficulties == null)
            {
                return;
            }
            for (var i = 0; i < difficulties.Count; i++)
            {
                var node = difficulties[i] as FalsumJsonObject;
                if (node == null)
                {
                    continue;
                }
                _difficulties.Add(new FalsumDifficultyInfo
                {
                    Node = node,
                });
            }
        }

        public static FalsumManifest Parse(string json)
        {
            var root = FalsumJson.Parse(json) as FalsumJsonObject;
            if (root == null)
            {
                throw new FormatException(
                    L.Get("falsum.error.manifest.objectRequired"));
            }

            var format = FalsumJson.GetString(root, "format", string.Empty);
            if (!string.Equals(
                    format,
                    FormatName,
                    StringComparison.Ordinal))
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.manifest.unsupportedFormat",
                        ("format", format)));
            }

            var version = FalsumJson.GetInt(root, "version", -1);
            if (version != CurrentVersion)
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.manifest.unsupportedVersion",
                        ("version", version)));
            }

            var manifest = new FalsumManifest(root);
            manifest.Validate();
            return manifest;
        }

        public static FalsumManifest CreateTemplate(
            string songId,
            string title,
            string artist,
            int durationMs,
            string backgroundFile = DefaultBackgroundFile)
        {
            var root = new FalsumJsonObject();
            FalsumJson.Set(root, "format", FormatName);
            FalsumJson.Set(root, "version", CurrentVersion);
            FalsumJson.Set(root, "songId", songId);
            FalsumJson.Set(root, "defaultLocale", "und");
            FalsumJson.Set(root, "title", title);
            FalsumJson.Set(root, "artist", artist);
            FalsumJson.Set(root, "jacketDesigner", string.Empty);
            FalsumJson.Set(
                root,
                "background",
                string.IsNullOrWhiteSpace(backgroundFile)
                    ? DefaultBackgroundFile
                    : NormalizeBackgroundName(backgroundFile));

            var audio = new FalsumJsonObject();
            FalsumJson.Set(audio, "file", "audio.ogg");
            FalsumJson.Set(audio, "codec", "vorbis");
            FalsumJson.Set(audio, "sampleRate", 48000);
            FalsumJson.Set(audio, "channels", 2);
            FalsumJson.Set(audio, "durationMs", durationMs);
            FalsumJson.Set(root, "audio", audio);

            var difficulties = new List<object>();
            for (var i = 0; i < DifficultyNames.Length; i++)
            {
                var difficulty = new FalsumJsonObject();
                FalsumJson.Set(difficulty, "index", i);
                FalsumJson.Set(
                    difficulty,
                    "name",
                    DifficultyNames[i]);
                FalsumJson.Set(difficulty, "available", i == 0);
                FalsumJson.Set(
                    difficulty,
                    "chart",
                    "charts/" +
                    DifficultyNames[i].ToLowerInvariant() +
                    ".chart.txt");
                FalsumJson.Set(difficulty, "rating", 0);
                FalsumJson.Set(difficulty, "chartDesigner", string.Empty);
                FalsumJson.Set(difficulty, "jacketDesigner", string.Empty);
                difficulties.Add(difficulty);
            }
            FalsumJson.Set(root, "difficulties", difficulties);
            return new FalsumManifest(root);
        }

        public void SetTitle(string value)
        {
            FalsumJson.Set(_root, "title", value ?? string.Empty);
        }

        public void SetSongId(string value)
        {
            FalsumJson.Set(
                _root,
                "songId",
                (value ?? string.Empty).Trim());
        }

        public void SetArtist(string value)
        {
            FalsumJson.Set(_root, "artist", value ?? string.Empty);
        }

        public static bool IsValidSongId(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   SongIdPattern.IsMatch(value);
        }

        /// <summary>
        /// Validates the song id typed into the "create chart" dialog. See
        /// <see cref="NewSongIdPattern"/> for why this is a separate rule
        /// from <see cref="IsValidSongId"/>.
        /// </summary>
        public static bool IsValidNewSongId(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   NewSongIdPattern.IsMatch(value);
        }

        public static string NormalizeSongId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "untitled";
            }
            var builder = new System.Text.StringBuilder(64);
            var lastWasSeparator = false;
            for (var i = 0;
                 i < value.Length && builder.Length < 64;
                 i++)
            {
                var c = value[i];
                if (c >= 'A' && c <= 'Z')
                {
                    c = (char)(c - 'A' + 'a');
                }
                var allowed = (c >= 'a' && c <= 'z') ||
                              (c >= '0' && c <= '9') ||
                              c == '_' ||
                              c == '-';
                if (!allowed)
                {
                    if (builder.Length > 0 && !lastWasSeparator)
                    {
                        builder.Append('-');
                        lastWasSeparator = true;
                    }
                    continue;
                }
                builder.Append(c);
                lastWasSeparator = c == '-' || c == '_';
            }
            var result = builder.ToString().Trim('-', '_');
            if (result.Length == 0 ||
                !(result[0] >= 'a' && result[0] <= 'z') &&
                !(result[0] >= '0' && result[0] <= '9'))
            {
                result = "song-" + result;
            }
            return result.Length == 0 ? "untitled" : result;
        }

        public void SetJacketDesigner(string value)
        {
            var designer = value ?? string.Empty;
            FalsumJson.Set(_root, "jacketDesigner", designer);
            for (var i = 0; i < _difficulties.Count; i++)
            {
                _difficulties[i].SetJacketDesigner(designer);
            }
        }

        public void SetBackground(string value)
        {
            FalsumJson.Set(
                _root,
                "background",
                string.IsNullOrWhiteSpace(value)
                    ? DefaultBackgroundFile
                    : NormalizeBackgroundName(value));
        }

        /// <summary>
        /// Normalizes a background name to the canonical <c>.png</c>
        /// spelling used in <c>song.json</c>. The library itself is
        /// stored as JPEG, so <c>03_x.jpg</c> is the same logical asset
        /// as <c>03_x.png</c>; writing the JPEG spelling into a package
        /// would make old players (which only look for the PNG name)
        /// show a black background.
        /// </summary>
        public static string NormalizeBackgroundName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultBackgroundFile;
            }
            var file = value.Trim();
            var dot = file.LastIndexOf('.');
            if (dot < 0 || dot == file.Length - 1)
            {
                return file;
            }
            var extension = file.Substring(dot);
            if (!string.Equals(
                    extension,
                    ".png",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    extension,
                    ".jpg",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    extension,
                    ".jpeg",
                    StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
            return file.Substring(0, dot) + ".png";
        }

        public static bool IsValidBackgroundFile(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            var file = value.Trim();
            if (file.IndexOf('/') >= 0 ||
                file.IndexOf('\\') >= 0 ||
                file.IndexOf(':') >= 0 ||
                file == "." ||
                file == "..")
            {
                return false;
            }
            var extension = FalsumPathExtension(file);
            return string.Equals(
                       extension,
                       ".png",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       extension,
                       ".jpg",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       extension,
                       ".jpeg",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string FalsumPathExtension(string file)
        {
            var dot = file.LastIndexOf('.');
            return dot >= 0 ? file.Substring(dot) : string.Empty;
        }

        public FalsumDifficultyInfo FindDifficulty(int index)
        {
            for (var i = 0; i < _difficulties.Count; i++)
            {
                if (_difficulties[i].Index == index)
                {
                    return _difficulties[i];
                }
            }
            return null;
        }

        public string Serialize()
        {
            return FalsumJson.Serialize(_root, true);
        }

        public void ReplaceFrom(FalsumManifest source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (ReferenceEquals(source, this))
            {
                return;
            }
            _root.Clear();
            foreach (var pair in source._root)
            {
                _root[pair.Key] = FalsumJson.DeepClone(pair.Value);
            }
            RebuildDifficultyCache();
        }

        public void Validate()
        {
            if (!IsValidSongId(SongId))
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.manifest.invalidSongId"));
            }
            if (string.IsNullOrWhiteSpace(Title))
            {
                throw new FormatException(
                    L.Get("falsum.error.manifest.titleRequired"));
            }
            if (string.IsNullOrWhiteSpace(Artist))
            {
                throw new FormatException(
                    L.Get("falsum.error.manifest.artistRequired"));
            }
            var audioObject = FalsumJson.GetObject(_root, "audio");
            if (audioObject == null)
            {
                throw new FormatException(
                    L.Get("falsum.error.manifest.audioRequired"));
            }
            if (!audioObject.ContainsKey("file"))
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.manifest.audioFileRequired"));
            }
            if (!string.Equals(
                    AudioFile,
                    "audio.ogg",
                    StringComparison.Ordinal))
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.manifest.audioFileInvalid"));
            }
            if (_difficulties.Count == 0)
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.manifest.difficultyRequired"));
            }

            var packagePaths = new HashSet<string>(
                StringComparer.Ordinal)
            {
                "song.json",
            };
            AddPackagePath(packagePaths, "audio.ogg", "audio.file");
            var coverObject = FalsumJson.GetObject(_root, "cover");
            if (_root.ContainsKey("cover"))
            {
                if (coverObject == null)
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.manifest.coverObjectRequired"));
                }
                if (!string.Equals(
                        CoverFile,
                        "cover.png",
                        StringComparison.Ordinal))
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.manifest.coverFileInvalid"));
                }
                AddPackagePath(
                    packagePaths,
                    CoverFile,
                    "cover.file");
            }

            var seen = new HashSet<int>();
            for (var i = 0; i < _difficulties.Count; i++)
            {
                var difficulty = _difficulties[i];
                if (difficulty.Index < 0 || difficulty.Index > 3)
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.manifest.difficultyIndexRange"));
                }
                if (!seen.Add(difficulty.Index))
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.manifest.duplicateDifficultyIndex",
                            ("index", difficulty.Index)));
                }
                if (difficulty.Available &&
                    string.IsNullOrWhiteSpace(difficulty.ChartPath))
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.manifest.chartPathRequired"));
                }
                if (!string.IsNullOrWhiteSpace(difficulty.ChartPath))
                {
                    ValidateChartPath(difficulty.ChartPath);
                    AddPackagePath(
                        packagePaths,
                        difficulty.ChartPath,
                        "difficulty chart path");
                }
            }

            var valid = 0;
            for (var i = 0; i < _difficulties.Count; i++)
            {
                if (_difficulties[i].Available)
                {
                    valid++;
                }
            }
            if (valid == 0)
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.manifest.noAvailableDifficulty"));
            }
        }

        private void RebuildDifficultyCache()
        {
            _difficulties.Clear();
            var difficulties = FalsumJson.GetArray(
                _root,
                "difficulties");
            if (difficulties == null)
            {
                return;
            }
            for (var i = 0; i < difficulties.Count; i++)
            {
                var node = difficulties[i] as FalsumJsonObject;
                if (node == null)
                {
                    continue;
                }
                _difficulties.Add(new FalsumDifficultyInfo
                {
                    Node = node,
                });
            }
        }

        private static void ValidateChartPath(string path)
        {
            if (!IsSafeRelativePackagePath(path) ||
                !path.StartsWith("charts/", StringComparison.Ordinal) ||
                !path.EndsWith(
                    ".chart.txt",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.manifest.chartPathInvalid"));
            }
        }

        private static void AddPackagePath(
            HashSet<string> paths,
            string path,
            string label)
        {
            if (!IsSafeRelativePackagePath(path))
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.manifest.packagePathInvalid",
                        ("label", label)));
            }
            if (!paths.Add(path))
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.manifest.duplicatePackagePath",
                        ("path", path)));
            }
        }

        private static bool IsSafeRelativePackagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                path.IndexOf('\\') >= 0 ||
                path.IndexOf(':') >= 0 ||
                path.IndexOf('\0') >= 0 ||
                path.StartsWith("/", StringComparison.Ordinal))
            {
                return false;
            }
            var parts = path.Split('/');
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0 ||
                    parts[i] == "." ||
                    parts[i] == "..")
                {
                    return false;
                }
            }
            return true;
        }
    }
}
