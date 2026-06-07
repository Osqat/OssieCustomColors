using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Cinnamon.UI;
using HarmonyLib;
using UnityEngine;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]
[assembly: Cinnamon.AutoUpdate("Osqat/OssieCustomColors")]

namespace OssieCustomColors
{
    [BepInPlugin("com.osqat.customcolors", "OssieCustomColors", "1.0.0")]
    [BepInDependency("com.osqat.cinnamon", BepInDependency.DependencyFlags.HardDependency)]
    public partial class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static ConfigEntry<string> SavedColors;
        public static ConfigEntry<float> ColorCorrectionExponent;
        public static ConfigEntry<float> ColorCorrectionBrightness;

        // Implemented by CalibrationTools.cs when present; no-op otherwise.
        partial void BindCalibrationConfig();

        void Awake()
        {
            Log = Logger;
            SavedColors = Config.Bind(
                "Colors",
                "SavedColors",
                "",
                "Saved custom colors as comma-separated RRGGBB hex values");

            ColorCorrectionExponent = Config.Bind(
                "Colors",
                "ColorCorrectionExponent",
                2.4f,
                "Fallback exponent used when a color has no LUT entry. Darkens display colors before sending to the game renderer.");

            ColorCorrectionBrightness = Config.Bind(
                "Colors",
                "ColorCorrectionBrightness",
                0.8f,
                "Fallback brightness multiplier applied after exponent correction when no LUT entry exists.");

            BindCalibrationConfig();

            var go = new GameObject("OssieCustomColors");
            DontDestroyOnLoad(go);
            go.AddComponent<ColorPickerUI>();

            CursorOverlay.DebugLogging = true;
            CursorOverlay.EnsureHost();

            new Harmony("com.osqat.customcolors").PatchAll();
            Log.LogInfo("[OssieCustomColors] loaded.");
        }
    }
}
