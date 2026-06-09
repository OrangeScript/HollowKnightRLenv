using System;
using System.Collections.Generic;
using UnityEngine;

namespace HollowKnightRLBridge
{
    internal sealed class RLDebugOverlay : MonoBehaviour
    {
        private const int MaxRows = 38;

        private RLStepResult snapshot;
        private bool visible = true;
        private Vector2 scroll;

        public void SetSnapshot(RLStepResult result)
        {
            snapshot = result;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                visible = !visible;
            }
        }

        private void OnGUI()
        {
            if (!visible || snapshot == null || snapshot.Info == null)
            {
                return;
            }

            Rect rect = new Rect(12f, 12f, 520f, 620f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f));
            GUILayout.Label("HollowKnightRLBridge  F8 hide/show");
            GUILayout.Label("obs=" + SafeInfo("observation_size") + "  profile=" + SafeInfo("boss_profile") + "  scene=" + SafeInfo("scene"));
            GUILayout.Label("done=" + snapshot.Done + "  hero_dead=" + SafeInfo("hero_dead") + "  boss_dead=" + SafeInfo("boss_dead"));

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Width(rect.width - 20f), GUILayout.Height(rect.height - 90f));
            DrawImportantRows();
            DrawRemainingRows();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawImportantRows()
        {
            string[] keys =
            {
                "hero_health",
                "hero_soul",
                "hero_delta",
                "can_input",
                "boss_count",
                "boss_total_hp",
                "target_name",
                "target_hp",
                "boss_damage",
                "boss_feature_names",
                "boss_features",
                "hornet_obstacle_distance",
                "hornet_obstacle_name",
                "markoth_shield_distance",
                "markoth_shield_name",
                "markoth_weapon_distance",
                "markoth_weapon_name",
                "markoth_platform_distance",
                "markoth_platform_name",
                "reset_combat_ready",
                "reset_enemy_ready",
                "reset_hero_arena_ready"
            };

            for (int i = 0; i < keys.Length; i++)
            {
                DrawRow(keys[i]);
            }
        }

        private void DrawRemainingRows()
        {
            int rows = 0;
            foreach (KeyValuePair<string, object> pair in snapshot.Info)
            {
                if (rows >= MaxRows)
                {
                    GUILayout.Label("...");
                    return;
                }

                if (IsImportant(pair.Key))
                {
                    continue;
                }

                GUILayout.Label(pair.Key + ": " + FormatValue(pair.Value));
                rows++;
            }
        }

        private void DrawRow(string key)
        {
            if (snapshot.Info.ContainsKey(key))
            {
                GUILayout.Label(key + ": " + FormatValue(snapshot.Info[key]));
            }
        }

        private bool IsImportant(string key)
        {
            string[] keys =
            {
                "hero_health",
                "hero_soul",
                "hero_delta",
                "can_input",
                "boss_count",
                "boss_total_hp",
                "target_name",
                "target_hp",
                "boss_damage",
                "boss_feature_names",
                "boss_features",
                "hornet_obstacle_distance",
                "hornet_obstacle_name",
                "markoth_shield_distance",
                "markoth_shield_name",
                "markoth_weapon_distance",
                "markoth_weapon_name",
                "markoth_platform_distance",
                "markoth_platform_name",
                "reset_combat_ready",
                "reset_enemy_ready",
                "reset_hero_arena_ready"
            };

            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] == key)
                {
                    return true;
                }
            }

            return false;
        }

        private string SafeInfo(string key)
        {
            if (snapshot == null || snapshot.Info == null || !snapshot.Info.ContainsKey(key))
            {
                return string.Empty;
            }

            return FormatValue(snapshot.Info[key]);
        }

        private static string FormatValue(object value)
        {
            if (value == null)
            {
                return "null";
            }

            float floatValue;
            if (value is float)
            {
                floatValue = (float)value;
                return floatValue.ToString("0.###");
            }

            if (value is double)
            {
                return ((double)value).ToString("0.###");
            }

            if (value is float[])
            {
                return FormatFloatArray((float[])value);
            }

            if (value is bool[])
            {
                return FormatBoolArray((bool[])value);
            }

            if (value is string[])
            {
                return string.Join(", ", (string[])value);
            }

            return value.ToString();
        }

        private static string FormatFloatArray(float[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            int count = Math.Min(values.Length, 12);
            string[] text = new string[count];
            for (int i = 0; i < count; i++)
            {
                text[i] = values[i].ToString("0.###");
            }

            return "[" + string.Join(", ", text) + (values.Length > count ? ", ..." : string.Empty) + "]";
        }

        private static string FormatBoolArray(bool[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            int count = Math.Min(values.Length, 24);
            char[] text = new char[count];
            for (int i = 0; i < count; i++)
            {
                text[i] = values[i] ? '1' : '0';
            }

            return new string(text) + (values.Length > count ? "..." : string.Empty);
        }
    }
}
