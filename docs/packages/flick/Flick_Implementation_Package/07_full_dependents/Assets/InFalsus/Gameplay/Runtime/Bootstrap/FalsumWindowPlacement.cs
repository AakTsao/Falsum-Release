using UnityEngine;

namespace InFalsus.Gameplay.Runtime.Bootstrap
{
    public static class FalsumWindowPlacement
    {
        public static void CenterMainWindow()
        {
            if (Application.isEditor)
            {
                return;
            }

            var display = Screen.mainWindowDisplayInfo;
            var workArea = display.workArea;
            var x = workArea.x +
                    Mathf.Max(0, (workArea.width - Screen.width) / 2);
            var y = workArea.y +
                    Mathf.Max(0, (workArea.height - Screen.height) / 2);
            Screen.MoveMainWindowTo(
                display,
                new Vector2Int(x, y));
        }
    }
}
