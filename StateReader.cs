using System;
using System.Collections.Generic;
using UnityEngine;

namespace HollowKnightRLBridge
{
    internal class StateReader
    {
        public const int FeatureCount = 48;
        public const int ObservationSize = FeatureCount + RLActionSpace.ActionCount;

        private const float PositionScale = 0.05f;
        private const float VelocityScale = 0.1f;
        private const float TimePenalty = -0.01f;
        private const float BossDamageReward = 0.5f;
        private const float HeroDamagePenalty = 10f;
        private const float HeroHealReward = 0.25f;
        private const float WinReward = 100f;
        private const float DeathPenalty = 100f;

        private readonly Dictionary<int, int> previousEnemyHp = new Dictionary<int, int>();

        private int previousHeroHealth = -1;
        private int initialEnemyTotalHp;
        private int lastTargetId;
        private int episodeSteps;

        public int EpisodeSteps => episodeSteps;

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
            return BuildResult(0f, done.Done, false, availability, actionMask, lastAction, enemies, 0, 0);
        }

        public RLStepResult ReadStepResult(ActionAvailability availability, bool[] actionMask, int lastAction)
        {
            episodeSteps++;

            EnemyReadout enemies = ReadEnemies();
            int heroDelta = CalculateHeroDelta();
            int bossDamage = CalculateEnemyDamage(enemies);
            DoneReadout done = ReadDone(enemies);

            float reward = TimePenalty;
            if (bossDamage > 0)
            {
                reward += bossDamage * BossDamageReward;
            }

            if (heroDelta < 0)
            {
                reward += heroDelta * HeroDamagePenalty;
            }
            else if (heroDelta > 0)
            {
                reward += heroDelta * HeroHealReward;
            }

            if (done.BossDead)
            {
                reward += WinReward;
            }

            if (done.HeroDead)
            {
                reward -= DeathPenalty;
            }

            return BuildResult(reward, done.Done, false, availability, actionMask, lastAction, enemies, bossDamage, heroDelta);
        }

        private RLStepResult BuildResult(
            float reward,
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
                Reward = reward,
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
                ["boss_count"] = enemies.Count,
                ["boss_total_hp"] = enemies.TotalHp,
                ["boss_initial_total_hp"] = initialEnemyTotalHp,
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

                if (hero != null)
                {
                    hero.MaxHealth();
                    hero.SetMPCharge(pd.MPCharge);
                    hero.ResetAirMoves();
                }
            }
            catch
            {
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
