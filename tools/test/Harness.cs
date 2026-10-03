using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

[BepInPlugin("ru.ironlung.harness", "IL Harness", "1.0.0")]
public class Harness : BaseUnityPlugin
{
    static string outDir;
    static StringBuilder log = new StringBuilder();
    string only;

    void Awake()
    {
        Application.runInBackground = true;
        outDir = Path.Combine(Paths.GameRootPath, "harness_out");
        Directory.CreateDirectory(outDir);
        only = Environment.GetEnvironmentVariable("IL_HARNESS_ONLY") ?? "";
        Harmony h = new Harmony("ru.ironlung.harness");
        h.Patch(AccessTools.Method(typeof(SteamScript), "UnlockCheevo"), new HarmonyMethod(typeof(Harness).GetMethod("NoCheevo")));
        StartCoroutine(Run());
    }

    public static bool NoCheevo(string cheevo)
    {
        W("BLOCKED CHEEVO " + cheevo);
        return false;
    }

    static void W(string s)
    {
        log.AppendLine(s);
        File.WriteAllText(Path.Combine(outDir, "log.txt"), log.ToString(), new UTF8Encoding(false));
    }

    static string Esc(string s)
    {
        if (s == null) return "<null>";
        return s.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
    }

    static string PathOf(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));
        yield return null;
        yield return null;
        yield return new WaitForSeconds(0.3f);
    }

    IEnumerator Wait(float s) { yield return new WaitForSeconds(s); }

    void Dump(string tag)
    {
        foreach (TMP_Text t in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (!t.gameObject.scene.IsValid()) continue;
            string fallbacks = "";
            W("[" + tag + "] " + PathOf(t.transform) + " :: " + Esc(t.GetParsedText()) + " | font=" + (t.font != null ? t.font.name : "null") + fallbacks + " size=" + t.fontSize);
        }
    }

    static string CurrentSubtitle()
    {
        foreach (MonoBehaviour mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (mb.GetType().Name != "IronLungRu") continue;
            System.Reflection.FieldInfo f = mb.GetType().GetField("currentText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return f == null ? "<no field>" : (string)f.GetValue(mb) ?? "<none>";
        }
        return "<plugin not found>";
    }

    void Try(string what, Action a)
    {
        try { a(); }
        catch (Exception e) { W("EXC in " + what + ": " + e); }
    }

    static string SettingsLine(string key)
    {
        foreach (string l in File.ReadAllLines(Path.Combine(Paths.GameRootPath, "settings.ini")))
            if (l.StartsWith(key + "=")) return l;
        return key + "=?";
    }

    IEnumerator Run()
    {
        while (SceneManager.GetActiveScene().name != "Menu") yield return null;
        yield return Wait(4f);
        W("SCENE " + SceneManager.GetActiveScene().name);
        Dump("menu");
        yield return Shot("01_menu");

        MenuControllerScript menu = UnityEngine.Object.FindObjectOfType<MenuControllerScript>();
        Try("PressSettings", delegate { menu.PressSettings(); });
        yield return Wait(1f);
        Dump("settings");
        yield return Shot("02_settings");
        W("SETTINGS before: vsync=" + menu.VsyncButton.GetParsedText() + " fullscreen=" + menu.FullscreenButton.GetParsedText() + " invert=" + menu.InvertButton.GetParsedText());
        Try("InvertChange1", delegate { menu.InvertChange(); });
        yield return null;
        yield return null; W("after InvertChange #1: invert=" + menu.InvertButton.GetParsedText() + " (game sees: " + menu.InvertButton.text + ")");
        Try("InvertChange2", delegate { menu.InvertChange(); });
        yield return null;
        yield return null; W("after InvertChange #2: invert=" + menu.InvertButton.GetParsedText() + " (game sees: " + menu.InvertButton.text + ")");
        W("ini before Apply: " + SettingsLine("mouse_invert") + " " + SettingsLine("fullscreen") + " " + SettingsLine("vsync"));
        Try("ApplySettings", delegate { menu.ApplySettings(); });
        yield return Wait(1.5f);
        W("ini after Apply:  " + SettingsLine("mouse_invert") + " " + SettingsLine("fullscreen") + " " + SettingsLine("vsync"));

        SceneManager.LoadScene("IntroScene");
        yield return Wait(2f);
        IntroManager im = UnityEngine.Object.FindObjectOfType<IntroManager>();
        if (im != null) { im.TimeUntilSceneChange = 0.01f; }
        yield return Wait(1.5f);
        W("SCENE " + SceneManager.GetActiveScene().name);
        Dump("intro");
        yield return Shot("03_intro");

        SceneManager.LoadScene("MainScene");
        yield return Wait(7f);
        W("SCENE " + SceneManager.GetActiveScene().name);
        Dump("main");
        W("SUBTITLE @7s: " + CurrentSubtitle());
        yield return Shot("04_main");
        yield return Wait(7.5f);
        W("SUBTITLE @15s: " + CurrentSubtitle());
        yield return Shot("04b_radio");
        yield return Wait(12f);
        W("SUBTITLE @27s: " + CurrentSubtitle());
        yield return Shot("04c_radio");

        CanvasGroup brief = GameObject.Find("BriefingCanvas").GetComponent<CanvasGroup>();
        brief.alpha = 1f;
        yield return Wait(0.5f);
        yield return Shot("05_briefing");
        brief.alpha = 0f;

        CanvasGroup map = GameObject.Find("MapCanvas").GetComponent<CanvasGroup>();
        map.alpha = 1f;
        foreach (MapMarkerScript m in UnityEngine.Object.FindObjectsOfType<MapMarkerScript>())
        {
            if (!m.IsPlayer) { MapMarkerScript mm = m; Try("ShowText", delegate { mm.ShowText(); }); break; }
        }
        yield return Wait(0.5f);
        Dump("map");
        yield return Shot("06_map");
        map.alpha = 0f;

        NoteScript note = UnityEngine.Object.FindObjectOfType<NoteScript>();
        if (note != null)
        {
            note.ToggleNote(true);
            yield return Wait(0.7f);
            yield return Shot("07_note");
            note.ToggleNote(false);
        }

        TerminalScript ts = UnityEngine.Object.FindObjectOfType<TerminalScript>();
        ts.ClearText();
        ts.ClearConsole();
        ts.CanType = true;
        yield return Wait(0.5f);
        W("TERM header: " + Esc(ts.Text.GetParsedText()));
        yield return Shot("08_term_header");

        for (int i = 0; i < ts.Entries.Length; i++)
        {
            ts.ClearText();
            ts.Console.text = "query>" + ts.Entries[i].Queries[0] + "_";
            ts.CheckQuery();
            ts.ClearConsole();
            yield return null;
            yield return null;
            W("TERM entry " + i + " [" + ts.Entries[i].Queries[0] + "]: " + Esc(ts.Text.GetParsedText()));
            if (only == "" || only.Contains("shots"))
                yield return Shot("09_term_entry_" + i.ToString("00"));
        }

        // accumulation test: several queries in a row, unknown query, cheats
        ts.ClearText();
        string[] seq = new string[] { "coi", "xyzzy", "sm-8", "chview", "chnoir", "chrave", "chava", "chvest", "chmark", "chclear", "кровавые океаны", "железный союз", "см8", "эдем", "станцию филамент", "сука", "тварь" };
        foreach (string q in seq)
        {
            ts.Console.text = "query>" + q + "_";
            ts.CheckQuery();
            ts.ClearConsole();
            yield return null;
            yield return null;
            W("TERM accum after [" + q + "]: " + Esc(ts.Text.GetParsedText()));
        }
        yield return Shot("10_term_accum");
        ts.Console.text = "query>съешь же ещё этих мягких французских булок да выпей чаю 0123456789_";
        yield return Wait(0.5f);
        yield return Shot("10b_term_pangram");
        ts.CanType = false;

        if (Environment.GetEnvironmentVariable("IL_TYPE_TEST") == "1")
        {
            ts.ClearText();
            ts.ClearConsole();
            ts.CanType = true;
            yield return Wait(0.5f);
            File.WriteAllText(Path.Combine(outDir, "ready_for_typing"), "1");
            W("TYPE TEST: waiting for keyboard input");
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 40f && !ts.Text.GetParsedText().Contains("[запись]")) yield return null;
            yield return Wait(1f);
            W("TYPE TEST console: " + Esc(ts.Console.GetParsedText()));
            W("TYPE TEST result: " + Esc(ts.Text.GetParsedText()));
            yield return Shot("12_typed_query");
            ts.CanType = false;
        }

        FinalCanvasScript fc = UnityEngine.Object.FindObjectOfType<FinalCanvasScript>();
        if (fc != null)
        {
            fc.enabled = false;
            fc.EndCanvas.alpha = 1f;
            for (int g = 0; g < fc.Groups.Length; g++)
            {
                for (int k = 0; k < fc.Groups.Length; k++) fc.Groups[k].alpha = (k == g) ? 1f : 0f;
                yield return Wait(0.5f);
                yield return Shot("11_final_" + g);
            }
            Dump("final");
        }

        W("DONE");
        Application.Quit();
    }
}
