using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed class SarhMissileTrackHud
{
    private readonly ConfigEntry<bool> enabled;
    private readonly ConfigEntry<float> fontSize;
    private readonly ConfigEntry<float> offset;
    private readonly Dictionary<Missile, Entry> entries = new Dictionary<Missile, Entry>();
    private readonly List<Missile> removed = new List<Missile>();
    private CombatHUD hud;
    private Aircraft aircraft;
    private TMP_FontAsset font;
    private Material fontMaterial;
    private float nextScan;

    internal SarhMissileTrackHud(ConfigFile config)
    {
        enabled = config.Bind("Missile HUD", "Enabled", true, "Display Track or NONE above your custom SARH missile's existing HUD icon.");
        fontSize = config.Bind("Missile HUD", "FontSize", 14f,
            new ConfigDescription("Track label font size in HUD units.", new AcceptableValueRange<float>(8f, 30f)));
        offset = config.Bind("Missile HUD", "VerticalOffset", 20f,
            new ConfigDescription("Distance above the missile icon in HUD units.", new AcceptableValueRange<float>(8f, 80f)));
    }

    private static bool TryContext(out CombatHUD currentHud, out Aircraft currentAircraft)
    {
        currentHud = SceneSingleton<CombatHUD>.i;
        currentAircraft = null;
        return (GameManager.gameState == GameState.SinglePlayer || GameManager.gameState == GameState.Multiplayer)
            && GameManager.GetLocalAircraft(out currentAircraft) && currentAircraft != null
            && currentAircraft.LocalSim && !currentAircraft.disabled && !currentAircraft.HasEjected()
            && currentHud != null && currentHud.aircraft == currentAircraft && currentHud.iconLayer != null;
    }

    internal void Refresh()
    {
        CombatHUD currentHud;
        Aircraft currentAircraft;
        if (!enabled.Value || !TryContext(out currentHud, out currentAircraft))
        {
            Clear();
            return;
        }
        if (currentHud != hud || currentAircraft != aircraft)
        {
            Clear();
            hud = currentHud;
            aircraft = currentAircraft;
            TextMeshProUGUI template = hud.GetComponentInChildren<TextMeshProUGUI>(true);
            if (template != null)
            {
                font = template.font;
                fontMaterial = template.fontSharedMaterial;
            }
            aircraft.onRegisterMissile += Register;
            nextScan = 0f;
        }
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.25f;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                Missile missile = unit as Missile;
                if (missile != null) Register(missile);
            }
        }
        Render();
    }

    private bool Eligible(Missile missile)
    {
        return missile != null && !missile.disabled && missile.owner == aircraft;
    }

    private void Register(Missile missile)
    {
        if (!Eligible(missile) || entries.ContainsKey(missile)) return;
        if (missile.GetComponent<RadarSARHSeeker>() == null) return;
        entries.Add(missile, new Entry());
        missile.onDisableUnit += MissileDisabled;
    }

    private void MissileDisabled(Unit unit)
    {
        Missile missile = unit as Missile;
        if (missile != null) Remove(missile);
    }

    internal void Render()
    {
        CombatHUD currentHud;
        Aircraft currentAircraft;
        if (!enabled.Value || !TryContext(out currentHud, out currentAircraft)
            || currentHud != hud || currentAircraft != aircraft)
        {
            Clear();
            return;
        }
        bool hudVisible = hud.isActiveAndEnabled && hud.iconLayer.gameObject.activeInHierarchy
            && !hud.landingMode && !aircraft.gearDeployed && !DynamicMap.mapMaximized;
        removed.Clear();
        foreach (KeyValuePair<Missile, Entry> pair in entries)
        {
            if (!Eligible(pair.Key))
            {
                removed.Add(pair.Key);
                continue;
            }
            HUDUnitMarker marker;
            Entry entry = pair.Value;
            if (!hud.TryGetMarker(pair.Key, out marker) || marker == null || marker.image == null)
            {
                entry.DestroyLabel();
                continue;
            }
            Image image = marker.image;
            bool visible = hudVisible && image.isActiveAndEnabled
                && image.canvas != null && image.canvas.isActiveAndEnabled;
            if (!visible)
            {
                if (entry.Label != null) entry.Label.enabled = false;
                continue;
            }
            if (entry.Label == null) entry.CreateLabel(image, font, fontMaterial);
            TextMeshProUGUI label = entry.Label;
            if (label.transform.parent != image.transform.parent)
                label.transform.SetParent(image.transform.parent, false);
            label.transform.localPosition = image.transform.parent.InverseTransformPoint(image.transform.position)
                + new Vector3(0f, offset.Value, 0f);
            label.fontSize = fontSize.Value;
            bool tracking = pair.Key.seekerMode == Missile.SeekerMode.activeLock && pair.Key.targetID.IsValid;
            string text = tracking ? "Track" : "NONE";
            if (label.text != text) label.text = text;
            label.color = new Color(1f, 1f, 1f, image.color.a);
            label.enabled = true;
        }
        foreach (Missile missile in removed) Remove(missile);
        removed.Clear();
    }

    private void Remove(Missile missile)
    {
        Entry entry;
        if (!entries.TryGetValue(missile, out entry)) return;
        if (missile != null) missile.onDisableUnit -= MissileDisabled;
        entry.DestroyLabel();
        entries.Remove(missile);
    }

    internal void Clear()
    {
        if (aircraft != null) aircraft.onRegisterMissile -= Register;
        foreach (KeyValuePair<Missile, Entry> pair in entries)
        {
            if (pair.Key != null) pair.Key.onDisableUnit -= MissileDisabled;
            pair.Value.DestroyLabel();
        }
        entries.Clear();
        removed.Clear();
        hud = null;
        aircraft = null;
        font = null;
        fontMaterial = null;
        nextScan = 0f;
    }

    private sealed class Entry
    {
        internal TextMeshProUGUI Label;
        internal void CreateLabel(Image image, TMP_FontAsset font, Material material)
        {
            GameObject labelObject = new GameObject("SARH missile track", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.layer = image.gameObject.layer;
            labelObject.transform.SetParent(image.transform.parent, false);
            Label = labelObject.GetComponent<TextMeshProUGUI>();
            if (font != null) Label.font = font;
            if (material != null) Label.fontSharedMaterial = material;
            Label.alignment = TextAlignmentOptions.Center;
            Label.enableWordWrapping = false;
            Label.overflowMode = TextOverflowModes.Overflow;
            Label.richText = false;
            Label.raycastTarget = false;
            Label.rectTransform.anchorMin = Label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            Label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Label.rectTransform.sizeDelta = new Vector2(150f, 24f);
            Label.enabled = false;
        }
        internal void DestroyLabel()
        {
            if (Label != null)
            {
                Label.enabled = false;
                Object.Destroy(Label.gameObject);
            }
            Label = null;
        }
    }
}
