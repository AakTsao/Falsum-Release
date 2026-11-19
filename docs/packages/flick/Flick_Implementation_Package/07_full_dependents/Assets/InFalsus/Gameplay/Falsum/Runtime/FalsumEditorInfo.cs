using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public static class FalsumEditorInfo
    {
        public static string ProductName
        {
            get { return L.Get("falsum.info.productName"); }
        }

        public const string Version = "0.46-Canary";
        public static string VersionLine
        {
            get
            {
                return L.Get(
                    "falsum.info.versionLine",
                    ("version", Version));
            }
        }

        public static string ProductVersionText
        {
            get
            {
                return L.Get(
                    "falsum.info.productVersion",
                    ("product", ProductName),
                    ("versionLine", VersionLine));
            }
        }

        public static string CreditsTitle
        {
            get { return L.Get("falsum.info.creditsTitle"); }
        }

        public static string CreditsBody
        {
            get { return L.Get("falsum.info.creditsBody"); }
        }

        public static string DisclaimerTitle
        {
            get { return L.Get("falsum.disclaimer.title"); }
        }

        public static string DisclaimerBody
        {
            get { return L.Get("falsum.info.disclaimerBody"); }
        }
    }
}
