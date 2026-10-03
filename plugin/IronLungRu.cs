// Iron Lung — компаньон русификатора.
// 1) Кириллица в игре рисуется резервным динамическим шрифтом TextMeshPro, у которого один маленький атлас:
//    когда он заполняется, новые буквы превращаются в квадратики. Плагин включает многостраничный атлас
//    и заранее загружает в него весь русский алфавит.
// 2) Добавляет русские варианты запросов в терминал подлодки (список в queries.txt рядом с плагином).
// 3) Отключает перенос строк у кнопок главного меню, чтобы «Новая игра» не разваливалась на две строки.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

[BepInPlugin("ru.ironlung.rusfix", "Iron Lung RU Fix", "1.0.0")]
public class IronLungRu : BaseUnityPlugin
{
    const string Glyphs =
        "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя«»—–…№„“”’";

    static ManualLogSource Log;
    static readonly Dictionary<string, List<string>> Aliases = new Dictionary<string, List<string>>();
    static readonly HashSet<int> FixedFonts = new HashSet<int>();
    static readonly string[] MenuButtons = { "Start", "Load", "Settings", "Quit" };

    void Awake()
    {
        Log = Logger;
        LoadAliases(Path.Combine(Path.GetDirectoryName(Info.Location), "queries.txt"));
        Harmony harmony = new Harmony("ru.ironlung.rusfix");
        harmony.Patch(AccessTools.Method(typeof(TerminalScript), "Start"),
            null, new HarmonyMethod(typeof(IronLungRu).GetMethod("TerminalStartPostfix")));
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        FixFonts();
        if (scene.name == "Menu") FixMenuButtons();
    }

    static void FixMenuButtons()
    {
        foreach (TMP_Text t in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
        {
            Transform button = t.transform.parent;
            if (button != null && button.parent != null && button.parent.name == "Canvas"
                && Array.IndexOf(MenuButtons, button.name) >= 0)
                t.enableWordWrapping = false;
        }
    }

    static void FixFonts()
    {
        List<TMP_FontAsset> fonts = new List<TMP_FontAsset>(Resources.FindObjectsOfTypeAll<TMP_FontAsset>());
        TMP_FontAsset fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
        if (fallback != null && !fonts.Contains(fallback)) fonts.Add(fallback);
        foreach (TMP_FontAsset font in fonts)
        {
            if (font == null || font.atlasPopulationMode != AtlasPopulationMode.Dynamic) continue;
            if (!FixedFonts.Add(font.GetInstanceID())) continue;
            try
            {
                font.isMultiAtlasTexturesEnabled = true;
                string missing;
                font.TryAddCharacters(Glyphs, out missing);
                Log.LogInfo("Font '" + font.name + "': multi-atlas on, cyrillic preloaded" +
                    (string.IsNullOrEmpty(missing) ? "" : " (not in font: " + missing.Length + " chars)"));
            }
            catch (Exception e)
            {
                Log.LogWarning("Font '" + font.name + "': " + e.Message);
            }
        }
    }

    static void LoadAliases(string path)
    {
        if (!File.Exists(path))
        {
            Log.LogWarning("queries.txt not found: " + path);
            return;
        }
        foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq).Trim().ToLowerInvariant();
            List<string> list;
            if (!Aliases.TryGetValue(key, out list)) { list = new List<string>(); Aliases[key] = list; }
            foreach (string part in line.Substring(eq + 1).Split(';'))
            {
                string a = part.Trim().ToLower();
                if (a.Length == 0) continue;
                if (!list.Contains(a)) list.Add(a);
                string e = a.Replace('ё', 'е');
                if (!list.Contains(e)) list.Add(e);
            }
        }
    }

    public static void TerminalStartPostfix(TerminalScript __instance)
    {
        FixFonts();
        int added = 0;
        foreach (TerminalScript.entry entry in __instance.Entries)
        {
            List<string> queries = new List<string>(entry.Queries);
            foreach (string q in entry.Queries)
            {
                List<string> list;
                if (!Aliases.TryGetValue(q.ToLowerInvariant(), out list)) continue;
                foreach (string a in list)
                    if (!queries.Contains(a)) { queries.Add(a); added++; }
            }
            entry.Queries = queries.ToArray();
        }
        Log.LogInfo("Terminal: added " + added + " russian queries");
    }
}
