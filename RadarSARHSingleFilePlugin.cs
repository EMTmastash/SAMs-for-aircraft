using System;
using System.IO;
using BepInEx;
using UnityEngine;

[BepInPlugin("aaa.local.nuclearoption.radarsarhloader", "SAMs for Aircraft SARH", "0.2.8")]
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
        if (!GameManager.IsHeadless)
            trackHud = new SarhMissileTrackHud(Config);
        Logger.LogInfo("Ready: " + seekerType.FullName + ", embedded SAMsForAircraftSARH.nobp and per-missile Track/NONE HUD.");
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

