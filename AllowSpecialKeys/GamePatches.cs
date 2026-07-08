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

    // ---- 物理键跟踪（所有被钩子条件拦截的键） ----
    private static int _lastSampleFrame = -1;
    private static readonly Dictionary<int, bool> _rawDown = new();
    private static readonly Dictionary<int, bool> _prevDown = new();

    private static readonly int[] TrackedVks =
    {
        VK_LWIN, VK_RWIN,
        VK_RETURN,   // Enter (Alt+Enter)
        VK_TAB,      // Alt+Tab
        VK_F4,       // Alt+F4
        VK_ESCAPE,   // Alt+Esc / Ctrl+Esc
    };

    public static void Register()
    {
        if (_patchesApplied) return;

        var harmony = Main.Harmony;

        // ========== 原有补丁（保持不变） ==========
        PatchGetSpecialInput(harmony);
        PatchAsyncMain(harmony);
        PatchCountSpecialInput(harmony);
        PatchKeyboardMainIgnoreActive(harmony);
        TryPatchKeyViewer(harmony);

        // ========== ★ 新增：修补 RDInputType_Keyboard.CheckKeyState ==========
        PatchCheckKeyState(harmony);

        _asyncLabelField = AccessTools.Field(typeof(AsyncKeyCode), "label");
        _patchesApplied = true;
    }

    // ---------- 各补丁注册方法 ----------
    private static void PatchGetSpecialInput(Harmony harmony)
    {
        var m = AccessTools.Method(typeof(RDInputType_AsyncKeyboard), "GetSpecialInput");
        if (m != null)
            harmony.Patch(m, prefix: new HarmonyMethod(typeof(GamePatches), nameof(GetSpecialInputPrefix)));
    }

    private static void PatchAsyncMain(Harmony harmony)
    {
        var m = AccessTools.Method(typeof(RDInputType_AsyncKeyboard), "Main", new[] { typeof(ButtonState) });
        if (m != null)
            harmony.Patch(m, postfix: new HarmonyMethod(typeof(GamePatches), nameof(MainPostfix)));
    }

    private static void PatchCountSpecialInput(Harmony harmony)
    {
        var m = AccessTools.Method(typeof(RDInputType_Keyboard), "CountSpecialInput");
        if (m != null)
            harmony.Patch(m, prefix: new HarmonyMethod(typeof(GamePatches), nameof(CountSpecialInputPrefix)));
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

    private static void TryPatchKeyViewer(Harmony harmony)
    {
        var kvType = AccessTools.TypeByName("JipperKeyViewer.KeyViewer.KeyViewer");
        if (kvType == null) return;
        var m = AccessTools.Method(kvType, "ProcessKeySelection");
        if (m == null) return;
        harmony.Patch(m, prefix: new HarmonyMethod(typeof(GamePatches), nameof(KvProcessKeySelPrefix)));
        Main.Mod.Logger.Log("Patched: KeyViewer.ProcessKeySelection (Alt/Win fix)");
    }

    // ---------- ★ 精确修补 RDInputType_Keyboard.CheckKeyState ----------
    private static void PatchCheckKeyState(Harmony harmony)
    {
        // 方法签名：internal static bool CheckKeyState(KeyCode key, ButtonState state = ButtonState.WentDown)
        var method = AccessTools.Method(typeof(RDInputType_Keyboard), "CheckKeyState",
            new[] { typeof(KeyCode), typeof(ButtonState) });
        if (method == null)
        {
            Main.Mod.Logger.Log("ERROR: RDInputType_Keyboard.CheckKeyState not found!");
            return;
        }

        harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(GamePatches), nameof(CheckKeyStatePrefix)));
        Main.Mod.Logger.Log("OK: Patched RDInputType_Keyboard.CheckKeyState");
    }

    public static void Unregister()
    {
        if (!_patchesApplied) return;
        Main.Harmony?.UnpatchAll(Main.Mod.Info.Id);
        _patchesApplied = false;
    }

    // ======================= 物理状态采样（每帧一次） =======================
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

    // ======================= 各补丁方法 =======================

    // 补丁1：GetSpecialInput 前缀
    private static bool GetSpecialInputPrefix(ref List<AsyncKeyCode> __result)
    {
        if (Main.Settings.AllowSpecialAsGameplay)
        {
            __result = new List<AsyncKeyCode>();
            return false;
        }
        return true;
    }

    // 补丁2：AsyncKeyboard.Main 后置 (控制 F12)
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

    // 补丁3：CountSpecialInput 前缀 (关闭特殊键过滤)
    private static bool CountSpecialInputPrefix(ref List<KeyCode> __result)
    {
        if (Main.Settings.AllowSpecialAsGameplay)
        {
            __result = new List<KeyCode>();
            return false;
        }
        return true;
    }

    // 补丁4：Keyboard.MainIgnoreActive 后置 (注入 Win 键，作为双保险)
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

        // 去重 AltGr（如果存在双重条目）
        DedupeAltGr(keys, ref __result);

        // 尝试添加左右 Win 键（如果物理状态匹配且不在列表中）
        TryAddKey(keys, VK_LWIN, KeyCode.LeftWindows, state, ref __result);
        TryAddKey(keys, VK_RWIN, KeyCode.RightWindows, state, ref __result);
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

    // ======================= ★ 核心补丁：RDInputType_Keyboard.CheckKeyState 前缀 =======================
    private static bool CheckKeyStatePrefix(KeyCode key, ButtonState state, ref bool __result)
    {
        // 只有在允许特殊键作为游戏输入时才干预
        if (!Main.Settings.AllowSpecialAsGameplay) return true;

        // 映射 KeyCode 到虚拟键码
        int vk = KeyCodeToVk(key);
        if (vk == -1) return true; // 不是我们需要处理的键

        // 确保物理状态已采样（带帧号防重）
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
        return false; // 跳过原始方法，使用我们计算的结果
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
            KeyCode.Escape => VK_ESCAPE,
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

    // ======================= KeyViewer 兼容补丁 =======================
    private static bool KvProcessKeySelPrefix(object __instance, int ___SelectedKey, int ___changeState)
    {
        if (___SelectedKey == -1 || ___changeState == 1 || !Application.isFocused)
            return true;

        if (Input.GetKeyDown(KeyCode.LeftAlt)) { CallSetupKey(__instance, KeyCode.LeftAlt); return false; }
        if (Input.GetKeyDown(KeyCode.RightAlt)) { CallSetupKey(__instance, KeyCode.RightAlt); return false; }

        if (KeyboardHook.IsKeyPhysicallyDown(VK_LWIN)) { CallSetupKey(__instance, KeyCode.LeftWindows); return false; }
        if (KeyboardHook.IsKeyPhysicallyDown(VK_RWIN)) { CallSetupKey(__instance, KeyCode.RightWindows); return false; }

        return true;
    }

    private static void CallSetupKey(object instance, KeyCode key)
    {
        var m = AccessTools.Method(instance.GetType(), "SetupKey", new[] { typeof(KeyCode) });
        try { m?.Invoke(instance, new object[] { key }); } catch { }
    }

    // ======================= 常量 =======================
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_RETURN = 0x0D;
    private const int VK_TAB = 0x09;
    private const int VK_F4 = 0x73;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F12 = 0x7B;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}