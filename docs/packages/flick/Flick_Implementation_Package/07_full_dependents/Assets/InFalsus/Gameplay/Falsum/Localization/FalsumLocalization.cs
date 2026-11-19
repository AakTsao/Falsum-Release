using System;
using System.Collections.Generic;
using System.Globalization;
using InFalsus.Gameplay.Falsum.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace InFalsus.Gameplay.Falsum.Localization
{
    public static class FalsumLocalization
    {
        public const string EnglishUnitedStates = "en-US";
        public const string ChineseSimplified = "zh-CN";

        private const string ResourceRoot = "Falsum/Localization/";
        private const string LocalizedTextPrefix = "l10n.";
        private const string LocalizedTooltipPrefix = "l10nt.";

        private static readonly string[] LocaleCodes =
        {
            ChineseSimplified,
            EnglishUnitedStates,
        };

        private static readonly Dictionary<string, string> Strings =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> FallbackStrings =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static string _locale = string.Empty;
        private static string _fallbackLocale = string.Empty;
        private static CultureInfo _formatCulture =
            CultureInfo.InvariantCulture;
        private static bool _initialized;

        public static event Action LocaleChanged;

        public static string Locale
        {
            get
            {
                EnsureInitialized();
                return _locale;
            }
        }

        public static IList<string> SupportedLocales
        {
            get { return Array.AsReadOnly(LocaleCodes); }
        }

        public static void Initialize(string preferredLocale = null)
        {
            var locale = NormalizeLocale(preferredLocale);
            LoadLocale(locale);
            _initialized = true;
        }

        public static bool SetLocale(string locale)
        {
            var normalized = NormalizeLocale(locale);
            if (_initialized &&
                string.Equals(
                    normalized,
                    _locale,
                    StringComparison.Ordinal))
            {
                return false;
            }

            LoadLocale(normalized);
            _initialized = true;
            var handler = LocaleChanged;
            if (handler != null)
            {
                handler();
            }
            return true;
        }

        public static bool IsSupportedLocale(string locale)
        {
            var normalized = NormalizeLocale(locale);
            for (var i = 0; i < LocaleCodes.Length; i++)
            {
                if (string.Equals(
                        LocaleCodes[i],
                        normalized,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public static string Get(
            string key,
            params (string Name, object Value)[] variables)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            EnsureInitialized();
            string template;
            if (!Strings.TryGetValue(key, out template) &&
                !FallbackStrings.TryGetValue(key, out template))
            {
                return key;
            }
            if (variables == null || variables.Length == 0)
            {
                return template;
            }
            for (var i = 0; i < variables.Length; i++)
            {
                var variable = variables[i];
                if (string.IsNullOrEmpty(variable.Name))
                {
                    continue;
                }
                var placeholder = "{" + variable.Name + "}";
                template = template.Replace(
                    placeholder,
                    FormatValue(variable.Value));
            }
            return template;
        }

        public static bool TryGet(
            string key,
            out string value)
        {
            value = string.Empty;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            EnsureInitialized();
            if (Strings.TryGetValue(key, out value))
            {
                return true;
            }
            return FallbackStrings.TryGetValue(key, out value);
        }

        public static void Bind(
            TextElement element,
            string key)
        {
            if (element != null)
            {
                element.text = Get(key);
            }
        }

        public static void BindTooltip(
            VisualElement element,
            string key)
        {
            if (element != null)
            {
                element.tooltip = Get(key);
            }
        }

        public static void ApplyStaticText(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            root.Query<VisualElement>().ForEach(
                element =>
                {
                    var name = element.name;
                    if (string.IsNullOrEmpty(name))
                    {
                        return;
                    }
                    if (name.StartsWith(
                            LocalizedTextPrefix,
                            StringComparison.Ordinal))
                    {
                        var textElement = element as TextElement;
                        if (textElement != null)
                        {
                            textElement.text = Get(
                                name.Substring(
                                    LocalizedTextPrefix.Length));
                        }
                        return;
                    }
                    if (name.StartsWith(
                            LocalizedTooltipPrefix,
                            StringComparison.Ordinal))
                    {
                        element.tooltip = Get(
                            name.Substring(
                                LocalizedTooltipPrefix.Length));
                    }
                });
        }

        private static void EnsureInitialized()
        {
            if (!_initialized)
            {
                Initialize(DetectSystemLocale());
            }
        }

        private static void LoadLocale(string locale)
        {
            Strings.Clear();
            FallbackStrings.Clear();

            _locale = locale;
            _fallbackLocale = string.Equals(
                locale,
                ChineseSimplified,
                StringComparison.Ordinal)
                ? EnglishUnitedStates
                : ChineseSimplified;
            _formatCulture = CultureInfo.GetCultureInfo(locale);

            LoadDictionary(locale, Strings);
            LoadDictionary(_fallbackLocale, FallbackStrings);
        }

        private static void LoadDictionary(
            string locale,
            Dictionary<string, string> target)
        {
            var asset = Resources.Load<TextAsset>(
                ResourceRoot + locale);
            if (asset == null)
            {
                return;
            }

            try
            {
                var root = FalsumJson.AsObject(
                    FalsumJson.Parse(asset.text));
                foreach (var pair in root)
                {
                    var text = pair.Value as string;
                    if (text != null)
                    {
                        target[pair.Key] = text;
                    }
                }
            }
            catch (Exception)
            {
                target.Clear();
            }
        }

        private static string NormalizeLocale(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
            {
                return DetectSystemLocale();
            }

            var normalized = locale
                .Trim()
                .Replace('_', '-')
                .ToLowerInvariant();
            if (normalized.StartsWith("zh", StringComparison.Ordinal))
            {
                return ChineseSimplified;
            }
            if (normalized.StartsWith("en", StringComparison.Ordinal))
            {
                return EnglishUnitedStates;
            }
            return DetectSystemLocale();
        }

        private static string DetectSystemLocale()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional:
                    return ChineseSimplified;
                default:
                    return EnglishUnitedStates;
            }
        }

        private static string FormatValue(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            var formattable = value as IFormattable;
            return formattable != null
                ? formattable.ToString(null, _formatCulture)
                : Convert.ToString(value, _formatCulture);
        }
    }
}
