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
    private static bool _specialKeysLogged;

    // KeyLabel 常量（F12=12 已确认，其余待日志确认后填入）
    private const int _LABEL_F12 = 12;
    private const int _LABEL_TAB = -1;
    private const int _LABEL_RETURN = -1;
    private const int _LABEL_F4 = -1;
    private const int _LABEL_LWIN = -1;
    private const int _LABEL_RWIN = -1;

    // ---- 物理键跟踪（只跟踪需要修复的键，不包括 Escape） ----
    private static int _lastSampleFrame = -1;
    private static readonly Dictionary<int, bool> _rawDown = new();
    private static readonly Dictionary<int, bool> _prevDown = new();

    // 只跟踪可能被钩子条件拦截且需要恢复游戏输入的键
    private static readonly int[] TrackedVks =
    {
        VK_LWIN, VK_RWIN,
        VK_RETURN,
        VK_TAB,
        VK_F4,
        // Escape 不跟踪，因为不对其做任何修正
    };

    public static void Register()
    {
        if (_patchesApplied) return;

        var harmony = Main.Harmony;

        PatchGetSpecialInput(harmony);
        PatchAsyncMain(harmony);
        PatchCountSpecialInput(harmony);
        PatchKeyboardMainIgnoreActive(harmony);

        // 修补 CheckKeyState，但只处理特定键，Escape 走原始逻辑
        PatchKeyboardCheckKeyState(harmony);
        // 异步的 CheckKeyState 我们不需要修补，因为异步模式不依赖消息，
        // 且我们没有修复异步模式下的任何键，所以跳过，避免干扰。
        // 如果需要修复异步模式下的某些键，可以添加，但 Escape 不处理。

        _asyncLabelField = AccessTools.Field(typeof(AsyncKeyCode), "label");
        _patchesApplied = true;
    }

    // ---------- 各补丁注册 ----------
    private static void PatchGetSpecialInput(Harmony harmony)
    {
        var getSpecialInput = AccessTools.Method(typeof(RDInputType_AsyncKeyboard), "GetSpecialInput");
        if (getSpecialInput != null)
            harmony.Patch(getSpecialInput,
                postfix: new HarmonyMethod(typeof(GamePatches), nameof(GetSpecialInputPostfix)));
    }

    private static void PatchAsyncMain(Harmony harmony)
    {
        var m = AccessTools.Method(typeof(RDInputType_AsyncKeyboard), "Main", new[] { typeof(ButtonState) });
        if (m != null)
            harmony.Patch(m, postfix: new HarmonyMethod(typeof(GamePatches), nameof(MainPostfix)));
    }

    private static void PatchCountSpecialInput(Harmony harmony)
    {
        var countSpecialInput = AccessTools.Method(typeof(RDInputType_Keyboard), "CountSpecialInput");
        if (countSpecialInput != null)
            harmony.Patch(countSpecialInput,
                postfix: new HarmonyMethod(typeof(GamePatches), nameof(CountSpecialInputPostfix)));
    }

    private static void PatchKeyboardMainIgnoreActive(Harmony harmony)
    {
        var m = AccessTools.Method(typeof(RDInputType_Keyboard), "MainIgnoreActive", new[] { typeof(ButtonState) });
        if (m != null)
        {
            harmony.Patch(m, postfix: new HarmonyMethod(typeof(GamePatches), nameof(KeyboardMainPostfix)));
            Main.Mod.Logger.Log("OK: RDInputType_Keyboard.MainIgnoreActive patched");
        }
        else
            Main.Mod.Logger.Log("FAIL: RDInputType_Keyboard.MainIgnoreActive NOT FOUND");
    }

    // ---- 修补 RDInputType_Keyboard.CheckKeyState（只针对特定键，Escape 跳过） ----
    private static void PatchKeyboardCheckKeyState(Harmony harmony)
    {
        var method = AccessTools.Method(typeof(RDInputType_Keyboard), "CheckKeyState",
            new[] { typeof(KeyCode), typeof(ButtonState) });
        if (method == null)
        {
            Main.Mod.Logger.Log("ERROR: RDInputType_Keyboard.CheckKeyState not found!");
            return;
        }

        harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(GamePatches), nameof(KeyboardCheckKeyStatePrefix)));
        Main.Mod.Logger.Log("OK: Patched RDInputType_Keyboard.CheckKeyState (excludes Escape)");
    }

    public static void Unregister()
    {
        if (!_patchesApplied) return;
        Main.Harmony?.UnpatchAll(Main.Mod.Info.Id);
        _patchesApplied = false;
    }

    // ======================= 物理状态采样 =======================
    private static void SampleSpecialKeysOncePerFrame()
    {
        if (Time.frameCount == _lastSampleFrame) return;
        _lastSampleFrame = Time.frameCount;

        foreach (var vk in TrackedVks)
        {
            bool down = KeyboardHook.IsKeyPhysicallyDown(vk);
            _rawDown.TryGetValue(vk, out bool previousDown);
            _prevDown[vk] = previousDown;
            _rawDown[vk] = down;
        }
    }

    // ======================= 补丁方法 =======================

    private static void GetSpecialInputPostfix(ref List<AsyncKeyCode> __result)
    {
        if (_asyncLabelField == null) return;

        if (!_specialKeysLogged)
        {
            _specialKeysLogged = true;
            foreach (var akc in __result)
            {
                try
                {
                    var label = (KeyLabel)_asyncLabelField.GetValue(akc);
                    Main.Mod.Logger.Log($"[GetSpecialInput] label={(int)label} raw={label}");
                }
                catch { }
            }
        }

        if (!Main.Settings.AllowSpecialAsGameplay) return;

        var filtered = new List<AsyncKeyCode>();

        foreach (var akc in __result)
        {
            try
            {
                var label = (KeyLabel)_asyncLabelField.GetValue(akc);
                int labelInt = (int)label;

                bool shouldRemove = ShouldRemoveFromSpecial(labelInt);

                if (!shouldRemove)
                    filtered.Add(akc);
            }
            catch
            {
                filtered.Add(akc);
            }
        }

        __result = filtered;
    }

    private static bool ShouldRemoveFromSpecial(int labelInt)
    {
        if (labelInt == _LABEL_F12 && Main.Settings.AllowF12AsGameplay)
            return true;
        if (IsWinKeyLabel(labelInt) && Main.Settings.AllowWinKeyAsGameplay)
            return true;
        if (labelInt == _LABEL_TAB && Main.Settings.AllowTabAsGameplay)
            return true;
        if (labelInt == _LABEL_RETURN && Main.Settings.AllowEnterAsGameplay)
            return true;
        if (labelInt == _LABEL_F4 && Main.Settings.AllowF4AsGameplay)
            return true;
        return false;
    }

    private static bool IsWinKeyLabel(int labelInt)
    {
        return labelInt == _LABEL_LWIN || labelInt == _LABEL_RWIN;
    }

    // 补丁 2：AsyncKeyboard.Main 后置 (F12)
    private static void MainPostfix(RDInputType_AsyncKeyboard __instance, ButtonState state, ref int __result)
    {
        var keys = __instance.pressCount.keys;
        bool toggle = Main.Settings.AllowF12AsGameplay;

        short phys = GetAsyncKeyState(VK_F12);
        bool f12Pressed = (phys & 0x0001) != 0;

        if (toggle && f12Pressed)
        {
            if (!keys.Exists(k => IsF12(k)))
            {
                var f12Code = new AsyncKeyCode((KeyLabel)12);
                keys.Add(new AnyKeyCode(f12Code));
                __result++;
            }
        }
        else if (!toggle)
        {
            int removed = keys.RemoveAll(k => IsF12(k));
            __result -= removed;
        }
    }

    // 补丁 3：CountSpecialInput 后缀
    private static void CountSpecialInputPostfix(ref List<KeyCode> __result)
    {
        if (!Main.Settings.AllowSpecialAsGameplay) return;

        var newList = new List<KeyCode>
        {
            KeyCode.Escape
        };
        if (!Main.Settings.AllowF12AsGameplay)
            newList.Add(KeyCode.F12);
        if (!Main.Settings.AllowWinKeyAsGameplay)
        {
            newList.Add(KeyCode.LeftWindows);
            newList.Add(KeyCode.RightWindows);
        }
        if (!Main.Settings.AllowTabAsGameplay)
            newList.Add(KeyCode.Tab);
        if (!Main.Settings.AllowEnterAsGameplay)
            newList.Add(KeyCode.Return);
        if (!Main.Settings.AllowF4AsGameplay)
            newList.Add(KeyCode.F4);

        __result = newList;
    }

    // 补丁 4：Keyboard.MainIgnoreActive 后置（注入特殊键）
    private static void KeyboardMainPostfix(RDInputType_Keyboard __instance, ButtonState state, ref int __result)
    {
        if (!Main.Settings.AllowSpecialAsGameplay) return;

        SampleSpecialKeysOncePerFrame();

        RDInputType.MainStateCount stateCount;
        switch (state)
        {
            case ButtonState.WentDown: stateCount = __instance.pressCount; break;
            case ButtonState.IsDown: stateCount = __instance.heldCount; break;
            case ButtonState.WentUp: stateCount = __instance.releaseCount; break;
            case ButtonState.IsUp: stateCount = __instance.isReleaseCount; break;
            default: return;
        }

        var keys = stateCount.keys;
        DedupeAltGr(keys, ref __result);
        if (Main.Settings.AllowWinKeyAsGameplay)
        {
            TryAddKey(keys, VK_LWIN, KeyCode.LeftWindows, state, ref __result);
            TryAddKey(keys, VK_RWIN, KeyCode.RightWindows, state, ref __result);
        }
        if (Main.Settings.AllowTabAsGameplay)
            TryAddKey(keys, VK_TAB, KeyCode.Tab, state, ref __result);
        if (Main.Settings.AllowEnterAsGameplay)
            TryAddKey(keys, VK_RETURN, KeyCode.Return, state, ref __result);
        if (Main.Settings.AllowF4AsGameplay)
            TryAddKey(keys, VK_F4, KeyCode.F4, state, ref __result);
    }

    private static void DedupeAltGr(List<AnyKeyCode> keys, ref int result)
    {
        if (!KeyExists(keys, KeyCode.RightAlt) || !KeyExists(keys, KeyCode.AltGr))
            return;
        int removed = keys.RemoveAll(k => k.value is KeyCode code && code == KeyCode.AltGr);
        result -= removed;
    }

    private static bool KeyExists(List<AnyKeyCode> keys, KeyCode kc)
    {
        foreach (var akc in keys)
        {
            try { if (akc.value is KeyCode code && code == kc) return true; }
            catch { }
        }
        return false;
    }

    private static void TryAddKey(List<AnyKeyCode> keys, int vk, KeyCode kc, ButtonState state, ref int result)
    {
        bool currentlyDown = _rawDown.TryGetValue(vk, out bool down) && down;
        bool wasDown = _prevDown.TryGetValue(vk, out bool prev) && prev;
        bool wentDown = currentlyDown && !wasDown;
        bool wentUp = !currentlyDown && wasDown;

        bool shouldAdd = state switch
        {
            ButtonState.WentDown => wentDown,
            ButtonState.IsDown => currentlyDown,
            ButtonState.WentUp => wentUp,
            ButtonState.IsUp => !currentlyDown,
            _ => false,
        };
        if (!shouldAdd) return;
        if (KeyExists(keys, kc)) return;

        keys.Add(new AnyKeyCode(kc));
        result++;
    }

    // ======================= ★ CheckKeyState 前缀（只处理特定键，Escape 不干预） =======================
    private static bool KeyboardCheckKeyStatePrefix(KeyCode key, ButtonState state, ref bool __result)
    {
        if (key == KeyCode.Escape)
            return true;

        if (!Main.Settings.AllowSpecialAsGameplay)
            return true;

        if (!IsKeyAllowedAsGameplay(key))
            return true;

        int vk = KeyCodeToVk(key);
        if (vk == -1) return true;

        SampleSpecialKeysOncePerFrame();

        bool currentlyDown = _rawDown.TryGetValue(vk, out bool down) && down;
        bool wasDown = _prevDown.TryGetValue(vk, out bool prev) && prev;

        bool result = state switch
        {
            ButtonState.WentDown => currentlyDown && !wasDown,
            ButtonState.IsDown => currentlyDown,
            ButtonState.WentUp => !currentlyDown && wasDown,
            ButtonState.IsUp => !currentlyDown,
            _ => false
        };

        __result = result;
        return false;
    }

    private static bool IsKeyAllowedAsGameplay(KeyCode key)
    {
        return key switch
        {
            KeyCode.LeftWindows => Main.Settings.AllowWinKeyAsGameplay,
            KeyCode.RightWindows => Main.Settings.AllowWinKeyAsGameplay,
            KeyCode.Return => Main.Settings.AllowEnterAsGameplay,
            KeyCode.Tab => Main.Settings.AllowTabAsGameplay,
            KeyCode.F4 => Main.Settings.AllowF4AsGameplay,
            _ => false
        };
    }

    // ======================= 辅助函数 =======================
    private static int KeyCodeToVk(KeyCode key)
    {
        return key switch
        {
            KeyCode.LeftWindows => VK_LWIN,
            KeyCode.RightWindows => VK_RWIN,
            KeyCode.Return => VK_RETURN,
            KeyCode.Tab => VK_TAB,
            KeyCode.F4 => VK_F4,
            // Escape 不映射，因为我们直接让它走原方法
            _ => -1
        };
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

    // ======================= 常量 =======================
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_RETURN = 0x0D;
    private const int VK_TAB = 0x09;
    private const int VK_F4 = 0x73;
    private const int VK_F12 = 0x7B;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}