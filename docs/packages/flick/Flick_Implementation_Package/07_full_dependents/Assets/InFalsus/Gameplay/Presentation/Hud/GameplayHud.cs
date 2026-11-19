using System.Globalization;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Judgement;
using InFalsus.Gameplay.Core.Timing;
using UnityEngine;

namespace InFalsus.Gameplay.Presentation.Hud
{
    public sealed class GameplayHud : MonoBehaviour
    {
        private JudgementEngine _engine;
        private IGameplayClock _clock;
        private Chart _chart;
        private string _songTitle;
        private string _difficulty;
        private double _speed;
        private GUIStyle _small;
        private GUIStyle _number;
        private GUIStyle _right;
        private GUIStyle _center;
        private Texture2D _pixel;

        public void Initialize(
            JudgementEngine engine,
            IGameplayClock clock,
            Chart chart,
            string songTitle,
            string difficulty,
            double speed)
        {
            _engine = engine;
            _clock = clock;
            _chart = chart;
            _songTitle = songTitle;
            _difficulty = difficulty;
            _speed = speed;
        }

        public void SetRuntime(
            JudgementEngine engine,
            Chart chart,
            string songTitle,
            string difficulty,
            double speed)
        {
            _engine = engine;
            _chart = chart;
            _songTitle = songTitle;
            _difficulty = difficulty;
            _speed = speed;
        }

        private void Awake()
        {
            _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
        }

        private void OnDestroy()
        {
            if (_pixel != null)
            {
                Destroy(_pixel);
            }
        }

        private void OnGUI()
        {
            if (_engine == null || _clock == null || _chart == null)
            {
                return;
            }

            var unit = Mathf.Max(0.72f, Screen.height / 720.0f);
            EnsureStyles(unit);

            var margin = 22.0f * unit;
            var panelWidth = 330.0f * unit;
            var panelHeight = 88.0f * unit;

            DrawPanel(new Rect(margin, margin, panelWidth, panelHeight));
            DrawPanel(new Rect(
                Screen.width - margin - panelWidth,
                margin,
                panelWidth,
                74.0f * unit));

            GUI.color = Color.white;
            GUI.Label(
                new Rect(margin + 14.0f * unit, margin + 8.0f * unit,
                    panelWidth, 18.0f * unit),
                "SCORE",
                _small);
            GUI.Label(
                new Rect(margin + 14.0f * unit, margin + 25.0f * unit,
                    panelWidth - 28.0f * unit, 44.0f * unit),
                _engine.Score.ToString("N0", CultureInfo.InvariantCulture),
                _number);
            GUI.Label(
                new Rect(margin + 14.0f * unit, margin + 68.0f * unit,
                    panelWidth - 28.0f * unit, 18.0f * unit),
                "COMBO  " + _engine.Combo.ToString(CultureInfo.InvariantCulture) +
                "   MAX " + _engine.MaxCombo.ToString(CultureInfo.InvariantCulture),
                _small);

            var rightX = Screen.width - margin - panelWidth + 14.0f * unit;
            GUI.Label(
                new Rect(rightX, margin + 8.0f * unit,
                    panelWidth - 28.0f * unit, 24.0f * unit),
                _songTitle,
                _right);
            GUI.Label(
                new Rect(rightX, margin + 31.0f * unit,
                    panelWidth - 28.0f * unit, 18.0f * unit),
                "DIFFICULTY " + _difficulty,
                _right);
            GUI.Label(
                new Rect(rightX, margin + 49.0f * unit,
                    panelWidth - 28.0f * unit, 18.0f * unit),
                "BPM " + _chart.BpmAt(_clock.TimeMs).ToString("0.##", CultureInfo.InvariantCulture) +
                "   ACC " + (_engine.Accuracy * 100.0).ToString("0.00", CultureInfo.InvariantCulture) + "%",
                _right);

            var barX = margin;
            var barY = Screen.height - margin - 16.0f * unit;
            var barW = Screen.width - margin * 2.0f;
            var barH = 4.0f * unit;
            GUI.color = new Color(0.08f, 0.1f, 0.14f, 0.8f);
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), _pixel);

            var duration = _chart.EndTimeMs;
            var progress = duration > 0.0 ? Mathf.Clamp01((float)(_clock.TimeMs / duration)) : 0.0f;
            GUI.color = new Color(0.35f, 0.9f, 0.95f, 0.95f);
            GUI.DrawTexture(new Rect(barX, barY, barW * progress, barH), _pixel);

            GUI.color = Color.white;
            GUI.Label(
                new Rect(barX, barY - 21.0f * unit, barW, 18.0f * unit),
                _clock.TimeMs.ToString("0.000", CultureInfo.InvariantCulture) + " / " +
                duration.ToString("0.000", CultureInfo.InvariantCulture) +
                "   SPEED " + _speed.ToString("0.##", CultureInfo.InvariantCulture),
                _center);

            if (!_clock.IsPlaying)
            {
                GUI.Label(
                    new Rect(0.0f, Screen.height * 0.5f - 24.0f * unit,
                        Screen.width, 48.0f * unit),
                    "PAUSED",
                    _center);
            }
        }

        private void DrawPanel(Rect rect)
        {
            GUI.color = new Color(0.02f, 0.03f, 0.05f, 0.62f);
            GUI.DrawTexture(rect, _pixel);
            GUI.color = new Color(0.25f, 0.75f, 0.84f, 0.55f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2.0f), _pixel);
            GUI.color = Color.white;
        }

        private void EnsureStyles(float unit)
        {
            var smallSize = Mathf.RoundToInt(12.0f * unit);
            var numberSize = Mathf.RoundToInt(31.0f * unit);
            var titleSize = Mathf.RoundToInt(16.0f * unit);

            if (_small == null || _small.fontSize != smallSize)
            {
                _small = new GUIStyle(GUI.skin.label)
                {
                    fontSize = smallSize,
                    normal = { textColor = new Color(0.68f, 0.82f, 0.9f, 1.0f) },
                    alignment = TextAnchor.MiddleLeft,
                };
                _number = new GUIStyle(GUI.skin.label)
                {
                    fontSize = numberSize,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.87f, 0.98f, 1.0f, 1.0f) },
                    alignment = TextAnchor.MiddleLeft,
                };
                _right = new GUIStyle(GUI.skin.label)
                {
                    fontSize = titleSize,
                    normal = { textColor = new Color(0.87f, 0.93f, 0.98f, 1.0f) },
                    alignment = TextAnchor.MiddleRight,
                };
                _center = new GUIStyle(GUI.skin.label)
                {
                    fontSize = smallSize,
                    normal = { textColor = new Color(0.78f, 0.86f, 0.9f, 1.0f) },
                    alignment = TextAnchor.MiddleCenter,
                };
            }
        }
    }
}
