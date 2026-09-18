using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SkillPeak
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class SkillPeakPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "net.gooop.valheim.skillpeak";
        public const string PluginName = "SkillPeak";
        public const string PluginVersion = "1.0.0";

        internal static ConfigEntry<bool> ConfigEnabled;

        internal static readonly Dictionary<Skills.SkillType, float> MaxLevels = new();

        private void Awake()
        {
            ConfigEnabled = Config.Bind(
                section: "General",
                key: "Enabled",
                defaultValue: true,
                description: "Show a tick mark on each skill bar for the highest level that skill has ever reached."
            );

            new Harmony(PluginGuid).PatchAll(Assembly.GetExecutingAssembly());
        }

        internal static void RecordMax(Skills.SkillType type, float level)
        {
            if (!MaxLevels.TryGetValue(type, out var currentLevel) || level > currentLevel)
            {
                MaxLevels[type] = level;
            }
        }

        internal static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name)
            {
                return parent;
            }
            foreach (Transform child in parent)
            {
                var found = FindChildRecursive(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(Skills.Skill), nameof(Skills.Skill.Raise))]
    public static class PatchSkillRaise
    {
        // ReSharper disable once InconsistentNaming (__instance used in reflection by HarmonyLib)
        // ReSharper disable UnusedMember.Local
        private static void Postfix(Skills.Skill __instance)
        {
            SkillPeakPlugin.RecordMax(__instance.m_info.m_skill, __instance.m_level);
        }
    }

    // Draw/update the "|" peak-level tick mark on each skill's bar in the skills menu.
    [HarmonyPatch(typeof(SkillsDialog), "Setup")]
    public static class PatchSkillsDialogSetup
    {
        private const string TickName = "SkillPeak_Tick";
        private static readonly Color TickColor = new Color(1f, 0.93f, 0.6f, 0.95f);
        private const float TickWidth = 2f;
        private const float TickOverflow = 4f;

        // SkillsDialog.m_elements is private, so reach it through Harmony's field accessor.
        private static readonly AccessTools.FieldRef<SkillsDialog, List<GameObject>> ElementsField =
            AccessTools.FieldRefAccess<SkillsDialog, List<GameObject>>("m_elements");

        // ReSharper disable once InconsistentNaming (__instance used in reflection by HarmonyLib)
        // ReSharper disable UnusedMember.Local
        private static void Postfix(SkillsDialog __instance, Player player)
        {
            if (!SkillPeakPlugin.ConfigEnabled.Value || player == null)
            {
                return;
            }

            var elements = ElementsField(__instance);
            var skills = player.GetSkills().GetSkillList();
            for (var i = 0; i < skills.Count && i < elements.Count; i++)
            {
                var type = skills[i].m_info.m_skill;
                SkillPeakPlugin.RecordMax(type, skills[i].m_level);

                if (
                    !SkillPeakPlugin.MaxLevels.TryGetValue(type, out var maxLevel)
                    || maxLevel <= 0f
                )
                {
                    continue;
                }

                var element = elements[i];
                var barTransform = SkillPeakPlugin.FindChildRecursive(
                    element.transform,
                    "currentlevel"
                );
                var barRect =
                    barTransform == null ? null : barTransform.GetComponent<RectTransform>();
                if (barRect == null)
                {
                    continue;
                }

                PositionTick(element.transform, barRect, Mathf.Clamp01(maxLevel / 100f));
            }
        }

        private static void PositionTick(
            Transform elementTransform,
            RectTransform barRect,
            float fraction
        )
        {
            var existing = SkillPeakPlugin.FindChildRecursive(elementTransform, TickName);
            RectTransform tickRect;
            if (existing != null)
            {
                tickRect = (RectTransform)existing;
            }
            else
            {
                var tick = new GameObject(TickName, typeof(RectTransform), typeof(Image));
                tick.transform.SetParent(barRect.parent, false);
                tickRect = (RectTransform)tick.transform;
                tickRect.anchorMin = barRect.anchorMin;
                tickRect.anchorMax = barRect.anchorMax;
                tickRect.pivot = new Vector2(0f, 0.5f);

                var image = tick.GetComponent<Image>();
                image.color = TickColor;
                image.raycastTarget = false;
            }

            tickRect.SetAsLastSibling();

            var barWidth = barRect.rect.width;
            var barHeight = barRect.rect.height;
            var leftEdgeX = barRect.anchoredPosition.x - barRect.pivot.x * barWidth;
            var centerY = barRect.anchoredPosition.y + (0.5f - barRect.pivot.y) * barHeight;

            tickRect.sizeDelta = new Vector2(TickWidth, barHeight + TickOverflow);
            tickRect.anchoredPosition = new Vector2(leftEdgeX + fraction * barWidth, centerY);
        }
    }

    // Persist peak levels on the character save, keyed to the local player.
    [HarmonyPatch(typeof(Player), nameof(Player.Save))]
    public static class PatchPlayerSave
    {
        private const string SaveKey = "SkillPeak_MaxLevels";
        private const int SaveVersion = 1;

        // ReSharper disable once InconsistentNaming (__instance used in reflection by HarmonyLib)
        private static void Prefix(Player __instance)
        {
            if (__instance == null || Player.m_localPlayer != __instance)
            {
                return;
            }

            var package = new ZPackage();
            package.Write(SaveVersion);
            package.Write(SkillPeakPlugin.MaxLevels.Count);
            foreach (var kvp in SkillPeakPlugin.MaxLevels)
            {
                package.Write((int)kvp.Key);
                package.Write(kvp.Value);
            }
            __instance.m_customData[SaveKey] = package.GetBase64();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    public static class PatchPlayerOnSpawned
    {
        private const string SaveKey = "SkillPeak_MaxLevels";
        private const int SaveVersion = 1;
        internal static bool Loaded = false;

        // ReSharper disable once InconsistentNaming (__instance used in reflection by HarmonyLib)
        private static void Postfix(Player __instance)
        {
            if (__instance == null || Player.m_localPlayer != __instance || Loaded)
            {
                return;
            }

            if (__instance.m_customData.TryGetValue(SaveKey, out var base64))
            {
                var package = new ZPackage(base64);
                var version = package.ReadInt();
                if (version == SaveVersion)
                {
                    var count = package.ReadInt();
                    for (var i = 0; i < count; i++)
                    {
                        var type = (Skills.SkillType)package.ReadInt();
                        var level = package.ReadSingle();
                        SkillPeakPlugin.RecordMax(type, level);
                    }
                }
            }

            // Seed from current levels too, so an existing character gets correct
            // peaks the first time this mod is installed, before any skill is raised again.
            foreach (var skill in __instance.GetSkills().GetSkillList())
            {
                SkillPeakPlugin.RecordMax(skill.m_info.m_skill, skill.m_level);
            }

            Loaded = true;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.Logout))]
    public static class PatchGameLogout
    {
        private static void Prefix()
        {
            PatchPlayerOnSpawned.Loaded = false;
            SkillPeakPlugin.MaxLevels.Clear();
        }
    }
}
