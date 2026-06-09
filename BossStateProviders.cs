using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HollowKnightRLBridge
{
    internal interface IBossStateProvider
    {
        string Id { get; }
        string DisplayName { get; }
        int FeatureCount { get; }
        string[] FeatureNames { get; }
        bool IsActive(BossStateContext context);
        void Read(BossStateContext context, float[] features, Dictionary<string, object> info);
    }

    internal sealed class BossStateContext
    {
        public string Scene;
        public HeroController Hero;
        public Vector3 HeroPosition;
        public HealthManager[] HealthManagers;

        public static BossStateContext Capture()
        {
            GameManager gm = GameManager.instance;
            HeroController hero = HeroController.instance;
            return new BossStateContext
            {
                Scene = gm != null ? gm.sceneName : string.Empty,
                Hero = hero,
                HeroPosition = hero != null ? hero.transform.position : Vector3.zero,
                HealthManagers = UnityEngine.Object.FindObjectsOfType<HealthManager>()
            };
        }
    }

    internal sealed class BossStateReadout
    {
        public string ProviderId = "default";
        public string DisplayName = "Default";
        public int FeatureCount;
        public int FeatureCapacity;
        public string[] FeatureNames = new string[0];
        public float[] FeatureVector = new float[0];
        public float[] ActiveFeatures = new float[0];
        public Dictionary<string, object> Info = new Dictionary<string, object>();
    }

    internal sealed class BossStateRegistry
    {
        public const int MaxFeatureCount = 12;

        private readonly IBossStateProvider[] providers =
        {
            new HornetBossStateProvider(),
            new MarkothBossStateProvider(),
            new DefaultBossStateProvider()
        };

        public BossStateReadout Read()
        {
            BossStateContext context = BossStateContext.Capture();
            IBossStateProvider provider = Resolve(context);
            float[] featureVector = new float[MaxFeatureCount];
            Dictionary<string, object> providerInfo = new Dictionary<string, object>();

            try
            {
                provider.Read(context, featureVector, providerInfo);
            }
            catch (Exception e)
            {
                providerInfo["boss_provider_error"] = e.Message;
            }

            int activeCount = Math.Min(provider.FeatureCount, MaxFeatureCount);
            float[] activeFeatures = new float[activeCount];
            Array.Copy(featureVector, activeFeatures, activeCount);

            return new BossStateReadout
            {
                ProviderId = provider.Id,
                DisplayName = provider.DisplayName,
                FeatureCount = activeCount,
                FeatureCapacity = MaxFeatureCount,
                FeatureNames = provider.FeatureNames,
                FeatureVector = featureVector,
                ActiveFeatures = activeFeatures,
                Info = providerInfo
            };
        }

        private IBossStateProvider Resolve(BossStateContext context)
        {
            for (int i = 0; i < providers.Length; i++)
            {
                IBossStateProvider provider = providers[i];
                if (provider.Id != "default" && provider.IsActive(context))
                {
                    return provider;
                }
            }

            return providers[providers.Length - 1];
        }
    }

    internal sealed class DefaultBossStateProvider : IBossStateProvider
    {
        public string Id => "default";
        public string DisplayName => "Default";
        public int FeatureCount => 0;
        public string[] FeatureNames => new string[0];
        public bool IsActive(BossStateContext context) => true;

        public void Read(BossStateContext context, float[] features, Dictionary<string, object> info)
        {
        }
    }

    internal sealed class HornetBossStateProvider : IBossStateProvider
    {
        private static readonly string[] ObstacleNames =
        {
            "spike",
            "spikes",
            "spike_ball",
            "ball",
            "gossamer",
            "thread",
            "trap",
            "hazard"
        };

        private static readonly string[] ExcludedNames =
        {
            "hero",
            "knight",
            "camera",
            "corpse"
        };

        public string Id => "hornet";
        public string DisplayName => "Hornet";
        public int FeatureCount => 4;
        public string[] FeatureNames => new[]
        {
            "hornet_obstacle_dx",
            "hornet_obstacle_dy",
            "hornet_obstacle_distance",
            "hornet_obstacle_count"
        };

        public bool IsActive(BossStateContext context)
        {
            return BossStateUtil.TextContains(context.Scene, "hornet") || BossStateUtil.HasHealthManagerName(context, "hornet");
        }

        public void Read(BossStateContext context, float[] features, Dictionary<string, object> info)
        {
            BossNearestObject nearest = BossStateUtil.FindNearestNamedCollider(context, ObstacleNames, ExcludedNames, false);
            BossStateUtil.WriteNearestFeatures(features, 0, nearest);

            info["hornet_obstacle_found"] = nearest.Found;
            info["hornet_obstacle_count"] = nearest.Count;
            info["hornet_obstacle_name"] = nearest.Name ?? string.Empty;
            info["hornet_obstacle_distance"] = nearest.Found ? nearest.Distance : -1f;
            info["hornet_obstacle_dx"] = nearest.Found ? nearest.Dx : 0f;
            info["hornet_obstacle_dy"] = nearest.Found ? nearest.Dy : 0f;
        }
    }

    internal sealed class MarkothBossStateProvider : IBossStateProvider
    {
        private static readonly string[] ShieldNames =
        {
            "shield",
            "orbit",
            "guard"
        };

        private static readonly string[] WeaponNames =
        {
            "nail",
            "sword",
            "weapon",
            "projectile",
            "spear",
            "slash"
        };

        private static readonly string[] PlatformNames =
        {
            "platform",
            "plat",
            "floor",
            "tile",
            "terrain",
            "ledge",
            "block"
        };

        private static readonly string[] ExcludedNames =
        {
            "hero",
            "knight",
            "camera",
            "corpse"
        };

        public string Id => "markoth";
        public string DisplayName => "Markoth";
        public int FeatureCount => 10;
        public string[] FeatureNames => new[]
        {
            "markoth_shield_dx",
            "markoth_shield_dy",
            "markoth_shield_distance",
            "markoth_weapon_dx",
            "markoth_weapon_dy",
            "markoth_weapon_distance",
            "markoth_platform_dx",
            "markoth_platform_dy",
            "markoth_platform_distance",
            "markoth_platform_count"
        };

        public bool IsActive(BossStateContext context)
        {
            return BossStateUtil.TextContains(context.Scene, "markoth") || BossStateUtil.HasHealthManagerName(context, "markoth");
        }

        public void Read(BossStateContext context, float[] features, Dictionary<string, object> info)
        {
            BossNearestObject shield = BossStateUtil.FindNearestNamedObject(context, ShieldNames, ExcludedNames);
            BossNearestObject weapon = BossStateUtil.FindNearestNamedObject(context, WeaponNames, ExcludedNames);
            BossNearestObject platform = BossStateUtil.FindNearestPlatform(context, PlatformNames, ExcludedNames);

            BossStateUtil.WriteNearestFeatures(features, 0, shield);
            BossStateUtil.WriteNearestFeatures(features, 3, weapon);
            BossStateUtil.WriteNearestFeatures(features, 6, platform);
            features[9] = BossStateUtil.ScaleCount(platform.Count);

            info["markoth_shield_found"] = shield.Found;
            info["markoth_shield_name"] = shield.Name ?? string.Empty;
            info["markoth_shield_distance"] = shield.Found ? shield.Distance : -1f;
            info["markoth_shield_dx"] = shield.Found ? shield.Dx : 0f;
            info["markoth_shield_dy"] = shield.Found ? shield.Dy : 0f;

            info["markoth_weapon_found"] = weapon.Found;
            info["markoth_weapon_name"] = weapon.Name ?? string.Empty;
            info["markoth_weapon_distance"] = weapon.Found ? weapon.Distance : -1f;
            info["markoth_weapon_dx"] = weapon.Found ? weapon.Dx : 0f;
            info["markoth_weapon_dy"] = weapon.Found ? weapon.Dy : 0f;

            info["markoth_platform_found"] = platform.Found;
            info["markoth_platform_count"] = platform.Count;
            info["markoth_platform_name"] = platform.Name ?? string.Empty;
            info["markoth_platform_distance"] = platform.Found ? platform.Distance : -1f;
            info["markoth_platform_dx"] = platform.Found ? platform.Dx : 0f;
            info["markoth_platform_dy"] = platform.Found ? platform.Dy : 0f;
        }
    }

    internal struct BossNearestObject
    {
        public bool Found;
        public string Name;
        public float Distance;
        public float Dx;
        public float Dy;
        public int Count;
    }

    internal static class BossStateUtil
    {
        private const float PositionScale = 0.05f;
        private const float CountScale = 0.1f;

        public static bool HasHealthManagerName(BossStateContext context, string needle)
        {
            if (context == null || context.HealthManagers == null)
            {
                return false;
            }

            for (int i = 0; i < context.HealthManagers.Length; i++)
            {
                HealthManager manager = context.HealthManagers[i];
                if (manager != null && manager.gameObject != null && TextContains(manager.gameObject.name, needle))
                {
                    return true;
                }
            }

            return false;
        }

        public static BossNearestObject FindNearestNamedObject(BossStateContext context, string[] includeNames, string[] excludeNames)
        {
            BossNearestObject colliderMatch = FindNearestNamedCollider(context, includeNames, excludeNames, true);
            BossNearestObject transformMatch = FindNearestNamedTransform(context, includeNames, excludeNames);

            if (!colliderMatch.Found)
            {
                return transformMatch;
            }

            if (!transformMatch.Found)
            {
                return colliderMatch;
            }

            colliderMatch.Count += transformMatch.Count;
            return colliderMatch.Distance <= transformMatch.Distance ? colliderMatch : transformMatch;
        }

        public static BossNearestObject FindNearestNamedCollider(BossStateContext context, string[] includeNames, string[] excludeNames, bool includeTriggers)
        {
            BossNearestObject nearest = EmptyNearest();
            if (context == null || context.Hero == null)
            {
                return nearest;
            }

            Vector2 hero = new Vector2(context.HeroPosition.x, context.HeroPosition.y);
            Collider2D[] colliders = UnityEngine.Object.FindObjectsOfType<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || !collider.enabled || collider.gameObject == null || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!includeTriggers && collider.isTrigger)
                {
                    continue;
                }

                if (IsHeroObject(collider.gameObject, context.Hero))
                {
                    continue;
                }

                string text = ObjectText(collider.transform);
                if (!ContainsAny(text, includeNames) || ContainsAny(text, excludeNames))
                {
                    continue;
                }

                Vector2 point = collider.ClosestPoint(hero);
                AddNearest(ref nearest, collider.gameObject.name, hero, point);
            }

            return nearest;
        }

        public static BossNearestObject FindNearestPlatform(BossStateContext context, string[] includeNames, string[] excludeNames)
        {
            BossNearestObject nearest = EmptyNearest();
            if (context == null || context.Hero == null)
            {
                return nearest;
            }

            Vector2 hero = new Vector2(context.HeroPosition.x, context.HeroPosition.y);
            Collider2D[] colliders = UnityEngine.Object.FindObjectsOfType<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || collider.isTrigger || !collider.enabled || collider.gameObject == null || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (IsHeroObject(collider.gameObject, context.Hero) || HasHealthManager(collider.gameObject))
                {
                    continue;
                }

                string text = ObjectText(collider.transform);
                bool namedPlatform = ContainsAny(text, includeNames);
                bool likelyPlatform = IsLikelyHorizontalPlatform(collider.bounds, context.HeroPosition);
                if ((!namedPlatform && !likelyPlatform) || ContainsAny(text, excludeNames))
                {
                    continue;
                }

                Vector2 point = collider.ClosestPoint(hero);
                AddNearest(ref nearest, collider.gameObject.name, hero, point);
            }

            return nearest;
        }

        public static void WriteNearestFeatures(float[] features, int offset, BossNearestObject nearest)
        {
            if (features == null || features.Length < offset + 3)
            {
                return;
            }

            if (!nearest.Found)
            {
                features[offset] = 0f;
                features[offset + 1] = 0f;
                features[offset + 2] = 0f;
                return;
            }

            features[offset] = ScalePosition(nearest.Dx);
            features[offset + 1] = ScalePosition(nearest.Dy);
            features[offset + 2] = ScaleDistance(nearest.Distance);
        }

        public static float ScalePosition(float value)
        {
            return Mathf.Clamp(value * PositionScale, -10f, 10f);
        }

        public static float ScaleDistance(float value)
        {
            return Mathf.Clamp(value * PositionScale, 0f, 10f);
        }

        public static float ScaleCount(int value)
        {
            return Mathf.Clamp(value * CountScale, 0f, 10f);
        }

        public static bool TextContains(string text, string needle)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle))
            {
                return false;
            }

            return text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static BossNearestObject FindNearestNamedTransform(BossStateContext context, string[] includeNames, string[] excludeNames)
        {
            BossNearestObject nearest = EmptyNearest();
            if (context == null || context.Hero == null)
            {
                return nearest;
            }

            Vector2 hero = new Vector2(context.HeroPosition.x, context.HeroPosition.y);
            Transform[] transforms = UnityEngine.Object.FindObjectsOfType<Transform>();
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform transform = transforms[i];
                if (transform == null || transform.gameObject == null || !transform.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (IsHeroObject(transform.gameObject, context.Hero))
                {
                    continue;
                }

                string text = ObjectText(transform);
                if (!ContainsAny(text, includeNames) || ContainsAny(text, excludeNames))
                {
                    continue;
                }

                Vector2 point = new Vector2(transform.position.x, transform.position.y);
                AddNearest(ref nearest, transform.gameObject.name, hero, point);
            }

            return nearest;
        }

        private static BossNearestObject EmptyNearest()
        {
            return new BossNearestObject
            {
                Found = false,
                Name = string.Empty,
                Distance = float.MaxValue,
                Dx = 0f,
                Dy = 0f,
                Count = 0
            };
        }

        private static void AddNearest(ref BossNearestObject nearest, string name, Vector2 hero, Vector2 point)
        {
            float dx = point.x - hero.x;
            float dy = point.y - hero.y;
            float distance = Mathf.Sqrt(dx * dx + dy * dy);
            nearest.Count++;

            if (!nearest.Found || distance < nearest.Distance)
            {
                nearest.Found = true;
                nearest.Name = name;
                nearest.Distance = distance;
                nearest.Dx = dx;
                nearest.Dy = dy;
            }
        }

        private static bool ContainsAny(string text, string[] needles)
        {
            if (string.IsNullOrEmpty(text) || needles == null)
            {
                return false;
            }

            for (int i = 0; i < needles.Length; i++)
            {
                if (TextContains(text, needles[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ObjectText(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            Transform current = transform;
            int depth = 0;
            while (current != null && depth < 4)
            {
                if (builder.Length > 0)
                {
                    builder.Append('/');
                }

                builder.Append(current.gameObject.name);
                current = current.parent;
                depth++;
            }

            return builder.ToString();
        }

        private static bool IsHeroObject(GameObject obj, HeroController hero)
        {
            if (obj == null || hero == null)
            {
                return false;
            }

            Transform transform = obj.transform;
            return transform == hero.transform || transform.IsChildOf(hero.transform);
        }

        private static bool HasHealthManager(GameObject obj)
        {
            if (obj == null)
            {
                return false;
            }

            try
            {
                return obj.GetComponent<HealthManager>() != null || obj.GetComponentInParent<HealthManager>() != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLikelyHorizontalPlatform(Bounds bounds, Vector3 heroPosition)
        {
            if (bounds.size.x < 1f || bounds.size.y > 2.5f)
            {
                return false;
            }

            if (bounds.center.y > heroPosition.y + 8f || bounds.center.y < heroPosition.y - 24f)
            {
                return false;
            }

            return true;
        }
    }
}
