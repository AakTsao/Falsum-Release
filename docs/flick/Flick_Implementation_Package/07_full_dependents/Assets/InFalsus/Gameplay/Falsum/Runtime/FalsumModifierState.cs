using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// The single place that answers "is Ctrl held right now".
    ///
    /// WHY IT EXISTS: the editor and the middle-edit partial each read
    /// <c>Input.GetKey(KeyCode.LeftControl)</c> directly in four places. A
    /// touch build has no Ctrl key at all, so the phone UI needs a LATCHED
    /// equivalent -- press the multi-select button and the next taps behave
    /// as if Ctrl were held, press it again to release.
    ///
    /// Routing every check through here means the latch cannot be honoured
    /// in one path and silently ignored in another: the selection code, the
    /// pick-cycle code and the middle edit all see the same answer. That
    /// asymmetry is exactly the kind of bug that is invisible until someone
    /// tries to multi-select on a phone and only half of it works.
    /// </summary>
    public static class FalsumModifierState
    {
        /// <summary>
        /// Latched Ctrl from the touch UI. Deliberately independent of the
        /// hardware key: a latched state plus a physically held key are
        /// both "Ctrl held", and OR-ing them is what makes the desktop
        /// behaviour unchanged when the latch is off (its default).
        /// </summary>
        public static bool LatchedControl { get; set; }

        /// <summary>Ctrl held, by physical key or by the touch latch.</summary>
        public static bool Control
        {
            get
            {
                if (LatchedControl)
                {
                    return true;
                }
                return Input.GetKey(KeyCode.LeftControl) ||
                       Input.GetKey(KeyCode.RightControl);
            }
        }

        /// <summary>
        /// Clears the latch. Called when the editor loses focus or the
        /// mobile layout is torn down, so a latched selection mode cannot
        /// outlive the surface that latched it.
        /// </summary>
        public static void ResetLatches()
        {
            LatchedControl = false;
        }
    }
}
