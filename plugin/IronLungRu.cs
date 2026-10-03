// Iron Lung — компаньон русификатора.
// 1) Кириллица в игре рисуется резервным динамическим шрифтом TextMeshPro, у которого один маленький атлас:
//    когда он заполняется, новые буквы превращаются в квадратики. Плагин включает многостраничный атлас
//    и заранее загружает в него весь русский алфавит.
// 2) Добавляет русские варианты запросов в терминал подлодки (список в queries.txt рядом с плагином).
// 3) Отключает перенос строк у кнопок главного меню, чтобы «Новая игра» не разваливалась на две строки.
// 4) Показывает субтитры к голосу по радио (тексты и тайминги — в subtitles.txt рядом с плагином).
// 5) Подменяет текстуры: textures/<имя>.png загружается в текстуру игры с тем же именем,
//    sprites/<имя>.png заменяет спрайт интерфейса (так можно поставить картинку большего разрешения).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[BepInPlugin("ru.ironlung.rusfix", "Iron Lung RU Fix", "1.1.0")]
public class IronLungRu : BaseUnityPlugin
{
    const string Glyphs =
        "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя«»—–…№„“”’";

    static ManualLogSource Log;
    static readonly Dictionary<string, List<string>> Aliases = new Dictionary<string, List<string>>();
    static readonly HashSet<int> FixedFonts = new HashSet<int>();
    static readonly string[] MenuButtons = { "Start", "Load", "Settings", "Quit" };

    class Cue
    {
        public float Start, End;
        public string Text;
    }

    static readonly Dictionary<string, List<Cue>> Subtitles = new Dictionary<string, List<Cue>>();
    readonly List<AudioSource> voiced = new List<AudioSource>();
    ConfigEntry<bool> subtitlesOn;
    ConfigEntry<float> subtitleScale;
    string currentText;
    GUIStyle subtitleStyle;
    static string pluginDir;
    static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

    void Awake()
    {
        Log = Logger;
        string dir = Path.GetDirectoryName(Info.Location);
        pluginDir = dir;
        LoadAliases(Path.Combine(dir, "queries.txt"));
        LoadSubtitles(Path.Combine(dir, "subtitles.txt"));
        subtitlesOn = Config.Bind("Subtitles", "Enabled", true, "Показывать субтитры к голосу по радио");
        subtitleScale = Config.Bind("Subtitles", "Scale", 1.0f, "Размер субтитров (1.0 — обычный)");
        Harmony harmony = new Harmony("ru.ironlung.rusfix");
        harmony.Patch(AccessTools.Method(typeof(TerminalScript), "Start"),
            null, new HarmonyMethod(typeof(IronLungRu).GetMethod("TerminalStartPostfix")));
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        FixFonts();
        if (scene.name == "Menu") FixMenuButtons();
        ReplaceTextures();
        voiced.Clear();
        foreach (AudioSource src in Resources.FindObjectsOfTypeAll<AudioSource>())
            if (src.gameObject.scene.IsValid() && src.clip != null && Subtitles.ContainsKey(src.clip.name))
                voiced.Add(src);
    }

    void Update()
    {
        currentText = null;
        if (!subtitlesOn.Value) return;
        foreach (AudioSource src in voiced)
        {
            if (src == null || src.clip == null || !src.isPlaying) continue;
            List<Cue> cues;
            if (!Subtitles.TryGetValue(src.clip.name, out cues)) continue;
            float t = src.time;
            foreach (Cue c in cues)
                if (t >= c.Start && t < c.End) { currentText = c.Text; return; }
        }
    }

    void OnGUI()
    {
        if (string.IsNullOrEmpty(currentText)) return;
        if (subtitleStyle == null)
        {
            subtitleStyle = new GUIStyle(GUI.skin.label);
            subtitleStyle.alignment = TextAnchor.LowerCenter;
            subtitleStyle.wordWrap = true;
            subtitleStyle.fontStyle = FontStyle.Italic;
        }
        subtitleStyle.fontSize = Mathf.Max(14, (int)(Screen.height / 30f * subtitleScale.Value));
        float w = Screen.width * 0.7f;
        Rect r = new Rect((Screen.width - w) / 2f, Screen.height * 0.62f, w, Screen.height * 0.25f);
        int o = Mathf.Max(1, subtitleStyle.fontSize / 14);
        subtitleStyle.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
        for (int dx = -o; dx <= o; dx += o)
            for (int dy = -o; dy <= o; dy += o)
                if (dx != 0 || dy != 0) GUI.Label(new Rect(r.x + dx, r.y + dy, r.width, r.height), currentText, subtitleStyle);
        subtitleStyle.normal.textColor = new Color(0.86f, 0.95f, 0.86f, 1f);
        GUI.Label(r, currentText, subtitleStyle);
    }

    static void ReplaceTextures()
    {
        string texDir = Path.Combine(pluginDir, "textures");
        if (Directory.Exists(texDir))
        {
            Dictionary<string, string> files = new Dictionary<string, string>();
            foreach (string f in Directory.GetFiles(texDir, "*.png")) files[Path.GetFileNameWithoutExtension(f)] = f;
            foreach (Texture2D tex in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                string f;
                if (tex == null || !files.TryGetValue(tex.name, out f)) continue;
                try
                {
                    FilterMode filter = tex.filterMode;
                    TextureWrapMode wrap = tex.wrapMode;
                    tex.LoadImage(File.ReadAllBytes(f));
                    tex.filterMode = filter;
                    tex.wrapMode = wrap;
                    Log.LogInfo("Texture replaced: " + tex.name);
                }
                catch (Exception e) { Log.LogWarning("Texture " + tex.name + ": " + e.Message); }
            }
        }
        string spriteDir = Path.Combine(pluginDir, "sprites");
        if (!Directory.Exists(spriteDir)) return;
        foreach (Image img in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (img == null || img.sprite == null || img.sprite.texture == null) continue;
            string name = img.sprite.texture.name;
            string f = Path.Combine(spriteDir, name + ".png");
            if (!File.Exists(f)) continue;
            Sprite sprite;
            if (!SpriteCache.TryGetValue(name, out sprite) || sprite == null)
            {
                Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                t.LoadImage(File.ReadAllBytes(f));
                t.name = name + "_ru";
                t.filterMode = FilterMode.Trilinear;
                t.wrapMode = TextureWrapMode.Clamp;
                Sprite old = img.sprite;
                sprite = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f),
                    old.pixelsPerUnit * t.width / old.rect.width);
                sprite.name = old.name;
                SpriteCache[name] = sprite;
            }
            img.sprite = sprite;
            Log.LogInfo("Sprite replaced: " + name + " (" + sprite.texture.width + "x" + sprite.texture.height + ")");
        }
    }

    static void LoadSubtitles(string path)
    {
        if (!File.Exists(path)) return;
        List<Cue> cues = null;
        foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                cues = new List<Cue>();
                Subtitles[line.Substring(1, line.Length - 2)] = cues;
                continue;
            }
            string[] parts = line.Split(new char[] { ' ' }, 3);
            float start, end;
            if (cues == null || parts.Length < 3 ||
                !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out start) ||
                !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out end))
            {
                Log.LogWarning("subtitles.txt: bad line: " + line);
                continue;
            }
            Cue c = new Cue();
            c.Start = start; c.End = end; c.Text = parts[2];
            cues.Add(c);
        }
        int n = 0;
        foreach (List<Cue> l in Subtitles.Values) n += l.Count;
        Log.LogInfo("Subtitles: " + n + " lines for " + Subtitles.Count + " clip(s)");
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
