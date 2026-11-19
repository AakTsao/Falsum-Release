using System;
using System.Globalization;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// Chart info page: song metadata, difficulty cards, media rows.
    ///
    /// Unlike every other page this one edits the <em>document</em>
    /// (<see cref="FalsumChartMetadataDraft"/>) rather than global
    /// preferences, which is why it is being split out into its own panel in
    /// P3. The host supplies the draft, the package and the media actions; the
    /// page never touches the shell's cover-texture cache, it only asks the
    /// host to browse.
    /// </summary>
    internal sealed class FalsumChartInfoPage
    {
        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;

        /// <summary>
        /// Cover preview element, exposed so the panel can push a converted
        /// cover texture into it after the user picks a file.
        /// </summary>
        public VisualElement CoverPreview { get; private set; }

        private Texture2D _coverTexture;

        public void Build(
            VisualElement content,
            IFalsumPreferencesPageHost host)
        {
            var draft = host.MetadataDraft;
            var package = host.Package;
            if (draft == null || !draft.HasPackage || package == null)
            {
                var empty = new Label(
                    L.Get("falsum.error.metadata.packageRequired"));
                empty.AddToClassList("preferences-empty");
                content.Add(empty);
                return;
            }

            var path = new Label(
                package.SourcePath ??
                L.Get("falsum.preferences.chart.pathUnknown"));
            path.AddToClassList("preferences-path");
            content.Add(path);

            var hero = new VisualElement();
            hero.AddToClassList("preferences-chart-hero");
            CoverPreview = new VisualElement();
            CoverPreview.AddToClassList("preferences-cover");
            hero.Add(CoverPreview);
            ApplyCover(host, draft);

            var fields = new VisualElement();
            fields.AddToClassList("preferences-chart-fields");
            fields.Add(CreateFormField(
                L.Get("falsum.preferences.chart.songId"),
                draft.SongId,
                value =>
                {
                    draft.SongId = value;
                },
                true));
            fields.Add(CreateFormField(
                L.Get("falsum.preferences.chart.title"),
                draft.Title,
                value =>
                {
                    draft.Title = value;
                },
                true));
            var names = new VisualElement();
            names.AddToClassList("preferences-form-row");
            names.Add(CreateFormField(
                L.Get("falsum.preferences.chart.artist"),
                draft.Artist,
                value =>
                {
                    draft.Artist = value;
                },
                false));
            names.Add(CreateFormField(
                L.Get("falsum.preferences.chart.jacketDesigner"),
                draft.JacketDesigner,
                value =>
                {
                    draft.JacketDesigner = value;
                },
                false));
            fields.Add(names);
            hero.Add(fields);
            content.Add(hero);

            var media = FalsumPreferencesWidgets.CreateGroup(
                L.Get("falsum.preferences.chart.mediaGroup"));
            media.Add(CreateMediaRow(
                L.Get("falsum.preferences.chart.mediaType.audio"),
                L.Get(
                    "falsum.preferences.chart.audio",
                    ("name", AudioName(draft, package))),
                AudioMeta(draft),
                L.Get("falsum.preferences.chart.replaceAudio"),
                host.BrowseAudio));
            media.Add(CreateMediaRow(
                L.Get("falsum.preferences.chart.mediaType.cover"),
                L.Get(
                    "falsum.preferences.chart.cover",
                    ("name", CoverName(draft, package))),
                CoverMeta(draft),
                L.Get("falsum.preferences.chart.replaceCover"),
                host.BrowseCover));
            media.Add(CreateMediaRow(
                L.Get("falsum.preferences.chart.mediaType.background"),
                L.Get(
                    "falsum.preferences.chart.background",
                    ("name", draft.BackgroundFile ?? string.Empty)),
                L.Get(
                    "falsum.preferences.chart.backgroundDescription"),
                L.Get("falsum.preferences.chart.preview"),
                host.OpenBackgroundPreview));
            media.Add(BuildDifficultyStrip(host, draft, package));
            content.Add(media);
        }

        private static VisualElement CreateFormField(
            string label,
            string value,
            Action<string> changed,
            bool fullWidth)
        {
            var field = new VisualElement();
            field.AddToClassList("preferences-form-field");
            var fieldLabel = new Label(label);
            fieldLabel.AddToClassList("preferences-form-label");
            field.Add(fieldLabel);
            var input = new TextField();
            input.value = value ?? string.Empty;
            input.AddToClassList("preferences-field");
            input.RegisterValueChangedCallback(
                evt => changed(evt.newValue ?? string.Empty));
            field.Add(input);
            if (fullWidth)
            {
                field.style.width = new Length(100, LengthUnit.Percent);
            }
            return field;
        }

        private static VisualElement CreateMediaRow(
            string tag,
            string title,
            string meta,
            string action,
            Action clicked)
        {
            var row = new VisualElement();
            row.AddToClassList("preferences-media-row");
            var tagLabel = new Label(tag);
            tagLabel.AddToClassList("preferences-badge");
            tagLabel.AddToClassList("ready");
            row.Add(tagLabel);
            var copy = new VisualElement();
            copy.AddToClassList("preferences-media-copy");
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("preferences-media-title");
            copy.Add(titleLabel);
            var metaLabel = new Label(meta);
            metaLabel.AddToClassList("preferences-media-meta");
            copy.Add(metaLabel);
            row.Add(copy);
            var button = new Button(clicked)
            {
                text = action,
            };
            button.AddToClassList("preferences-inline-button");
            row.Add(button);
            return row;
        }

        private static VisualElement BuildDifficultyStrip(
            IFalsumPreferencesPageHost host,
            FalsumChartMetadataDraft draft,
            FalsumPackage package)
        {
            var strip = new VisualElement();
            strip.AddToClassList("preferences-difficulty-strip");
            var classes = new[] { "min", "evo", "ult", "fbd" };
            for (var i = 0; i < 4; i++)
            {
                var info = package.Manifest.FindDifficulty(i);
                var available = info != null && info.Available;
                var card = new VisualElement();
                card.name = "PreferencesDifficultyCard" + i;
                card.AddToClassList("preferences-difficulty");
                card.AddToClassList(classes[i]);
                card.EnableInClassList("is-disabled", !available);

                var heading = new VisualElement();
                heading.AddToClassList("preferences-difficulty-heading");
                var title = new Label(
                    FalsumPreferencesWidgets.DifficultyDisplayName(i));
                title.AddToClassList("preferences-difficulty-title");
                heading.Add(title);
                card.Add(heading);

                if (available)
                {
                    var captured = i;
                    var rating = new TextField
                    {
                        name = "PreferencesDifficultyRating" + i,
                        value = draft.DifficultyRatings[i] ??
                                info.Rating.ToString(Invariant),
                        maxLength = 4,
                    };
                    rating.AddToClassList("preferences-difficulty-rating");
                    rating.RegisterValueChangedCallback(
                        evt =>
                        {
                            var value =
                                FalsumEditorView.SanitizeNumericInput(
                                    evt.newValue);
                            if (!string.Equals(
                                    rating.value,
                                    value,
                                    StringComparison.Ordinal))
                            {
                                rating.SetValueWithoutNotify(value);
                            }
                            draft.DifficultyRatings[captured] = value;
                        });
                    heading.Add(rating);

                    var designer = new TextField
                    {
                        name = "PreferencesDifficultyDesigner" + i,
                        value = draft.DifficultyChartDesigners[i] ??
                                info.ChartDesigner ??
                                string.Empty,
                        maxLength = 120,
                    };
                    designer.AddToClassList(
                        "preferences-difficulty-designer");
                    designer.textEdition.placeholder =
                        L.Get("falsum.preferences.chart.chartDesigner");
                    designer.RegisterValueChangedCallback(
                        evt =>
                        {
                            draft.DifficultyChartDesigners[captured] =
                                evt.newValue ?? string.Empty;
                        });
                    card.Add(designer);
                }
                else
                {
                    var rating = new Label("--");
                    rating.name = "PreferencesDifficultyRating" + i;
                    rating.AddToClassList(
                        "preferences-difficulty-rating-disabled");
                    heading.Add(rating);

                    var disabled = new TextField
                    {
                        name = "PreferencesDifficultyState" + i,
                        value = L.Get(
                            "falsum.preferences.chart.difficultyDisabled"),
                        isReadOnly = true,
                    };
                    disabled.AddToClassList(
                        "preferences-difficulty-designer");
                    disabled.AddToClassList("is-disabled");
                    disabled.pickingMode = PickingMode.Ignore;
                    disabled.Query<VisualElement>().ForEach(
                        element =>
                        {
                            element.pickingMode = PickingMode.Ignore;
                        });
                    card.Add(disabled);
                    card.tooltip = L.Get(
                        "falsum.preferences.chart.switchDifficultyTooltip");
                    card.focusable = true;
                    var captured = i;
                    card.RegisterCallback<ClickEvent>(
                        evt =>
                        {
                            evt.StopPropagation();
                            host.SwitchDifficulty(captured);
                        });
                    card.RegisterCallback<KeyDownEvent>(
                        evt =>
                        {
                            if (evt.keyCode != KeyCode.Return &&
                                evt.keyCode != KeyCode.KeypadEnter &&
                                evt.keyCode != KeyCode.Space)
                            {
                                return;
                            }
                            evt.StopImmediatePropagation();
                            host.SwitchDifficulty(captured);
                        });
                }
                strip.Add(card);
            }
            return strip;
        }

        private static string AudioName(
            FalsumChartMetadataDraft draft,
            FalsumPackage package)
        {
            if (draft == null)
            {
                return string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(draft.PendingAudioPath))
            {
                return System.IO.Path.GetFileName(draft.PendingAudioPath);
            }
            return package != null && package.Manifest != null
                ? package.Manifest.AudioFile
                : "audio.ogg";
        }

        private static string AudioMeta(FalsumChartMetadataDraft draft)
        {
            if (draft == null)
            {
                return string.Empty;
            }
            var duration = TimeSpan.FromMilliseconds(
                Math.Max(0, draft.AudioDurationMs));
            return L.Get(
                "falsum.preferences.chart.audioMeta",
                ("duration", duration.ToString(
                    @"mm\:ss\.fff",
                    Invariant)));
        }

        private static string CoverName(
            FalsumChartMetadataDraft draft,
            FalsumPackage package)
        {
            if (draft != null &&
                !string.IsNullOrWhiteSpace(draft.PendingCoverPath))
            {
                return System.IO.Path.GetFileName(draft.PendingCoverPath);
            }
            return package != null &&
                   package.Manifest != null &&
                   !string.IsNullOrWhiteSpace(package.Manifest.CoverFile)
                ? package.Manifest.CoverFile
                : "cover.png";
        }

        private static string CoverMeta(FalsumChartMetadataDraft draft)
        {
            var width = draft != null ? draft.CoverWidth : 1024;
            var height = draft != null ? draft.CoverHeight : 1024;
            return L.Get(
                "falsum.preferences.chart.coverSize",
                ("width", width),
                ("height", height));
        }

        /// <summary>
        /// Paints the cover into the preview element.
        ///
        /// The panel previously rendered an empty "preferences-cover" box: the
        /// texture plumbing lived in the preferences view and was never called
        /// once the chart page moved out of that dialog, so the cover was
        /// simply blank. The page now owns its own texture.
        ///
        /// Precedence: an unsaved cover the user just picked wins, then the
        /// song-info card texture the dialog was opened with, then the bytes
        /// stored in the package.
        /// </summary>
        private void ApplyCover(
            IFalsumPreferencesPageHost host,
            FalsumChartMetadataDraft draft)
        {
            if (!draft.HasPendingCover &&
                host.SharedCoverTexture != null)
            {
                CopySharedCover(host.SharedCoverTexture, draft.CoverBytes);
                return;
            }
            SetCoverBytes(draft.CoverBytes);
        }

        private void CopySharedCover(Texture2D source, byte[] fallbackBytes)
        {
            ReleaseCoverTexture();
            if (source == null)
            {
                SetCoverBytes(fallbackBytes);
                return;
            }
            Texture2D copy = null;
            try
            {
                copy = new Texture2D(
                    source.width,
                    source.height,
                    source.format,
                    source.mipmapCount > 1);
                Graphics.CopyTexture(source, copy);
                copy.wrapMode = TextureWrapMode.Clamp;
                copy.filterMode = source.filterMode;
                _coverTexture = copy;
                if (CoverPreview != null)
                {
                    CoverPreview.style.backgroundImage =
                        new StyleBackground(copy);
                }
            }
            catch (Exception)
            {
                if (copy != null)
                {
                    FalsumPlatform.DestroyObject(copy);
                }
                SetCoverBytes(fallbackBytes);
            }
        }

        /// <summary>Pushes a freshly converted cover into the preview.</summary>
        public void SetCoverBytes(byte[] bytes)
        {
            ReleaseCoverTexture();
            if (bytes == null || bytes.Length == 0)
            {
                return;
            }
            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false);
            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                FalsumPlatform.DestroyObject(texture);
                return;
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            _coverTexture = texture;
            if (CoverPreview != null)
            {
                CoverPreview.style.backgroundImage =
                    new StyleBackground(texture);
            }
        }

        /// <summary>Frees the decoded cover; call when the panel closes.</summary>
        public void ReleaseCoverTexture()
        {
            if (_coverTexture != null)
            {
                FalsumPlatform.DestroyObject(_coverTexture);
                _coverTexture = null;
            }
            if (CoverPreview != null)
            {
                CoverPreview.style.backgroundImage =
                    new StyleBackground();
            }
        }
    }
}
