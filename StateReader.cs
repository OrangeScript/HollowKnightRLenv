using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HollowKnightRLBridge
{
    internal class StateReader
    {
        public const int FeatureCount = 48;
        public const int ObservationSize = FeatureCount + RLActionSpace.ActionCount;

        private const float PositionScale = 0.05f;
        private const float VelocityScale = 0.1f;

        private readonly Dictionary<int, int> previousEnemyHp = new Dictionary<int, int>();

        private int previousHeroHealth = -1;
        private int initialEnemyTotalHp;
        private int lastTargetId;
        private int episodeSteps;

        public int EpisodeSteps => episodeSteps;

        public void ReviveHeroForReset()
        {
            RefillHero();
        }

        public void ResetEpisode(bool refillHero)
        {
            if (refillHero)
            {
                RefillHero();
            }

            previousEnemyHp.Clear();
            previousHeroHealth = ReadHeroHealth();
            initialEnemyTotalHp = 0;
            lastTargetId = 0;
            episodeSteps = 0;

            EnemyReadout readout = ReadEnemies();
            PrimeEnemyBaselines(readout);
        }

        public RLStepResult ReadSnapshot(ActionAvailability availability, bool[] actionMask, int lastAction)
        {
            EnemyReadout enemies = ReadEnemies();
            DoneReadout done = ReadDone(enemies);
            return BuildResult(done.Done, false, availability, actionMask, lastAction, enemies, 0, 0);
        }

        public RLStepResult ReadStepResult(ActionAvailability availability, bool[] actionMask, int lastAction)
        {
            episodeSteps++;

            EnemyReadout enemies = ReadEnemies();
            int heroDelta = CalculateHeroDelta();
            int bossDamage = CalculateEnemyDamage(enemies);
            DoneReadout done = ReadDone(enemies);

            return BuildResult(done.Done, false, availability, actionMask, lastAction, enemies, bossDamage, heroDelta);
        }

        private RLStepResult BuildResult(
            bool done,
            bool truncated,
            ActionAvailability availability,
            bool[] actionMask,
            int lastAction,
            EnemyReadout enemies,
            int bossDamage,
            int heroDelta)
        {
            float[] observation = BuildObservation(availability, actionMask, lastAction, enemies, done, bossDamage, heroDelta);
            Dictionary<string, object> info = BuildInfo(availability, actionMask, lastAction, enemies, bossDamage, heroDelta);

            return new RLStepResult
            {
                Observation = observation,
                Done = done,
                Truncated = truncated,
                Info = info
            };
        }

        private float[] BuildObservation(
            ActionAvailability availability,
            bool[] actionMask,
            int lastAction,
            EnemyReadout enemies,
            bool done,
            int bossDamage,
            int heroDelta)
        {
            float[] obs = new float[ObservationSize];
            HeroController hero = HeroController.instance;
            PlayerData pd = PlayerData.instance;

            Vector3 heroPos = Vector3.zero;
            Vector2 heroVelocity = Vector2.zero;

            if (hero != null)
            {
                heroPos = hero.transform.position;
                Rigidbody2D rb = hero.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    heroVelocity = rb.velocity;
                }
            }

            int health = pd != null ? pd.health : 0;
            int maxHealth = pd != null ? Math.Max(1, pd.maxHealth) : 1;
            int soul = pd != null ? pd.MPCharge : 0;
            int maxSoul = pd != null ? Math.Max(1, pd.maxMP) : 99;

            obs[0] = Scale(heroPos.x, PositionScale);
            obs[1] = Scale(heroPos.y, PositionScale);
            obs[2] = Scale(heroVelocity.x, VelocityScale);
            obs[3] = Scale(heroVelocity.y, VelocityScale);
            obs[4] = Ratio(health, maxHealth);
            obs[5] = health;
            obs[6] = Ratio(soul, maxSoul);
            obs[7] = soul * 0.01f;

            if (hero != null)
            {
                obs[8] = Bool(hero.cState.facingRight);
                obs[9] = Bool(hero.cState.onGround);
                obs[10] = Bool(hero.cState.falling);
                obs[11] = Bool(hero.cState.jumping);
                obs[12] = Bool(hero.cState.doubleJumping);
                obs[13] = Bool(hero.cState.dashing || hero.cState.shadowDashing);
                obs[14] = Bool(hero.cState.dashCooldown);
                obs[15] = Bool(hero.cState.attacking);
                obs[16] = Bool(hero.cState.casting);
                obs[17] = Bool(hero.cState.focusing);
                obs[18] = Bool(hero.cState.invulnerable);
                obs[19] = Bool(hero.cState.lookingUp);
                obs[20] = Bool(hero.cState.lookingDown);
                obs[21] = Bool(hero.cState.touchingWall);
                obs[22] = Bool(hero.cState.wallSliding);
            }

            obs[23] = Bool(availability.CanInput);
            obs[24] = Bool(availability.CanJump);
            obs[25] = Bool(availability.CanDash);
            obs[26] = Bool(availability.CanAttack);
            obs[27] = Bool(availability.CanDownAttack);
            obs[28] = Bool(availability.CanCast);
            obs[29] = Bool(availability.CanFocus);
            obs[30] = Bool(enemies.TargetFound);
            obs[31] = Scale(enemies.TargetDx, PositionScale);
            obs[32] = Scale(enemies.TargetDy, PositionScale);
            obs[33] = Scale(enemies.TargetVx, VelocityScale);
            obs[34] = Scale(enemies.TargetVy, VelocityScale);
            obs[35] = initialEnemyTotalHp > 0 ? Ratio(enemies.TotalHp, initialEnemyTotalHp) : 0f;
            obs[36] = enemies.TotalHp * 0.001f;
            obs[37] = enemies.TargetFound ? enemies.TargetHp * 0.001f : 0f;
            obs[38] = Scale(enemies.TargetDistance, PositionScale);
            obs[39] = enemies.Count * 0.1f;
            obs[40] = Time.timeSinceLevelLoad * 0.01f;
            obs[41] = lastAction >= 0 ? Ratio(lastAction, Math.Max(1, RLActionSpace.ActionCount - 1)) : -1f;
            obs[42] = episodeSteps * 0.001f;
            obs[43] = bossDamage * 0.01f;
            obs[44] = heroDelta * 0.1f;
            obs[45] = Bool(done);
            obs[46] = Bool(pd != null && pd.health <= 0);
            obs[47] = Bool(enemies.WasTracked && enemies.Count == 0);

            for (int i = 0; i < RLActionSpace.ActionCount; i++)
            {
                obs[FeatureCount + i] = actionMask != null && i < actionMask.Length && actionMask[i] ? 1f : 0f;
            }

            return obs;
        }

        private Dictionary<string, object> BuildInfo(
            ActionAvailability availability,
            bool[] actionMask,
            int lastAction,
            EnemyReadout enemies,
            int bossDamage,
            int heroDelta)
        {
            PlayerData pd = PlayerData.instance;
            GameManager gm = GameManager.instance;
            HeroController hero = HeroController.instance;
            bool heroDead = pd != null && pd.health <= 0;
            bool bossDead = enemies.WasTracked && enemies.Count == 0;
            Dictionary<string, object> info = new Dictionary<string, object>
            {
                ["scene"] = gm != null ? gm.sceneName : string.Empty,
                ["episode_steps"] = episodeSteps,
                ["observation_size"] = ObservationSize,
                ["feature_count"] = FeatureCount,
                ["action_count"] = RLActionSpace.ActionCount,
                ["action_names"] = RLActionSpace.Names,
                ["action_mask"] = actionMask,
                ["last_action"] = lastAction,
                ["can_input"] = availability.CanInput,
                ["can_jump"] = availability.CanJump,
                ["can_dash"] = availability.CanDash,
                ["can_attack"] = availability.CanAttack,
                ["can_down_attack"] = availability.CanDownAttack,
                ["can_cast"] = availability.CanCast,
                ["can_focus"] = availability.CanFocus,
                ["hero_health"] = pd != null ? pd.health : 0,
                ["hero_max_health"] = pd != null ? pd.maxHealth : 0,
                ["hero_soul"] = pd != null ? pd.MPCharge : 0,
                ["hero_dead"] = heroDead,
                ["hero_cstate_dead"] = hero != null && hero.cState.dead,
                ["hero_cstate_recoiling"] = hero != null && hero.cState.recoiling,
                ["hero_cstate_hazard_death"] = hero != null && hero.cState.hazardDeath,
                ["hero_invulnerable"] = hero != null && hero.cState.invulnerable,
                ["hero_accepting_input"] = ReadBoolField(hero, "acceptingInput"),
                ["hero_control_relinquished"] = ReadBoolField(hero, "controlReqlinquished"),
                ["hero_enter_without_input"] = ReadBoolField(hero, "enterWithoutInput"),
                ["hero_transition_state"] = ReadFieldString(hero, "transitionState"),
                ["hero_take_no_damage"] = ReadBoolField(hero, "takeNoDamage"),
                ["hero_damage_mode"] = ReadFieldString(hero, "damageMode"),
                ["hero_can_take_damage"] = InvokeBoolIfExists(hero, "CanTakeDamage"),
                ["hero_pd_is_invincible"] = ReadPlayerDataBool(pd, "isInvincible"),
                ["boss_scene_transitioning"] = IsBossSceneTransitioning(),
                ["boss_count"] = enemies.Count,
                ["boss_total_hp"] = enemies.TotalHp,
                ["boss_initial_total_hp"] = initialEnemyTotalHp,
                ["boss_dead"] = bossDead,
                ["target_id"] = enemies.TargetId,
                ["target_name"] = enemies.TargetName ?? string.Empty,
                ["target_hp"] = enemies.TargetHp,
                ["boss_damage"] = bossDamage,
                ["hero_delta"] = heroDelta
            };

            return info;
        }

        private EnemyReadout ReadEnemies()
        {
            HeroController hero = HeroController.instance;
            Vector3 heroPos = hero != null ? hero.transform.position : Vector3.zero;
            HealthManager[] managers = UnityEngine.Object.FindObjectsOfType<HealthManager>();

            EnemyReadout readout = new EnemyReadout
            {
                TargetDistance = float.MaxValue,
                WasTracked = previousEnemyHp.Count > 0
            };

            foreach (HealthManager manager in managers)
            {
                if (!IsCandidateEnemy(manager))
                {
                    continue;
                }

                int id = manager.GetInstanceID();
                int hp = Math.Max(0, manager.hp);
                Vector3 pos = manager.transform.position;
                float distance = Vector2.Distance(heroPos, pos);

                readout.Count++;
                readout.TotalHp += hp;

                if (distance < readout.TargetDistance)
                {
                    Rigidbody2D rb = manager.GetComponent<Rigidbody2D>();
                    Vector2 velocity = rb != null ? rb.velocity : Vector2.zero;

                    readout.TargetFound = true;
                    readout.TargetId = id;
                    readout.TargetName = manager.gameObject.name;
                    readout.TargetHp = hp;
                    readout.TargetDx = pos.x - heroPos.x;
                    readout.TargetDy = pos.y - heroPos.y;
                    readout.TargetVx = velocity.x;
                    readout.TargetVy = velocity.y;
                    readout.TargetDistance = distance;
                }
            }

            if (readout.TargetFound)
            {
                lastTargetId = readout.TargetId;
            }

            if (initialEnemyTotalHp <= 0 && readout.TotalHp > 0)
            {
                initialEnemyTotalHp = readout.TotalHp;
            }

            return readout;
        }

        private int CalculateHeroDelta()
        {
            int current = ReadHeroHealth();
            if (previousHeroHealth < 0)
            {
                previousHeroHealth = current;
                return 0;
            }

            int delta = current - previousHeroHealth;
            previousHeroHealth = current;
            return delta;
        }

        private int CalculateEnemyDamage(EnemyReadout readout)
        {
            Dictionary<int, int> current = new Dictionary<int, int>();
            HealthManager[] managers = UnityEngine.Object.FindObjectsOfType<HealthManager>();

            foreach (HealthManager manager in managers)
            {
                if (!IsCandidateEnemy(manager))
                {
                    continue;
                }

                current[manager.GetInstanceID()] = Math.Max(0, manager.hp);
            }

            int damage = 0;
            foreach (KeyValuePair<int, int> pair in previousEnemyHp)
            {
                if (current.TryGetValue(pair.Key, out int hp))
                {
                    if (pair.Value > hp)
                    {
                        damage += pair.Value - hp;
                    }
                }
                else if (pair.Value > 0)
                {
                    damage += pair.Value;
                }
            }

            previousEnemyHp.Clear();
            foreach (KeyValuePair<int, int> pair in current)
            {
                previousEnemyHp[pair.Key] = pair.Value;
            }

            if (initialEnemyTotalHp <= 0 && readout.TotalHp > 0)
            {
                initialEnemyTotalHp = readout.TotalHp;
            }

            return damage;
        }

        private DoneReadout ReadDone(EnemyReadout enemies)
        {
            PlayerData pd = PlayerData.instance;
            bool heroDead = pd != null && pd.health <= 0;
            bool bossDead = enemies.WasTracked && enemies.Count == 0;
            return new DoneReadout
            {
                HeroDead = heroDead,
                BossDead = bossDead,
                Done = heroDead || bossDead
            };
        }

        private void PrimeEnemyBaselines(EnemyReadout readout)
        {
            HealthManager[] managers = UnityEngine.Object.FindObjectsOfType<HealthManager>();
            foreach (HealthManager manager in managers)
            {
                if (!IsCandidateEnemy(manager))
                {
                    continue;
                }

                previousEnemyHp[manager.GetInstanceID()] = Math.Max(0, manager.hp);
            }

            initialEnemyTotalHp = readout.TotalHp;
        }

        private static bool IsCandidateEnemy(HealthManager manager)
        {
            if (manager == null || manager.gameObject == null || !manager.gameObject.activeInHierarchy)
            {
                return false;
            }

            if (manager.hp <= 0 || manager.isDead)
            {
                return false;
            }

            try
            {
                if (manager.GetIsDead())
                {
                    return false;
                }
            }
            catch
            {
            }

            return true;
        }

        private static int ReadHeroHealth()
        {
            PlayerData pd = PlayerData.instance;
            return pd != null ? pd.health : 0;
        }

        private static void RefillHero()
        {
            PlayerData pd = PlayerData.instance;
            HeroController hero = HeroController.instance;
            if (pd == null)
            {
                return;
            }

            try
            {
                pd.health = Math.Max(pd.health, pd.maxHealth);
                pd.MPCharge = Math.Max(pd.MPCharge, pd.maxMP > 0 ? pd.maxMP : 99);
                pd.MPReserve = Math.Max(pd.MPReserve, pd.MPReserveMax);
                pd.healthBlue = 0;
                InvokeStringBoolIfExists(pd, "SetBool", "isInvincible", false);

                if (hero != null)
                {
                    hero.MaxHealth();
                    hero.SetMPCharge(pd.MPCharge);
                    hero.ResetAirMoves();
                    RestoreHeroDamageState(hero);
                }
            }
            catch
            {
            }
        }

        private static void RestoreHeroDamageState(HeroController hero)
        {
            if (hero == null)
            {
                return;
            }

            try
            {
                hero.cState.dead = false;
                hero.cState.invulnerable = false;
                hero.cState.recoiling = false;
                hero.cState.hazardDeath = false;
                hero.cState.hazardRespawning = false;
            }
            catch
            {
            }

            InvokeIfExists(hero, "EndTakeNoDamage");
            InvokeIfExists(hero, "CancelParryInvuln");
            InvokeIfExists(hero, "stopInvulnerablePulse");
            InvokeIfExists(hero, "RegainControl");
            InvokeIfExists(hero, "AcceptInput");
            InvokeBoolArgIfExists(hero, "EnterWithoutInput", false);
            InvokeBoolArgIfExists(hero, "SetTakeNoDamage", false);
            SetFieldIfExists(hero, "acceptingInput", true);
            SetFieldIfExists(hero, "controlReqlinquished", false);
            SetFieldIfExists(hero, "enterWithoutInput", false);
            SetFieldIfExists(hero, "takeNoDamage", false);
            SetFieldIfExists(hero, "damagedThisFrame", false);
            SetFieldIfExists(hero, "parryInvulnTimer", 0f);
            SetFieldIfExists(hero, "invulnerableTime", 0f);
            SetEnumOrIntFieldIfExists(hero, "transitionState", "WAITING_TO_TRANSITION", 0);
            SetDamageModeFull(hero);
        }

        private static void SetDamageModeFull(HeroController hero)
        {
            FieldInfo field = FindField(hero, "damageMode");
            if (field != null)
            {
                SetMemberValue(hero, field.FieldType, value => field.SetValue(hero, value), "FULL_DAMAGE", 0);
            }

            MethodInfo method = FindMethod(hero, "SetDamageMode");
            if (method == null)
            {
                return;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 1)
            {
                return;
            }

            SetMemberValue(hero, parameters[0].ParameterType, value => method.Invoke(hero, new[] { value }), "FULL_DAMAGE", 0);
        }

        private static void SetFieldIfExists(object target, string fieldName, object value)
        {
            FieldInfo field = FindField(target, fieldName);
            if (field == null)
            {
                return;
            }

            try
            {
                field.SetValue(target, value);
            }
            catch
            {
            }
        }

        private static void InvokeIfExists(object target, string methodName)
        {
            MethodInfo method = FindMethod(target, methodName);
            if (method == null || method.GetParameters().Length != 0)
            {
                return;
            }

            try
            {
                method.Invoke(target, null);
            }
            catch
            {
            }
        }

        private static void InvokeBoolArgIfExists(object target, string methodName, bool value)
        {
            MethodInfo method = FindMethod(target, methodName);
            if (method == null)
            {
                return;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(bool))
            {
                return;
            }

            try
            {
                method.Invoke(target, new object[] { value });
            }
            catch
            {
            }
        }

        private static void InvokeStringBoolIfExists(object target, string methodName, string key, bool value)
        {
            MethodInfo method = FindMethod(target, methodName);
            if (method == null)
            {
                return;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 2 || parameters[0].ParameterType != typeof(string) || parameters[1].ParameterType != typeof(bool))
            {
                return;
            }

            try
            {
                method.Invoke(target, new object[] { key, value });
            }
            catch
            {
            }
        }

        private static void SetEnumOrIntFieldIfExists(object target, string fieldName, string enumName, int intValue)
        {
            FieldInfo field = FindField(target, fieldName);
            if (field == null)
            {
                return;
            }

            SetMemberValue(target, field.FieldType, value => field.SetValue(target, value), enumName, intValue);
        }

        private static FieldInfo FindField(object target, string fieldName)
        {
            if (target == null)
            {
                return null;
            }

            return target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static MethodInfo FindMethod(object target, string methodName)
        {
            if (target == null)
            {
                return null;
            }

            return target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static void SetMemberValue(object target, Type valueType, Action<object> setter, string enumName, int intValue)
        {
            try
            {
                if (valueType.IsEnum)
                {
                    setter(Enum.Parse(valueType, enumName));
                }
                else if (valueType == typeof(int))
                {
                    setter(intValue);
                }
            }
            catch
            {
            }
        }

        private static bool? ReadBoolField(object target, string fieldName)
        {
            FieldInfo field = FindField(target, fieldName);
            if (field == null || field.FieldType != typeof(bool))
            {
                return null;
            }

            try
            {
                return (bool)field.GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        private static string ReadFieldString(object target, string fieldName)
        {
            FieldInfo field = FindField(target, fieldName);
            if (field == null)
            {
                return string.Empty;
            }

            try
            {
                object value = field.GetValue(target);
                return value != null ? value.ToString() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool? InvokeBoolIfExists(object target, string methodName)
        {
            MethodInfo method = FindMethod(target, methodName);
            if (method == null || method.ReturnType != typeof(bool) || method.GetParameters().Length != 0)
            {
                return null;
            }

            try
            {
                return (bool)method.Invoke(target, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool? ReadPlayerDataBool(PlayerData pd, string key)
        {
            if (pd == null)
            {
                return null;
            }

            try
            {
                return pd.GetBool(key);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsBossSceneTransitioning()
        {
            try
            {
                return BossSceneController.IsTransitioning;
            }
            catch
            {
                return false;
            }
        }

        private static float Bool(bool value)
        {
            return value ? 1f : 0f;
        }

        private static float Ratio(float value, float max)
        {
            if (Math.Abs(max) < 0.0001f)
            {
                return 0f;
            }

            return value / max;
        }

        private static float Scale(float value, float scale)
        {
            float scaled = value * scale;
            if (scaled > 1000f)
            {
                return 1000f;
            }

            if (scaled < -1000f)
            {
                return -1000f;
            }

            return scaled;
        }

        private struct EnemyReadout
        {
            public int Count;
            public int TotalHp;
            public bool WasTracked;
            public bool TargetFound;
            public int TargetId;
            public string TargetName;
            public int TargetHp;
            public float TargetDx;
            public float TargetDy;
            public float TargetVx;
            public float TargetVy;
            public float TargetDistance;
        }

        private struct DoneReadout
        {
            public bool HeroDead;
            public bool BossDead;
            public bool Done;
        }
    }
}
