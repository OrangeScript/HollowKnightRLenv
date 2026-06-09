using System;
using System.Collections.Generic;
using System.Threading;
using Modding;
using UnityEngine;

namespace HollowKnightRLBridge
{
    internal class RLController : MonoBehaviour
    {
        private readonly object lockObj = new object();
        private readonly Queue<PendingCommand> commandQueue = new Queue<PendingCommand>();

        private StateReader stateReader;
        private ActionExecutor actionExecutor;
        private PendingCommand activeStep;
        private PendingCommand activeReset;
        private int activeFramesRemaining;
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
            TickController();
        }

        private void OnHeroUpdate()
        {
            TickController();
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

        private void TickController()
        {
            if (lastFrame == Time.frameCount)
            {
                return;
            }

            lastFrame = Time.frameCount;

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
            lastAction = command.ActionId;

            actionExecutor.BeginAction(command.ActionFrame);
            TickActiveStep();
        }

        private void TickActiveStep()
        {
            actionExecutor.Tick();
            activeFramesRemaining--;

            if (activeFramesRemaining > 0)
            {
                return;
            }

            PendingCommand finished = activeStep;
            activeStep = null;
            actionExecutor.Clear();

            ActionAvailability availability = actionExecutor.ReadAvailability();
            bool[] mask = RLActionSpace.BuildMask(availability);
            latestSnapshot = stateReader.ReadStepResult(availability, mask, lastAction);
            finished.Result = latestSnapshot;
            finished.Done.Set();

            if (IsHeroDead())
            {
                StartAutoDeathReset();
            }
        }

        private void CompleteReset(PendingCommand command)
        {
            activeStep = null;
            activeFramesRemaining = 0;
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
            if (sceneReadyFrames < 20)
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

        private void StartAutoDeathReset()
        {
            if (activeReset != null)
            {
                return;
            }

            GameManager gm = GameManager.instance;
            string scene = !string.IsNullOrEmpty(lastResetScene)
                ? lastResetScene
                : gm != null ? gm.sceneName : string.Empty;

            if (string.IsNullOrEmpty(scene))
            {
                return;
            }

            PendingCommand command = new PendingCommand
            {
                Type = RLCommandType.Reset,
                Refill = true,
                HardReset = true,
                TargetScene = scene,
                EntryGate = lastEntryGate,
                InfoNote = "auto_death_reset"
            };

            StartSceneReset(command);
        }

        private static bool IsHeroDead()
        {
            PlayerData pd = PlayerData.instance;
            HeroController hero = HeroController.instance;
            return (pd != null && pd.health <= 0) || (hero != null && hero.cState.dead);
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
