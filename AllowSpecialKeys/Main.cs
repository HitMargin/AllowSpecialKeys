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

    private static GameObject _holder;
    private static CoroutineRunner _runner;

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

        // 创建持久对象
        _holder = new GameObject("AllowSpecialKeys_Runner");
        _runner = _holder.AddComponent<CoroutineRunner>();
        Object.DontDestroyOnLoad(_holder);

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
            Mod.Logger.Log("Harmony patches applied.");

            // 挂载焦点处理器
            if (_runner.GetComponent<FocusHandler>() == null)
            {
                var fh = _runner.gameObject.AddComponent<FocusHandler>();
                Mod.Logger.Log("FocusHandler component added.");
            }
            else
            {
                Mod.Logger.Log("FocusHandler already exists.");
            }
        }
        else
        {
            Mod.Logger.Log("AllowSpecialKeys disabled.");
            Hook?.Stop();
            Hook = null;
            GamePatches.Unregister();

            var fh = _runner.GetComponent<FocusHandler>();
            if (fh != null) Object.Destroy(fh);
        }
        return true;
    }

    public static void RestartMod()
    {
        Mod.Logger.Log("RestartMod called.");
        Hook?.Stop();
        Hook = null;
        GamePatches.Unregister();

        Hook = new KeyboardHook();
        Hook.UpdateBlockedKeys(Settings);
        Hook.Start();
        GamePatches.Register();
        Mod.Logger.Log("RestartMod completed.");
    }

    private static void OnGUI(UnityModManager.ModEntry modEntry)
    {
        GUILayout.BeginVertical();

        // Language
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
        Settings.BlockAltTab = GUILayout.Toggle(Settings.BlockAltTab, T(I18n.T_BLOCK_ALT_TAB));
        Settings.BlockAltEnter = GUILayout.Toggle(Settings.BlockAltEnter, T(I18n.T_BLOCK_ALT_ENTER));
        Settings.BlockCtrlEsc = GUILayout.Toggle(Settings.BlockCtrlEsc, T(I18n.T_BLOCK_CTRL_ESC));

        GUILayout.Space(5);
        GUILayout.Label(T(I18n.T_GAME_HEADER), GUILayout.ExpandWidth(true));

        Settings.AllowSpecialAsGameplay = GUILayout.Toggle(
            Settings.AllowSpecialAsGameplay, T(I18n.T_SPECIAL_KEYS));
        if (Settings.AllowSpecialAsGameplay)
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
        Mod.Logger.Log($"Language switched to {new[] { "中文", "English", "한국어" }[lang]}");
    }

    private static void SetLangBtn(int lang, string label)
    {
        bool isActive = Settings.Language == lang;
        if (isActive) GUI.color = Color.yellow;
        if (GUILayout.Button(label, GUILayout.ExpandWidth(true)))
            SwitchLang(lang);
        if (isActive) GUI.color = Color.white;
    }

    // ===== 协程载体 =====
    private class CoroutineRunner : MonoBehaviour { }

    // ===== 焦点处理器（带调试日志） =====
    private class FocusHandler : MonoBehaviour
    {
        private bool _wasFocused = true;
        private float _lastRestartTime = -10f;

        private void Update()
        {
            bool focused = Application.isFocused;
            if (focused != _wasFocused && focused)
            {
                _wasFocused = true;
                if (Time.unscaledTime - _lastRestartTime > 3f)
                {
                    _lastRestartTime = Time.unscaledTime;
                    Main.RestartMod();
                }
            }
            else
            {
                _wasFocused = focused;
            }
        }
    }
}