using System;
using System.Collections.Generic;

namespace Verse
{
    public static class Log
    {
        public static void Message(string text) => Console.WriteLine(text);
        public static void Warning(string text) => Console.WriteLine("[WARN] " + text);
        public static void Error(string text) => Console.WriteLine("[ERR] " + text);
    }

    public static class Prefs
    {
        public static bool LogVerbose = true;
    }

    public static class GenText
    {
        public static string ToTitleCaseSmart(string text) => text; // Mock implementation
    }
    
    public class ModContentPack { }

    public class Mod
    {
        public Mod(ModContentPack content) { }
        public virtual string SettingsCategory() => "";
        public virtual void DoSettingsWindowContents(UnityEngine.Rect inRect) { }
        public virtual void WriteSettings() { }
        public T GetSettings<T>() where T : ModSettings, new() => new T();
    }

    public class ModSettings
    {
        public virtual void ExposeData() { }
    }

    public static class Scribe_Values
    {
        public static void Look<T>(ref T value, string label, T defaultValue = default(T), bool forceSave = false) { }
    }
    
    public class Listing_Standard
    {
        public void Begin(UnityEngine.Rect rect) { }
        public void Label(string text) { }
        public string TextEntry(string text) => text;
        public void Gap(float height = 12f) { }
        public void CheckboxLabeled(string label, ref bool checkOn, string tooltip = null) { }
        public void End() { }
    }

    public static class Translator
    {
        public static bool TryGetTranslatedStringsForFile(string file, out List<string> strings)
        {
            strings = new List<string>();
            return false;
        }
    }
    
    public class TranslatorFormattedStringExtensions
    {
        public static string Translate(string key) => key;
    }

    public class TaggedString
    {
        public static implicit operator string(TaggedString ts) => ts?.ToString();
        public static implicit operator TaggedString(string s) => new TaggedString();
    }
    
    public class Letter { }
    public class ChoiceLetter : Letter { }

    [AttributeUsage(AttributeTargets.Class)]
    public class StaticConstructorOnStartupAttribute : Attribute { }
}

namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object message) => Console.WriteLine(message);
        public static void LogWarning(object message) => Console.WriteLine("[WARN] " + message);
        public static void LogError(object message) => Console.WriteLine("[ERR] " + message);
    }
    
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        {
            this.x = x; this.y = y; this.width = width; this.height = height;
        }
    }

    public static class GUI
    {
        public static bool changed = false;
    }
    
    public class TextAnchor { }
    public class Font { }
}

namespace RimWorld
{
    public class ThingDef
    {
        public string defName;
        public string label;
        public string description;
    }
    public class Quest { 
        public string name;
        public string description;
    }
}

namespace RimWorld.Planet
{
}

namespace Verse
{
    public struct TipSignal
    {
        public static implicit operator TipSignal(string s) => new TipSignal();
    }
    public class Root { }
    public class Def { }
    public static class Widgets { }
    public class GameFont { }
    public static class Messages {
        public static void Message(string text, object def, bool historical = true) { }
    }
    public class MessageTypeDefOf {
        public static object RejectInput = null;
    }
}

namespace HarmonyLib
{
    public class Harmony
    {
        public Harmony(string id) { }
        public void PatchAll() { }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class HarmonyPatchAttribute : Attribute
    {
        public HarmonyPatchAttribute() { }
        public HarmonyPatchAttribute(Type declaringType, string methodName = null, Type[] argumentTypes = null) { }
        public HarmonyPatchAttribute(string methodName) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefixAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfixAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPriorityAttribute : Attribute
    {
        public HarmonyPriorityAttribute(int priority) { }
    }

    public static class Priority
    {
        public const int Last = 0;
        public const int Low = 400;
        public const int First = 800;
    }

    public static class AccessTools
    {
        public delegate F FieldRef<T, F>(T obj);
        public static FieldRef<T, F> FieldRefAccess<T, F>(string name) => (obj) => default(F);
    }
}

namespace UnityEngine
{
    public static class Mathf {
        public static int CeilToInt(float f) => (int)f;
    }
}
