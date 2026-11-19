using System;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// "About" page: logo, version line, credits and the disclaimer entry.
    ///
    /// The version line carries a hidden five-tap easter egg that toggles the
    /// performance window. The tap history lives here rather than in the
    /// shell, because it is state of this page: leaving the tab has to start
    /// the count over, which the old view did by resetting two shell fields
    /// from the tab switch.
    /// </summary>
    internal sealed class FalsumAboutPreferencesPage
    {
        private const int TapsRequired = 5;
        private const float TapWindowSeconds = 1.2f;

        private readonly Action _disclaimerRequested;
        private readonly Action _performanceWindowRequested;
        private int _tapCount;
        private double _lastTapAt = -1.0;

        public FalsumAboutPreferencesPage(
            Action disclaimerRequested,
            Action performanceWindowRequested)
        {
            _disclaimerRequested = disclaimerRequested;
            _performanceWindowRequested = performanceWindowRequested;
        }

        public int TapCount
        {
            get { return _tapCount; }
        }

        public void ResetTaps()
        {
            _tapCount = 0;
            _lastTapAt = -1.0;
        }

        /// <summary>Registers a tap; true when it completed the easter egg.</summary>
        public bool RegisterTap(double now)
        {
            if (_lastTapAt >= 0.0 &&
                now - _lastTapAt > TapWindowSeconds)
            {
                _tapCount = 0;
            }
            _lastTapAt = now;
            _tapCount++;
            if (_tapCount < TapsRequired)
            {
                return false;
            }
            _tapCount = 0;
            _lastTapAt = -1.0;
            return true;
        }

        public void Build(VisualElement content)
        {
            ResetTaps();
            var about = new VisualElement();
            about.AddToClassList("preferences-about");

            var hero = new VisualElement();
            hero.AddToClassList("preferences-about-hero");
            var logoTexture = Resources.Load<Texture2D>(
                "Falsum/UI/FalsumLogo");
            if (logoTexture != null)
            {
                var logo = new Image
                {
                    name = "PreferencesAboutLogo",
                    image = logoTexture,
                    scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore,
                };
                logo.AddToClassList("preferences-about-logo");
                hero.Add(logo);
            }
            else
            {
                Debug.LogWarning(
                    "[Falsum] About logo is missing at " +
                    "Resources/Falsum/UI/FalsumLogo.png.");
            }

            var product = new Label(FalsumEditorInfo.ProductName);
            product.AddToClassList("preferences-about-product");
            hero.Add(product);

            var version = new Label(FalsumEditorInfo.VersionLine);
            version.name = "PreferencesAboutVersion";
            version.pickingMode = PickingMode.Position;
            version.RegisterCallback<PointerDownEvent>(OnVersionPointerDown);
            version.AddToClassList("preferences-about-version");
            hero.Add(version);
            about.Add(hero);

            var credits = FalsumPreferencesWidgets.CreateGroup(
                FalsumEditorInfo.CreditsTitle);
            credits.AddToClassList("preferences-about-credits");
            var creditBody = new Label(FalsumEditorInfo.CreditsBody);
            creditBody.name = "PreferencesAboutCredits";
            creditBody.AddToClassList("preferences-about-credits-body");
            credits.Add(creditBody);
            about.Add(credits);

            var disclaimer = new VisualElement();
            disclaimer.AddToClassList("preferences-about-disclaimer");
            var button = new Button
            {
                name = "PreferencesAboutDisclaimerButton",
                text = FalsumEditorInfo.DisclaimerTitle,
                tooltip = L.Get("falsum.preferences.about.openDisclaimer"),
            };
            button.AddToClassList("preferences-inline-button");
            button.AddToClassList("preferences-about-disclaimer-button");
            button.clicked += delegate
            {
                Raise(_disclaimerRequested);
            };
            disclaimer.Add(button);
            about.Add(disclaimer);

            content.Add(about);
        }

        private void OnVersionPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }
            if (RegisterTap(Time.unscaledTimeAsDouble))
            {
                Raise(_performanceWindowRequested);
                evt.StopPropagation();
            }
        }

        private static void Raise(Action action)
        {
            if (action != null)
            {
                action();
            }
        }
    }
}
