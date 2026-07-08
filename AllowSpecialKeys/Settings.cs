using System;
using System.IO;
using UnityEngine;
using UnityModManagerNet;
using static UnityModManagerNet.UnityModManager.Param;

namespace AllowSpecialKeys;

public class Settings
{
    public bool BlockWinKey = true;
    public bool BlockAltTab = true;
    public bool BlockAltEnter = false;
    public bool BlockCtrlEsc = true;
    public bool AllowSpecialAsGameplay = false;
    public bool AllowF12AsGameplay = false;
    public int Language; // 0=中文, 1=English, 2=한국어

    public static Settings Load(UnityModManager.ModEntry modEntry)
    {
        string path = Path.Combine(modEntry.Path, "config.json");
        if (File.Exists(path))
        {
            try
            {
                return JsonUtility.FromJson<Settings>(File.ReadAllText(path));
            }
            catch
            {
                return new Settings();
            }
        }
        return new Settings();
    }

    public void Save(UnityModManager.ModEntry modEntry)
    {
        string path = Path.Combine(modEntry.Path, "config.json");
        File.WriteAllText(path, JsonUtility.ToJson(this, prettyPrint: true));
    }

    public static void OnSaveGUI(UnityModManager.ModEntry modEntry)
    {
        Main.Mod.Logger.Log("Settings applied.");
        Main.Hook?.UpdateBlockedKeys(Main.Settings);
        Main.Settings.Save(modEntry);
    }

    internal void OnHideGUI(UnityModManager.ModEntry modEntry)
    {
        Main.Mod.Logger.Log("Settings applied.");
        Main.Hook?.UpdateBlockedKeys(Main.Settings);
        Main.Settings.Save(modEntry);
    }
}
