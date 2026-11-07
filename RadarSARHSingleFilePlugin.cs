using System;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

[BepInPlugin("aaa.local.nuclearoption.radarsarhloader", "SAMs for Aircraft SARH", "0.3.1")]
[BepInProcess("NuclearOption.exe")]
[DefaultExecutionOrder(20000)]
public sealed class RadarSARHSingleFilePlugin : BaseUnityPlugin
{
    private SarhMissileTrackHud trackHud;
    private bool hudFailed;

    private void Awake()
    {
        Type seekerType = typeof(RadarSARHSeeker);
        if (!typeof(MissileSeeker).IsAssignableFrom(seekerType))
        {
            throw new TypeLoadException("RadarSARHSeeker does not inherit the game's MissileSeeker.");
        }
        if (seekerType.Assembly.GetManifestResourceInfo("SAMsForAircraftSARH.nobp") == null)
        {
            throw new InvalidDataException("The embedded SAMsForAircraftSARH.nobp bundle is missing.");
        }
        RadarSARHSeeker.ResolveBvrSettings = ReadBvrSettings;
        if (!GameManager.IsHeadless)
            trackHud = new SarhMissileTrackHud(Config);
        Logger.LogInfo("Ready: " + seekerType.FullName + ", embedded SAMsForAircraftSARH.nobp and per-missile Track/NONE HUD.");
    }

    private static RadarSARHSeeker.BvrSettings ReadBvrSettings(Missile missile)
    {
        // Resolve at launch so the plugin can start before BVR and still see its bound settings.
        if (!Chainloader.PluginInfos.TryGetValue("com.Draken0015.BVR", out PluginInfo bvr) || bvr.Instance == null)
            return null;

        string missileType;
        switch (missile.GetWeaponInfo().name)
        {
            case "air_info_SAM_Radar1": missileType = "RAM45"; break;
            case "air_info_SAM_Radar2": missileType = "R9"; break;
            default: return null;
        }

        ConfigFile config = bvr.Instance.Config;
        float? persistence = null;
        if (ReadBvrEntry<bool>(config, "Enable " + missileType + " Lock Persistence")?.Value == true)
            persistence = ReadBvrSeconds(config, missileType + " Lock Persistence Value");

        float? relockDelay = null;
        int attempts = 0;
        if (ReadBvrEntry<bool>(config, "Enable " + missileType + " SARH Relock")?.Value == true)
        {
            ConfigEntry<int> attemptEntry = ReadBvrEntry<int>(config, missileType + " SARH Relock Attempts");
            if (attemptEntry != null)
            {
                attempts = Math.Max(0, attemptEntry.Value);
                relockDelay = ReadBvrSeconds(config, missileType + " SARH Relock Delay");
            }
        }

        return new RadarSARHSeeker.BvrSettings(persistence, relockDelay, attempts);
    }

    private static ConfigEntry<T> ReadBvrEntry<T>(ConfigFile config, string key)
    {
        try
        {
            config.TryGetEntry("Missile Balance - SARH", key, out ConfigEntry<T> entry);
            return entry;
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }

    private static float? ReadBvrSeconds(ConfigFile config, string key)
    {
        float? value = ReadBvrEntry<float>(config, key)?.Value;
        return value is float seconds && !float.IsNaN(seconds) && !float.IsInfinity(seconds)
            ? (float?)Math.Max(0f, seconds)
            : null;
    }

    private void OnEnable() { Canvas.willRenderCanvases += RenderHud; }
    private void OnDisable() { Canvas.willRenderCanvases -= RenderHud; trackHud?.Clear(); }
    private void OnDestroy() { Canvas.willRenderCanvases -= RenderHud; trackHud?.Clear(); }

    private void LateUpdate()
    {
        if (trackHud == null || hudFailed) return;
        try { trackHud.Refresh(); }
        catch (Exception exception) { StopHud(exception); }
    }

    private void RenderHud()
    {
        if (trackHud == null || hudFailed || !isActiveAndEnabled) return;
        try { trackHud.Render(); }
        catch (Exception exception) { StopHud(exception); }
    }

    private void StopHud(Exception exception)
    {
        hudFailed = true;
        trackHud.Clear();
        Logger.LogError("Missile track labels stopped after an error; seeker/bundle remain loaded. " + exception);
    }
}

