using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SkillPeak
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class SkillPeakPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "gooop.valheim.skillpeak";
        public const string PluginName = "SkillPeak";
        public const string PluginVersion = "1.0.0";

        internal static ConfigEntry<bool> ConfigEnabled;

        internal static readonly Dictionary<Skills.SkillType, float> MaxLevels = new Dictionary<Skills.SkillType, float>();

        private void Awake()
        {
            ConfigEnabled = Config.Bind("General", "Enabled", true,
                "Show a tick mark on each skill bar for the highest level that skill has ever reached.");

            new Harmony(PluginGUID).PatchAll(Assembly.GetExecutingAssembly());
        }

        internal static void RecordMax(Skills.SkillType type, float level)
        {
            if (!MaxLevels.TryGetValue(type, out float current) || level > current)
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
                Transform found = FindChildRecursive(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }

    // Track the highest level ever reached, independent of skill loss on death.
    [HarmonyPatch(typeof(Skills.Skill), nameof(Skills.Skill.Raise))]
    public static class Patch_Skill_Raise
    {
        private static void Postfix(Skills.Skill __instance)
        {
            SkillPeakPlugin.RecordMax(__instance.m_info.m_skill, __instance.m_level);
        }
    }

    // Draw/update the "|" peak-level tick mark on each skill's bar in the skills menu.
    [HarmonyPatch(typeof(SkillsDialog), "Setup")]
    public static class Patch_SkillsDialog_Setup
    {
        private const string TickName = "SkillPeak_Tick";
        private static readonly Color TickColor = new Color(1f, 0.93f, 0.6f, 0.95f);
        private const float TickWidth = 2f;
        private const float TickOverflow = 4f;

        // SkillsDialog.m_elements is private, so reach it through Harmony's field accessor.
        private static readonly AccessTools.FieldRef<SkillsDialog, List<GameObject>> ElementsField =
            AccessTools.FieldRefAccess<SkillsDialog, List<GameObject>>("m_elements");

        private static void Postfix(SkillsDialog __instance, Player player)
        {
            if (!SkillPeakPlugin.ConfigEnabled.Value || player == null)
            {
                return;
            }

            List<GameObject> elements = ElementsField(__instance);
            List<Skills.Skill> skills = player.GetSkills().GetSkillList();
            for (int i = 0; i < skills.Count && i < elements.Count; i++)
            {
                Skills.SkillType type = skills[i].m_info.m_skill;
                SkillPeakPlugin.RecordMax(type, skills[i].m_level);

                if (!SkillPeakPlugin.MaxLevels.TryGetValue(type, out float maxLevel) || maxLevel <= 0f)
                {
                    continue;
                }

                GameObject element = elements[i];
                Transform barTransform = SkillPeakPlugin.FindChildRecursive(element.transform, "currentlevel");
                RectTransform barRect = barTransform == null ? null : barTransform.GetComponent<RectTransform>();
                if (barRect == null)
                {
                    continue;
                }

                PositionTick(element.transform, barRect, Mathf.Clamp01(maxLevel / 100f));
            }
        }

        private static void PositionTick(Transform elementTransform, RectTransform barRect, float fraction)
        {
            Transform existing = SkillPeakPlugin.FindChildRecursive(elementTransform, TickName);
            RectTransform tickRect;
            if (existing != null)
            {
                tickRect = (RectTransform)existing;
            }
            else
            {
                GameObject tickGO = new GameObject(TickName, typeof(RectTransform), typeof(Image));
                tickGO.transform.SetParent(barRect.parent, false);
                tickRect = (RectTransform)tickGO.transform;
                tickRect.anchorMin = barRect.anchorMin;
                tickRect.anchorMax = barRect.anchorMax;
                tickRect.pivot = new Vector2(0f, 0.5f);

                Image img = tickGO.GetComponent<Image>();
                img.color = TickColor;
                img.raycastTarget = false;
            }

            tickRect.SetAsLastSibling();

            float barWidth = barRect.rect.width;
            float barHeight = barRect.rect.height;
            float leftEdgeX = barRect.anchoredPosition.x - barRect.pivot.x * barWidth;
            float centerY = barRect.anchoredPosition.y + (0.5f - barRect.pivot.y) * barHeight;

            tickRect.sizeDelta = new Vector2(TickWidth, barHeight + TickOverflow);
            tickRect.anchoredPosition = new Vector2(leftEdgeX + fraction * barWidth, centerY);
        }
    }

    // Persist peak levels on the character save, keyed to the local player.
    [HarmonyPatch(typeof(Player), nameof(Player.Save))]
    public static class Patch_Player_Save
    {
        private const string SaveKey = "SkillPeak_MaxLevels";
        private const int SaveVersion = 1;

        private static void Prefix(Player __instance)
        {
            if (__instance == null || Player.m_localPlayer != __instance)
            {
                return;
            }

            ZPackage pkg = new ZPackage();
            pkg.Write(SaveVersion);
            pkg.Write(SkillPeakPlugin.MaxLevels.Count);
            foreach (KeyValuePair<Skills.SkillType, float> kv in SkillPeakPlugin.MaxLevels)
            {
                pkg.Write((int)kv.Key);
                pkg.Write(kv.Value);
            }
            __instance.m_customData[SaveKey] = pkg.GetBase64();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    public static class Patch_Player_OnSpawned
    {
        private const string SaveKey = "SkillPeak_MaxLevels";
        private const int SaveVersion = 1;
        internal static bool Loaded = false;

        private static void Postfix(Player __instance)
        {
            if (__instance == null || Player.m_localPlayer != __instance || Loaded)
            {
                return;
            }

            if (__instance.m_customData.TryGetValue(SaveKey, out string base64))
            {
                ZPackage pkg = new ZPackage(base64);
                int version = pkg.ReadInt();
                if (version == SaveVersion)
                {
                    int count = pkg.ReadInt();
                    for (int i = 0; i < count; i++)
                    {
                        Skills.SkillType type = (Skills.SkillType)pkg.ReadInt();
                        float level = pkg.ReadSingle();
                        SkillPeakPlugin.RecordMax(type, level);
                    }
                }
            }

            // Seed from current levels too, so an existing character gets correct
            // peaks the first time this mod is installed, before any skill is raised again.
            foreach (Skills.Skill skill in __instance.GetSkills().GetSkillList())
            {
                SkillPeakPlugin.RecordMax(skill.m_info.m_skill, skill.m_level);
            }

            Loaded = true;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.Logout))]
    public static class Patch_Game_Logout
    {
        private static void Prefix()
        {
            Patch_Player_OnSpawned.Loaded = false;
            SkillPeakPlugin.MaxLevels.Clear();
        }
    }
}
