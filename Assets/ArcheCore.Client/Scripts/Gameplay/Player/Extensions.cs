using UnityEngine;

namespace ArcheCore.Client.Gameplay.Player
{
    public static class Extensions
    {
        // Is floatA equal to zero? Takes floating point inaccuracy into account, by using Epsilon.
        public static bool IsEqualToZero(this float floatA)
        {
            return Mathf.Abs(floatA) < Mathf.Epsilon;
        }

        // Is floatA not equal to zero? Takes floating point inaccuracy into account, by using Epsilon.
        public static bool NotEqualToZero(this float floatA)
        {
            return Mathf.Abs(floatA) > Mathf.Epsilon;
        }
    }
}