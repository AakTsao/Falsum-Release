using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public static class FalsumFileDialog
    {
#if UNITY_EDITOR
        public static string OpenFalsum()
        {
            return UnityEditor.EditorUtility.OpenFilePanel(
                L.Get("falsum.fileDialog.importPackage"),
                string.Empty,
                "falsum");
        }

        public static string SaveFalsum(string suggestedName)
        {
            return UnityEditor.EditorUtility.SaveFilePanel(
                L.Get("falsum.fileDialog.savePackage"),
                string.Empty,
                suggestedName,
                "falsum");
        }

        public static string OpenAudio()
        {
            return UnityEditor.EditorUtility.OpenFilePanel(
                L.Get("falsum.fileDialog.chooseAudio"),
                string.Empty,
                "wav,mp3,ogg,flac,m4a,aac");
        }

        public static string OpenImage()
        {
            return UnityEditor.EditorUtility.OpenFilePanel(
                L.Get("falsum.fileDialog.chooseImage"),
                string.Empty,
                "png,jpg,jpeg,bmp,tga");
        }

        public static string OpenToolFile(
            IList<string> extensions)
        {
            return UnityEditor.EditorUtility.OpenFilePanel(
                L.Get("falsum.fileDialog.chooseToolFile"),
                string.Empty,
                JoinExtensions(extensions));
        }

        public static string SaveToolFile(
            string suggestedName,
            IList<string> extensions)
        {
            return UnityEditor.EditorUtility.SaveFilePanel(
                L.Get("falsum.fileDialog.saveToolFile"),
                string.Empty,
                suggestedName ?? string.Empty,
                JoinExtensions(extensions));
        }

        public static string OpenSkyAreaBank()
        {
            return UnityEditor.EditorUtility.OpenFilePanel(
                L.Get("falsum.fileDialog.importSkyAreaBank"),
                string.Empty,
                "fsky");
        }

        public static string SaveSkyAreaBank(string suggestedName)
        {
            return UnityEditor.EditorUtility.SaveFilePanel(
                L.Get("falsum.fileDialog.exportSkyAreaBank"),
                string.Empty,
                suggestedName ?? string.Empty,
                "fsky");
        }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        public static string OpenFalsum()
        {
            return EnsureFalsumExtension(OpenFile(
                L.Get("falsum.fileDialog.importPackage"),
                L.Get("falsum.fileDialog.filter.falsum"),
                "falsum"));
        }

        public static string SaveFalsum(string suggestedName)
        {
            return EnsureFalsumExtension(SaveFile(
                L.Get("falsum.fileDialog.savePackage"),
                suggestedName,
                L.Get("falsum.fileDialog.filter.falsum"),
                "falsum"));
        }

        public static string OpenAudio()
        {
            return OpenFile(
                L.Get("falsum.fileDialog.chooseAudio"),
                L.Get("falsum.fileDialog.filter.audio") +
                L.Get("falsum.fileDialog.filter.allFiles"),
                string.Empty);
        }

        public static string OpenImage()
        {
            return OpenFile(
                L.Get("falsum.fileDialog.chooseImage"),
                L.Get("falsum.fileDialog.filter.image") +
                L.Get("falsum.fileDialog.filter.allFiles"),
                string.Empty);
        }

        public static string OpenToolFile(
            IList<string> extensions)
        {
            return OpenFile(
                L.Get("falsum.fileDialog.chooseToolFile"),
                BuildToolPattern(extensions) +
                L.Get("falsum.fileDialog.filter.allFiles"),
                string.Empty);
        }

        public static string SaveToolFile(
            string suggestedName,
            IList<string> extensions)
        {
            var defaultExtension = extensions != null &&
                                   extensions.Count > 0
                ? extensions[0]
                : string.Empty;
            return SaveFile(
                L.Get("falsum.fileDialog.saveToolFile"),
                suggestedName,
                BuildToolPattern(extensions) +
                L.Get("falsum.fileDialog.filter.allFiles"),
                defaultExtension);
        }

        public static string OpenSkyAreaBank()
        {
            return OpenFile(
                L.Get("falsum.fileDialog.importSkyAreaBank"),
                L.Get("falsum.fileDialog.filter.skyAreaBank"),
                "fsky");
        }

        public static string SaveSkyAreaBank(string suggestedName)
        {
            return SaveFile(
                L.Get("falsum.fileDialog.exportSkyAreaBank"),
                suggestedName,
                L.Get("falsum.fileDialog.filter.skyAreaBank"),
                "fsky");
        }
#else
        public static string OpenFalsum()
        {
            return string.Empty;
        }

        public static string SaveFalsum(string suggestedName)
        {
            return string.Empty;
        }

        public static string OpenAudio()
        {
            return string.Empty;
        }

        public static string OpenImage()
        {
            return string.Empty;
        }

        public static string OpenToolFile(
            IList<string> extensions)
        {
            return string.Empty;
        }

        public static string SaveToolFile(
            string suggestedName,
            IList<string> extensions)
        {
            return string.Empty;
        }

        public static string OpenSkyAreaBank()
        {
            return string.Empty;
        }

        public static string SaveSkyAreaBank(string suggestedName)
        {
            return string.Empty;
        }
#endif

        public static string EnsureFalsumExtension(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }
            return string.Equals(
                       Path.GetExtension(path),
                       ".falsum",
                       StringComparison.OrdinalIgnoreCase)
                ? path
                : Path.ChangeExtension(path, ".falsum");
        }

        /// <summary>
        /// Forces the .fsky extension. Unlike a package, a share-code file is
        /// plain text; a wrong extension would only confuse forwards, so the
        /// same rule as <see cref="EnsureFalsumExtension"/> applies.
        /// </summary>
        public static string EnsureSkyAreaBankExtension(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }
            return string.Equals(
                       Path.GetExtension(path),
                       ".fsky",
                       StringComparison.OrdinalIgnoreCase)
                ? path
                : Path.ChangeExtension(path, ".fsky");
        }

#if UNITY_EDITOR
        private static string JoinExtensions(
            IList<string> extensions)
        {
            return extensions == null ||
                   extensions.Count == 0
                ? string.Empty
                : string.Join(",", extensions);
        }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static string BuildToolPattern(
            IList<string> extensions)
        {
            if (extensions == null || extensions.Count == 0)
            {
                return L.Get("falsum.fileDialog.filter.toolAll");
            }
            var builder = new StringBuilder();
            var labels = new StringBuilder();
            for (var i = 0; i < extensions.Count; i++)
            {
                if (i > 0)
                {
                    labels.Append("; ");
                }
                labels.Append("*.");
                labels.Append(extensions[i]);
            }
            var patterns = new StringBuilder();
            for (var i = 0; i < extensions.Count; i++)
            {
                if (i > 0)
                {
                    patterns.Append(';');
                }
                patterns.Append("*.");
                patterns.Append(extensions[i]);
            }
            return L.Get(
                "falsum.fileDialog.filter.tool",
                ("extensions", labels),
                ("patterns", patterns));
        }
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int FileBufferChars = 4096;

        [StructLayout(LayoutKind.Sequential)]
        private struct OpenFileName
        {
            public int structSize;
            public IntPtr dlgOwner;
            public IntPtr instance;
            public IntPtr filter;
            public IntPtr customFilter;
            public int maxCustFilter;
            public int filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            public IntPtr initialDir;
            public IntPtr title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public IntPtr defExt;
            public IntPtr custData;
            public IntPtr hook;
            public IntPtr templateName;
            public IntPtr reservedPtr;
            public int reservedInt;
            public int flagsEx;
        }

        [DllImport(
            "comdlg32.dll",
            SetLastError = true,
            CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        private static extern bool GetOpenFileNameW(
            ref OpenFileName data);

        [DllImport(
            "comdlg32.dll",
            SetLastError = true,
            CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        private static extern bool GetSaveFileNameW(
            ref OpenFileName data);

        private static string OpenFile(
            string title,
            string filter,
            string defaultExtension)
        {
            return Show(
                title,
                null,
                filter,
                false,
                defaultExtension);
        }

        private static string SaveFile(
            string title,
            string suggestedName,
            string filter,
            string defaultExtension)
        {
            return Show(
                title,
                suggestedName,
                filter,
                true,
                defaultExtension);
        }

        private static string Show(
            string title,
            string suggestedName,
            string filter,
            bool save,
            string defaultExtension)
        {
            const int OfnOverwritePrompt = 0x00000002;
            const int OfnNoChangeDir = 0x00000008;
            const int OfnPathMustExist = 0x00000800;
            const int OfnFileMustExist = 0x00001000;
            const int OfnExplorer = 0x00080000;

            var fileBuffer = Marshal.AllocHGlobal(
                FileBufferChars * sizeof(char));
            var filterBuffer = IntPtr.Zero;
            var titleBuffer = IntPtr.Zero;
            var extensionBuffer = IntPtr.Zero;
            try
            {
                var initialName = suggestedName ?? string.Empty;
                if (initialName.Length >= FileBufferChars)
                {
                    initialName = initialName.Substring(
                        0,
                        FileBufferChars - 1);
                }
                if (initialName.Length > 0)
                {
                    Marshal.Copy(
                        initialName.ToCharArray(),
                        0,
                        fileBuffer,
                        initialName.Length);
                }
                Marshal.WriteInt16(
                    fileBuffer,
                    initialName.Length * sizeof(char),
                    0);

                filterBuffer = Marshal.StringToHGlobalUni(filter);
                titleBuffer = Marshal.StringToHGlobalUni(title);
                extensionBuffer = Marshal.StringToHGlobalUni(
                    defaultExtension ?? string.Empty);

                var data = new OpenFileName
                {
                    structSize = IntPtr.Size == 8 ? 152 : 88,
                    filter = filterBuffer,
                    filterIndex = 1,
                    file = fileBuffer,
                    maxFile = FileBufferChars,
                    title = titleBuffer,
                    flags = OfnExplorer | OfnNoChangeDir |
                            (save
                                ? OfnOverwritePrompt | OfnPathMustExist
                                : OfnFileMustExist | OfnPathMustExist),
                    defExt = extensionBuffer,
                };
                var accepted = save
                    ? GetSaveFileNameW(ref data)
                    : GetOpenFileNameW(ref data);
                if (!accepted)
                {
                    return string.Empty;
                }

                var selectedName = new char[FileBufferChars];
                Marshal.Copy(
                    fileBuffer,
                    selectedName,
                    0,
                    selectedName.Length);
                var length = Array.IndexOf(selectedName, '\0');
                if (length < 0)
                {
                    length = selectedName.Length;
                }
                return new string(selectedName, 0, length);
            }
            finally
            {
                Marshal.FreeHGlobal(fileBuffer);
                FreeBuffer(filterBuffer);
                FreeBuffer(titleBuffer);
                FreeBuffer(extensionBuffer);
            }
        }

        private static void FreeBuffer(IntPtr buffer)
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
#endif
    }
}
