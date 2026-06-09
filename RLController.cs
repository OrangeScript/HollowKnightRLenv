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
        private int activeFramesRemaining;
        private int lastFrame = -1;
        private int lastAction;
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

        public RLStepResult ResetEpisode(bool refill, bool hardReset, int timeoutMs)
        {
            PendingCommand command = new PendingCommand
            {
                Type = RLCommandType.Reset,
                Refill = refill,
                HardReset = hardReset
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
        }

        private void CompleteReset(PendingCommand command)
        {
            activeStep = null;
            activeFramesRemaining = 0;
            lastAction = 0;
            actionExecutor.Clear();
            stateReader.ResetEpisode(command.Refill);

            latestSnapshot = MakeSnapshot();
            latestSnapshot.Info["hard_reset"] = command.HardReset;

            if (command.HardReset)
            {
                latestSnapshot.Info["hard_reset_note"] = "Current scene reload requested.";
                ReloadCurrentScene();
            }

            command.Result = latestSnapshot;
            command.Done.Set();
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

        private void ReloadCurrentScene()
        {
            try
            {
                GameManager gm = GameManager.instance;
                if (gm != null && !string.IsNullOrEmpty(gm.sceneName))
                {
                    gm.LoadScene(gm.sceneName);
                }
            }
            catch
            {
            }
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
        }

        private sealed class PendingCommand
        {
            public RLCommandType Type;
            public RLActionFrame ActionFrame;
            public int ActionId;
            public int Frames = 3;
            public bool Refill;
            public bool HardReset;
            public RLStepResult Result;
            public string Error;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }
    }
}
