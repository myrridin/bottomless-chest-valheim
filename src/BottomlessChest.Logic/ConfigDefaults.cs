using System;

namespace BottomlessChest.Logic
{
    public static class ConfigDefaults
    {
        /// <summary>
        /// Moves a config value from an old default to a new one, if nobody changed it.
        /// </summary>
        /// <remarks>
        /// BepInEx keeps whatever a config file already holds, so a new default only reaches
        /// fresh installs - and most players never open the file. A value still exactly equal
        /// to the old default is taken as never chosen and replaced. Anything else, however
        /// slightly different, was edited on purpose and is kept.
        /// </remarks>
        public static string Upgrade(string stored, string previousDefault, string currentDefault) =>
            string.Equals(stored, previousDefault, StringComparison.Ordinal) ? currentDefault : stored;
    }
}
