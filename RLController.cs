using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Modding;
using UnityEngine;

namespace HollowKnightRLBridge
{
    internal class RLController : MonoBehaviour
    {
        private const int ResetWarmupFrames = 20;
        private const int ResetCombatReadyMaxFrames = 600;
        private const string DefaultGodhomeEntryGate = "door_dreamEnter";
        private const float HeroArenaMaxDistance = 80f;

        private readonly object lockObj = new object();
        private readonly Queue<PendingCommand> commandQueue = new Queue<PendingCommand>();

        private StateReader stateReader;
        private ActionExecutor actionExecutor;
        private PendingCommand activeStep;
        private PendingCommand activeReset;
        private int activeFramesRemaining;
        private bool activeStepReadyToComplete;
        private int activeStepReadyFrame = -1;
        private int sceneReadyFrames;
        private int lastFrame = -1;
        private int lastAction;
        private string lastResetScene;
        private string lastEntryGate;
        private RLStepResult latestSnapshot;

        private void Awake()
        {
            stateReader = new StateReader();
            actionExecutor = new ActionExecutor();
            stateReader.ResetEpisode(false);
            latestSnapshot = MakeSnapshot();
        }

        private void OnEnable()
        {
            ModHooks.HeroUpdateHook += OnHeroUpdate;
        }

        private void OnDisable()
        {
            ModHooks.HeroUpdateHook -= OnHeroUpdate;
            actionExecutor?.Clear();
            FailQueued("RLController disabled.");
        }

        private void Update()
        {
            TickController(false);
        }

        private void OnHeroUpdate()
        {
            TickController(true);
        }

        private void LateUpdate()
        {
            // Finish after the frame's hero update so one-frame inputs are not released before vanilla code reads them.
            if (activeStepReadyToComplete && activeStepReadyFrame == Time.frameCount)
            {
                FinishActiveStep();
            }
        }

        public RLStepResult Step(RLActionFrame actionFrame, int actionId, int frames, int timeoutMs)
        {
            PendingCommand command = new PendingCommand
            {
                Type = RLCommandType.Step,
                ActionFrame = actionFrame,
                ActionId = actionId,
                Frames = Mathf.Clamp(frames, 1, 60)
            };

            Enqueue(command);
            return WaitForCommand(command, timeoutMs);
        }

        public RLStepResult ResetEpisode(bool refill, bool hardReset, string targetScene, string entryGate, int timeoutMs)
        {
            PendingCommand command = new PendingCommand
            {
                Type = RLCommandType.Reset,
                Refill = refill,
                HardReset = hardReset,
                TargetScene = targetScene,
                EntryGate = entryGate
            };

            Enqueue(command);
            return WaitForCommand(command, timeoutMs);
        }

        public RLStepResult GetInfo(int timeoutMs)
        {
            PendingCommand command = new PendingCommand
            {
                Type = RLCommandType.Info
            };

            Enqueue(command);
            return WaitForCommand(command, timeoutMs);
        }

        private void TickController(bool allowStepTick)
        {
            if (lastFrame == Time.frameCount)
            {
                return;
            }

            if (!allowStepTick && HeroController.instance != null && activeStep != null)
            {
                return;
            }

            PendingCommand pending = Peek();
            if (!allowStepTick && HeroController.instance != null && activeStep == null && activeReset == null && pending != null && pending.Type == RLCommandType.Step)
            {
                return;
            }

            lastFrame = Time.frameCount;

            if (activeStepReadyToComplete && activeStepReadyFrame < Time.frameCount)
            {
                FinishActiveStep();
                return;
            }

            if (activeStep != null)
            {
                TickActiveStep();
                return;
            }

            if (activeReset != null)
            {
                TickActiveReset();
                return;
            }

            PendingCommand command = Dequeue();
            if (command == null)
            {
                actionExecutor.Tick();
                return;
            }

            try
            {
                switch (command.Type)
                {
                    case RLCommandType.Step:
                        StartStep(command);
                        break;
                    case RLCommandType.Reset:
                        CompleteReset(command);
                        break;
                    case RLCommandType.Info:
                        CompleteInfo(command);
                        break;
                    default:
                        command.Result = MakeSnapshot();
                        command.Done.Set();
                        break;
                }
            }
            catch (Exception e)
            {
                command.Error = e.ToString();
                command.Done.Set();
            }
        }

        private void StartStep(PendingCommand command)
        {
            activeStep = command;
            activeFramesRemaining = Mathf.Clamp(command.Frames, 1, 60);
            activeStepReadyToComplete = false;
            activeStepReadyFrame = -1;
            lastAction = command.ActionId;

            actionExecutor.BeginAction(command.ActionFrame);
            TickActiveStep();
        }

        private void TickActiveStep()
        {
            if (activeStepReadyToComplete)
            {
                return;
            }

            actionExecutor.Tick();
            activeFramesRemaining--;

            if (activeFramesRemaining > 0)
            {
                return;
            }

            activeStepReadyToComplete = true;
            activeStepReadyFrame = Time.frameCount;
        }

        private void FinishActiveStep()
        {
            if (activeStep == null)
            {
                activeStepReadyToComplete = false;
                activeStepReadyFrame = -1;
                return;
            }

            PendingCommand finished = activeStep;
            activeStep = null;
            activeFramesRemaining = 0;
            activeStepReadyToComplete = false;
            activeStepReadyFrame = -1;

            ActionAvailability availability = actionExecutor.ReadAvailability();
            bool[] mask = RLActionSpace.BuildMask(availability);
            latestSnapshot = stateReader.ReadStepResult(availability, mask, lastAction);
            actionExecutor.Clear();
            finished.Result = latestSnapshot;
            finished.Done.Set();
        }

        private void CompleteReset(PendingCommand command)
        {
            activeStep = null;
            activeFramesRemaining = 0;
            activeStepReadyToComplete = false;
            activeStepReadyFrame = -1;
            lastAction = 0;
            actionExecutor.Clear();

            if (ShouldLoadScene(command))
            {
                StartSceneReset(command);
                return;
            }

            FinishReset(command, "soft_reset");
        }

        private void CompleteInfo(PendingCommand command)
        {
            latestSnapshot = MakeSnapshot();
            command.Result = latestSnapshot;
            command.Done.Set();
        }

        private RLStepResult MakeSnapshot()
        {
            ActionAvailability availability = actionExecutor.ReadAvailability();
            bool[] mask = RLActionSpace.BuildMask(availability);
            return stateReader.ReadSnapshot(availability, mask, lastAction);
        }

        private void StartSceneReset(PendingCommand command)
        {
            activeReset = command;
            sceneReadyFrames = 0;

            try
            {
                GameManager gm = GameManager.instance;
                if (gm == null)
                {
                    throw new InvalidOperationException("GameManager is not ready.");
                }

                string scene = GetRequestedScene(command, gm);
                if (string.IsNullOrEmpty(scene))
                {
                    throw new InvalidOperationException("No target scene is available.");
                }

                command.TargetScene = scene;
                command.EntryGate = ResolveEntryGate(command.EntryGate, scene);
                lastResetScene = scene;
                lastEntryGate = command.EntryGate;
                command.InfoNote = command.HardReset ? "hard_reset" : "load_scene";

                if (!string.IsNullOrEmpty(command.EntryGate))
                {
                    gm.ChangeToScene(scene, command.EntryGate, 0f);
                }
                else
                {
                    gm.LoadScene(scene);
                }
            }
            catch (Exception e)
            {
                activeReset = null;
                command.Error = e.ToString();
                command.Done.Set();
            }
        }

        private void TickActiveReset()
        {
            PendingCommand command = activeReset;
            if (command == null)
            {
                return;
            }

            if (!IsSceneReady(command.TargetScene))
            {
                sceneReadyFrames = 0;
                return;
            }

            sceneReadyFrames++;
            if (sceneReadyFrames < ResetWarmupFrames)
            {
                return;
            }

            if (command.Refill || IsHeroDead())
            {
                stateReader.ReviveHeroForReset();
            }

            EnsureBossArenaPlacement();

            bool isTransitioning = IsBossSceneTransitioning();
            if (isTransitioning && sceneReadyFrames < ResetWarmupFrames + ResetCombatReadyMaxFrames)
            {
                return;
            }

            if ((!IsCombatReady() || !HasLiveEnemy() || !IsHeroNearBossArena()) && sceneReadyFrames < ResetWarmupFrames + ResetCombatReadyMaxFrames)
            {
                return;
            }

            activeReset = null;
            FinishReset(command, command.InfoNote ?? "load_scene");
        }

        private void FinishReset(PendingCommand command, string resetMode)
        {
            actionExecutor.Clear();
            stateReader.ResetEpisode(command.Refill);

            latestSnapshot = MakeSnapshot();
            latestSnapshot.Info["reset_mode"] = resetMode;
            latestSnapshot.Info["hard_reset"] = command.HardReset;
            latestSnapshot.Info["target_scene"] = command.TargetScene ?? string.Empty;
            latestSnapshot.Info["entry_gate"] = command.EntryGate ?? string.Empty;
            latestSnapshot.Info["reset_wait_frames"] = sceneReadyFrames;
            latestSnapshot.Info["reset_combat_ready"] = IsCombatReady();
            latestSnapshot.Info["reset_enemy_ready"] = HasLiveEnemy();
            latestSnapshot.Info["reset_hero_arena_ready"] = IsHeroNearBossArena();

            command.Result = latestSnapshot;
            command.Done.Set();
        }

        private static bool ShouldLoadScene(PendingCommand command)
        {
            if (command == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(command.TargetScene))
            {
                return true;
            }

            return command.HardReset || IsHeroDead();
        }

        private static string GetRequestedScene(PendingCommand command, GameManager gm)
        {
            if (!string.IsNullOrEmpty(command.TargetScene))
            {
                return command.TargetScene;
            }

            return gm != null ? gm.sceneName : string.Empty;
        }

        private static string ResolveEntryGate(string entryGate, string scene)
        {
            if (!string.IsNullOrEmpty(entryGate))
            {
                return entryGate;
            }

            return IsGodhomeBossScene(scene) ? DefaultGodhomeEntryGate : entryGate;
        }

        private static bool IsGodhomeBossScene(string scene)
        {
            return !string.IsNullOrEmpty(scene) && scene.StartsWith("GG_", StringComparison.Ordinal);
        }

        private static void EnsureBossArenaPlacement()
        {
            GameManager gm = GameManager.instance;
            HeroController hero = HeroController.instance;
            if (gm == null || hero == null || !IsGodhomeBossScene(gm.sceneName))
            {
                return;
            }

            Transform spawn = FindBossHeroSpawn();
            if (spawn == null)
            {
                return;
            }

            Vector3 heroPos = hero.transform.position;
            Vector3 spawnPos = spawn.position;
            bool shouldMove = IsHeroOffMap(heroPos) || Vector2.Distance(new Vector2(heroPos.x, heroPos.y), new Vector2(spawnPos.x, spawnPos.y)) > HeroArenaMaxDistance;
            if (!shouldMove)
            {
                return;
            }

            try
            {
                hero.transform.position = spawnPos;
                Rigidbody2D rb = hero.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.velocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                }

                InvokeHeroVoid(hero, "ResetMotion");
                hero.ResetAirMoves();
                hero.RegainControl();
                hero.AcceptInput();
                PositionCameraToHero();
            }
            catch
            {
            }
        }

        private static bool IsHeroNearBossArena()
        {
            GameManager gm = GameManager.instance;
            HeroController hero = HeroController.instance;
            if (gm == null || !IsGodhomeBossScene(gm.sceneName))
            {
                return true;
            }

            if (hero == null)
            {
                return false;
            }

            Transform spawn = FindBossHeroSpawn();
            if (spawn == null)
            {
                return true;
            }

            Vector3 heroPos = hero.transform.position;
            Vector3 spawnPos = spawn.position;
            return !IsHeroOffMap(heroPos) && Vector2.Distance(new Vector2(heroPos.x, heroPos.y), new Vector2(spawnPos.x, spawnPos.y)) <= HeroArenaMaxDistance;
        }

        private static Transform FindBossHeroSpawn()
        {
            try
            {
                BossSceneController controller = BossSceneController.Instance;
                if (controller == null)
                {
                    controller = UnityEngine.Object.FindObjectOfType<BossSceneController>();
                }

                if (controller != null && controller.heroSpawn != null)
                {
                    return controller.heroSpawn;
                }
            }
            catch
            {
            }

            string[] names = { "Hero Spawn", "heroSpawn", "HeroSpawn" };
            foreach (string name in names)
            {
                try
                {
                    GameObject obj = GameObject.Find(name);
                    if (obj != null)
                    {
                        return obj.transform;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool IsHeroOffMap(Vector3 heroPos)
        {
            return Mathf.Abs(heroPos.x) > 5000f || Mathf.Abs(heroPos.y) > 5000f;
        }

        private static void InvokeHeroVoid(HeroController hero, string methodName)
        {
            if (hero == null)
            {
                return;
            }

            try
            {
                MethodInfo method = typeof(HeroController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null && method.ReturnType == typeof(void) && method.GetParameters().Length == 0)
                {
                    method.Invoke(hero, null);
                }
            }
            catch
            {
            }
        }

        private static void PositionCameraToHero()
        {
            try
            {
                GameManager gm = GameManager.instance;
                if (gm != null && gm.cameraCtrl != null)
                {
                    gm.cameraCtrl.PositionToHero(true);
                }
            }
            catch
            {
            }
        }

        private static bool IsHeroDead()
        {
            PlayerData pd = PlayerData.instance;
            HeroController hero = HeroController.instance;
            return (pd != null && pd.health <= 0) || (hero != null && hero.cState.dead);
        }

        private static bool IsCombatReady()
        {
            HeroController hero = HeroController.instance;
            return hero != null && SafeHeroBool(hero.CanInput) && InvokeHeroBool(hero, "CanTakeDamage") && !IsBossSceneTransitioning();
        }

        private static bool SafeHeroBool(Func<bool> func)
        {
            try
            {
                return func();
            }
            catch
            {
                return false;
            }
        }

        private static bool InvokeHeroBool(HeroController hero, string methodName)
        {
            if (hero == null)
            {
                return false;
            }

            try
            {
                MethodInfo method = typeof(HeroController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return method != null && method.ReturnType == typeof(bool) && method.GetParameters().Length == 0 && (bool)method.Invoke(hero, null);
            }
            catch
            {
                return false;
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

        private static bool HasLiveEnemy()
        {
            try
            {
                HealthManager[] managers = UnityEngine.Object.FindObjectsOfType<HealthManager>();
                foreach (HealthManager manager in managers)
                {
                    if (manager == null || manager.gameObject == null || !manager.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    if (manager.hp <= 0 || manager.isDead)
                    {
                        continue;
                    }

                    try
                    {
                        if (manager.GetIsDead())
                        {
                            continue;
                        }
                    }
                    catch
                    {
                    }

                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsSceneReady(string targetScene)
        {
            GameManager gm = GameManager.instance;
            if (gm == null || HeroController.instance == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(targetScene) && gm.sceneName != targetScene)
            {
                return false;
            }

            try
            {
                if (gm.IsInSceneTransition || gm.IsLoadingSceneTransition)
                {
                    return false;
                }
            }
            catch
            {
            }

            return true;
        }

        private void Enqueue(PendingCommand command)
        {
            lock (lockObj)
            {
                commandQueue.Enqueue(command);
            }
        }

        private PendingCommand Dequeue()
        {
            lock (lockObj)
            {
                if (commandQueue.Count == 0)
                {
                    return null;
                }

                return commandQueue.Dequeue();
            }
        }

        private PendingCommand Peek()
        {
            lock (lockObj)
            {
                if (commandQueue.Count == 0)
                {
                    return null;
                }

                return commandQueue.Peek();
            }
        }

        private RLStepResult WaitForCommand(PendingCommand command, int timeoutMs)
        {
            timeoutMs = Mathf.Clamp(timeoutMs, 100, 60000);
            if (!command.Done.Wait(timeoutMs))
            {
                throw new TimeoutException("Timed out waiting for Unity main thread.");
            }

            if (!string.IsNullOrEmpty(command.Error))
            {
                throw new InvalidOperationException(command.Error);
            }

            return command.Result ?? latestSnapshot ?? MakeSnapshot();
        }

        private void FailQueued(string error)
        {
            lock (lockObj)
            {
                while (commandQueue.Count > 0)
                {
                    PendingCommand command = commandQueue.Dequeue();
                    command.Error = error;
                    command.Done.Set();
                }
            }

            if (activeStep != null)
            {
                activeStep.Error = error;
                activeStep.Done.Set();
                activeStep = null;
                activeStepReadyToComplete = false;
                activeStepReadyFrame = -1;
            }

            if (activeReset != null)
            {
                activeReset.Error = error;
                activeReset.Done.Set();
                activeReset = null;
            }
        }

        private sealed class PendingCommand
        {
            public RLCommandType Type;
            public RLActionFrame ActionFrame;
            public int ActionId;
            public int Frames = 1;
            public bool Refill;
            public bool HardReset;
            public string TargetScene;
            public string EntryGate;
            public string InfoNote;
            public RLStepResult Result;
            public string Error;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }
    }
}
