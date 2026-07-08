using HarmonyLib;
using UnityModManagerNet;
using UnityEngine;

namespace AllowSpecialKeys;

public static class Main
{
    public static UnityModManager.ModEntry Mod { get; private set; }
    public static Harmony Harmony { get; private set; }
    public static Settings Settings { get; private set; }
    public static KeyboardHook Hook { get; private set; }

    private static string T(int key) => I18n.Get(Settings.Language, key);

    public static bool Load(UnityModManager.ModEntry modEntry)
    {
        Mod = modEntry;
        Settings = Settings.Load(modEntry);

        modEntry.OnToggle = OnToggle;
        modEntry.OnGUI = OnGUI;
        modEntry.OnSaveGUI = Settings.OnSaveGUI;
        modEntry.OnHideGUI = Settings.OnHideGUI;

        Harmony = new Harmony(modEntry.Info.Id);
        Mod.Logger.Log("AllowSpecialKeys loaded.");

        return true;
    }

    private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
    {
        if (value)
        {
            Mod.Logger.Log("AllowSpecialKeys enabled.");

            Hook = new KeyboardHook();
            Hook.UpdateBlockedKeys(Settings);
            Hook.Start();
            Mod.Logger.Log("Keyboard hook started.");

            GamePatches.Register();
            Main.Mod.Logger.Log("Harmony patches applied.");
        }
        else
        {
            Mod.Logger.Log("AllowSpecialKeys disabled.");

            Hook?.Stop();
            Hook = null;

            GamePatches.Unregister();
        }
        return true;
    }

    private static void OnGUI(UnityModManager.ModEntry modEntry)
    {
        GUILayout.BeginVertical();

        // ========== Language switcher ==========
        GUILayout.BeginHorizontal();
        SetLangBtn(0, T(I18n.T_LANG_ZH));
        SetLangBtn(1, T(I18n.T_LANG_EN));
        SetLangBtn(2, T(I18n.T_LANG_KO));
        GUILayout.EndHorizontal();

        GUILayout.Space(5);
        GUILayout.Label(T(I18n.T_TITLE), GUILayout.ExpandWidth(true));

        GUILayout.Space(5);
        GUILayout.Label(T(I18n.T_OS_HEADER), GUILayout.ExpandWidth(true));

        Settings.BlockWinKey = GUILayout.Toggle(Settings.BlockWinKey, T(I18n.T_BLOCK_WIN));

        GUILayout.BeginHorizontal();
        Settings.BlockAltTab = GUILayout.Toggle(Settings.BlockAltTab, T(I18n.T_BLOCK_ALT_TAB));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Settings.BlockAltEnter = GUILayout.Toggle(Settings.BlockAltEnter, T(I18n.T_BLOCK_ALT_ENTER));
        GUILayout.EndHorizontal();

        Settings.BlockCtrlEsc = GUILayout.Toggle(Settings.BlockCtrlEsc, T(I18n.T_BLOCK_CTRL_ESC));

        GUILayout.Space(5);
        GUILayout.Label(T(I18n.T_GAME_HEADER), GUILayout.ExpandWidth(true));

        Settings.AllowSpecialAsGameplay = GUILayout.Toggle(
            Settings.AllowSpecialAsGameplay, T(I18n.T_SPECIAL_KEYS));
        if  (Settings.AllowSpecialAsGameplay)
            Settings.AllowF12AsGameplay = GUILayout.Toggle(
                Settings.AllowF12AsGameplay, T(I18n.T_F12_KEY));

        GUILayout.Space(10);

        if (GUILayout.Button(T(I18n.T_APPLY), GUILayout.Width(100)))
        {
            Mod.Logger.Log("Settings applied.");
            Hook?.UpdateBlockedKeys(Settings);
            Settings.Save(modEntry);
        }

        GUILayout.EndVertical();
    }

    private static void SwitchLang(int lang)
    {
        if (Settings.Language == lang) return;
        Settings.Language = lang;
        Mod.Logger.Log($"Language switched to {new[]{"中文","English","한국어"}[lang]}");
    }

    private static void SetLangBtn(int lang, string label)
    {
        bool isActive = Settings.Language == lang;
        if (isActive) GUI.color = Color.yellow;
        if (GUILayout.Button(label, GUILayout.ExpandWidth(true)))
            SwitchLang(lang);
        if (isActive) GUI.color = Color.white;
    }
}
