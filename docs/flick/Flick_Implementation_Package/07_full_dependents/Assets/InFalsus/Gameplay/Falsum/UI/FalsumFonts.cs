using UnityEngine;

namespace InFalsus.Gameplay.Falsum.UI
{
    public static class FalsumFonts
    {
        private const string BundledCjkResource =
            "Falsum/NotoSansSC-Medium";

        private static Font _cjk;

        public static Font Cjk
        {
            get
            {
                if (_cjk == null)
                {
                    _cjk = LoadCjk();
                }
                return _cjk;
            }
        }

        private static Font LoadCjk()
        {
            var bundled = Resources.Load<Font>(
                BundledCjkResource);
            if (bundled != null)
            {
                Debug.Log(
                    "[Falsum] CJK UI font: " + bundled.name);
                return bundled;
            }

            Debug.LogWarning(
                "[Falsum] Bundled CJK font is missing; " +
                "trying installed OS fonts.");
            var osFont = Font.CreateDynamicFontFromOSFont(
                new[]
                {
                    "Source Han Sans SC",
                    "Source Han Sans CN",
                    "Noto Sans CJK SC",
                    "Noto Sans SC",
                    "Microsoft YaHei UI",
                    "Microsoft YaHei",
                    "PingFang SC",
                    "WenQuanYi Micro Hei",
                    "Segoe UI",
                },
                20);
            if (osFont != null)
            {
                Debug.Log(
                    "[Falsum] CJK UI font: OS " +
                    osFont.name);
                return osFont;
            }

            Debug.LogError(
                "[Falsum] No CJK UI font is available.");
            return Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
        }
    }
}
