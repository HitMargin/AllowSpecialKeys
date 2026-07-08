using HarmonyLib;
using SkyHook;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AllowSpecialKeys;

internal static class GamePatches
{
    private static bool _patchesApplied;
    private static FieldInfo _asyncLabelField;

    public static void Register()
    {
        if (_patchesApplied) return;

        var harmony = Main.Harmony;

        // ====================== Patch 1: GetSpecialInput ======================
        // When AllowSpecialAsGameplay is ON → return empty → all keys pass
        var getSpecialInput = AccessTools.Method(typeof(RDInputType_AsyncKeyboard), "GetSpecialInput");
        if (getSpecialInput != null)
            harmony.Patch(getSpecialInput,
                prefix: new HarmonyMethod(typeof(GamePatches), nameof(GetSpecialInputPrefix)));

        // ====================== Patch 2: Main() F12 control ======================
        // When AllowF12AsGameplay is OFF → remove F12 from press list
        // When ON → ensure F12 is in press list
        var mainMethod = AccessTools.Method(typeof(RDInputType_AsyncKeyboard), "Main", new[] { typeof(ButtonState) });
        if (mainMethod != null)
            harmony.Patch(mainMethod,
                postfix: new HarmonyMethod(typeof(GamePatches), nameof(MainPostfix)));

        // ====================== Patch 3: Legacy CountSpecialInput ======================
        var countSpecialInput = AccessTools.Method(typeof(RDInputType_Keyboard), "CountSpecialInput");
        if (countSpecialInput != null)
            harmony.Patch(countSpecialInput,
                prefix: new HarmonyMethod(typeof(GamePatches), nameof(CountSpecialInputPrefix)));

        // ====================== Patch 4: KeyViewer compat ======================
        TryPatchKeyViewer(harmony);

        _asyncLabelField = AccessTools.Field(typeof(AsyncKeyCode), "label");
        _patchesApplied = true;
    }

    private static void TryPatchKeyViewer(Harmony harmony)
    {
        var kvType = AccessTools.TypeByName("JipperKeyViewer.KeyViewer.KeyViewer");
        if (kvType == null) return;
        var m = AccessTools.Method(kvType, "ProcessKeySelection");
        if (m == null) return;
        harmony.Patch(m, prefix: new HarmonyMethod(typeof(GamePatches), nameof(KvProcessKeySelPrefix)));
        Main.Mod.Logger.Log("Patched: KeyViewer.ProcessKeySelection (Alt/Win fix)");
    }

    public static void Unregister()
    {
        if (!_patchesApplied) return;
        Main.Harmony?.UnpatchAll(Main.Mod.Info.Id);
        _patchesApplied = false;
    }

    // ======================= Harmony methods =======================

    private static bool GetSpecialInputPrefix(ref List<AsyncKeyCode> __result)
    {
        if (Main.Settings.AllowSpecialAsGameplay)
        {
            __result = new List<AsyncKeyCode>();
            return false;
        }
        return true;
    }

    /// <summary>
    /// Postfix on Main(ButtonState). Controls F12 based on AllowF12AsGameplay toggle.
    /// Uses GetAsyncKeyState to detect F12 independent of Windows message queue.
    /// </summary>
    private static void MainPostfix(RDInputType_AsyncKeyboard __instance, ButtonState state, ref int __result)
    {
        var keys = __instance.pressCount.keys;  // List<AnyKeyCode>
        bool toggle = Main.Settings.AllowF12AsGameplay;

        short phys = GetAsyncKeyState(VK_F12);
        bool f12Pressed = (phys & 0x0001) != 0;  // transition up→down since last call

        if (toggle && f12Pressed)
        {
            // F12 physically pressed → ensure it's in the key list
            if (!keys.Exists(k => IsF12(k)))
            {
                // Create AsyncKeyCode with KeyLabel.F12 (=12)
                var f12Code = new AsyncKeyCode((KeyLabel)12);
                keys.Add(new AnyKeyCode(f12Code));
                __result++;
            }
        }
        else if (!toggle)
        {
            // F12 NOT allowed → remove from key list
            int removed = keys.RemoveAll(k => IsF12(k));
            __result -= removed;
        }
    }

    private static bool IsF12(AnyKeyCode akc)
    {
        try
        {
            var code = (AsyncKeyCode)akc.value;
            var label = (KeyLabel)_asyncLabelField.GetValue(code);
            return (int)label == 12;
        }
        catch { return false; }
    }

    private static bool CountSpecialInputPrefix(ref List<KeyCode> __result)
    {
        if (Main.Settings.AllowSpecialAsGameplay)
        {
            __result = new List<KeyCode>();
            return false;
        }
        return true;
    }

    // ======================= KeyViewer compat =======================

    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_F12 = 0x7B;

    private static bool KvProcessKeySelPrefix(object __instance, int ___SelectedKey, int ___changeState)
    {
        if (___SelectedKey == -1 || ___changeState == 1 || !Application.isFocused)
            return true;

        if (Input.GetKeyDown(KeyCode.LeftAlt)) { CallSetupKey(__instance, KeyCode.LeftAlt); return false; }
        if (Input.GetKeyDown(KeyCode.RightAlt)) { CallSetupKey(__instance, KeyCode.RightAlt); return false; }

        var s = GetAsyncKeyState(VK_LWIN);
        if ((s & 0x8001) != 0) { CallSetupKey(__instance, KeyCode.LeftWindows); return false; }
        s = GetAsyncKeyState(VK_RWIN);
        if ((s & 0x8001) != 0) { CallSetupKey(__instance, KeyCode.RightWindows); return false; }

        return true;
    }

    private static void CallSetupKey(object instance, KeyCode key)
    {
        var m = AccessTools.Method(instance.GetType(), "SetupKey", new[] { typeof(KeyCode) });
        try { m?.Invoke(instance, new object[] { key }); } catch { }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
