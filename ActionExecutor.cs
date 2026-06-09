using System;
using System.Reflection;
using InControl;
using UnityEngine;

namespace HollowKnightRLBridge
{
    internal class ActionExecutor
    {
        private readonly MethodInfo canJump;
        private readonly MethodInfo canDash;
        private readonly MethodInfo canAttack;
        private readonly MethodInfo updateWithAxes;

        private RLActionFrame currentFrame;
        private bool holdingInput;
        private bool hasInjectedInput;

        public ActionExecutor()
        {
            Type heroType = typeof(HeroController);
            const BindingFlags heroFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            const BindingFlags axisFlags = BindingFlags.Instance | BindingFlags.NonPublic;

            canJump = heroType.GetMethod("CanJump", heroFlags);
            canDash = heroType.GetMethod("CanDash", heroFlags);
            canAttack = heroType.GetMethod("CanAttack", heroFlags);
            updateWithAxes = typeof(TwoAxisInputControl).GetMethod("UpdateWithAxes", axisFlags);
        }

        public void BeginAction(RLActionFrame frame)
        {
            currentFrame = frame;
            holdingInput = true;
        }

        public void Tick()
        {
            if (holdingInput)
            {
                ApplyInput(currentFrame);
            }
        }

        public void Clear()
        {
            holdingInput = false;
            currentFrame = new RLActionFrame();

            if (hasInjectedInput)
            {
                ReleaseInput();
            }
        }

        public ActionAvailability ReadAvailability()
        {
            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                return new ActionAvailability();
            }

            bool canInput = SafeCanInput(hero);
            bool canAttackNow = canInput && InvokeBool(hero, canAttack);
            return new ActionAvailability
            {
                CanInput = canInput,
                CanMove = canInput,
                CanJump = canInput && InvokeBool(hero, canJump),
                CanDash = canInput && InvokeBool(hero, canDash),
                CanAttack = canAttackNow,
                CanDownAttack = canAttackNow && !hero.cState.onGround,
                CanCast = canInput && SafePublicBool(hero.CanCast),
                CanFocus = canInput && SafePublicBool(hero.CanFocus)
            };
        }

        private void ApplyInput(RLActionFrame frame)
        {
            hasInjectedInput = true;

            InputHandler input = InputHandler.Instance;
            if (input == null || input.inputActions == null)
            {
                return;
            }

            HeroController hero = HeroController.instance;
            bool attackPressed = frame.Attack && (hero == null || IsAttackDirectionLegal(hero, frame));
            HeroActions actions = input.inputActions;
            ulong tick = CurrentTick();
            float delta = CurrentDelta();

            input.inputX = frame.Horizontal;
            input.inputY = frame.Vertical;

            Commit(actions.left, frame.Horizontal < 0, tick, delta);
            Commit(actions.right, frame.Horizontal > 0, tick, delta);
            Commit(actions.up, frame.Vertical > 0, tick, delta);
            Commit(actions.down, frame.Vertical < 0, tick, delta);
            Commit(actions.jump, frame.Jump, tick, delta);
            Commit(actions.dash, frame.Dash, tick, delta);
            Commit(actions.evade, frame.Dash, tick, delta);
            Commit(actions.attack, attackPressed, tick, delta);
            Commit(actions.cast, frame.Cast, tick, delta);
            Commit(actions.quickCast, frame.Cast, tick, delta);
            Commit(actions.focus, frame.Focus, tick, delta);
            UpdateMoveVector(actions.moveVector, frame.Horizontal, frame.Vertical, tick, delta);
        }

        private void ReleaseInput()
        {
            InputHandler input = InputHandler.Instance;
            if (input == null || input.inputActions == null)
            {
                hasInjectedInput = false;
                return;
            }

            HeroActions actions = input.inputActions;
            ulong tick = CurrentTick();
            float delta = CurrentDelta();

            input.inputX = 0f;
            input.inputY = 0f;

            Commit(actions.left, false, tick, delta);
            Commit(actions.right, false, tick, delta);
            Commit(actions.up, false, tick, delta);
            Commit(actions.down, false, tick, delta);
            Commit(actions.jump, false, tick, delta);
            Commit(actions.dash, false, tick, delta);
            Commit(actions.evade, false, tick, delta);
            Commit(actions.attack, false, tick, delta);
            Commit(actions.cast, false, tick, delta);
            Commit(actions.quickCast, false, tick, delta);
            Commit(actions.focus, false, tick, delta);
            UpdateMoveVector(actions.moveVector, 0, 0, tick, delta);

            hasInjectedInput = false;
        }

        private static void Commit(PlayerAction action, bool pressed, ulong tick, float delta)
        {
            if (action == null)
            {
                return;
            }

            action.CommitWithState(pressed, tick, delta);
        }

        private void UpdateMoveVector(PlayerTwoAxisAction moveVector, int horizontal, int vertical, ulong tick, float delta)
        {
            if (moveVector == null || updateWithAxes == null)
            {
                return;
            }

            try
            {
                updateWithAxes.Invoke(moveVector, new object[] { (float)horizontal, (float)vertical, tick, delta });
            }
            catch
            {
            }
        }

        private static ulong CurrentTick()
        {
            return (ulong)Math.Max(1, Time.frameCount);
        }

        private static float CurrentDelta()
        {
            return Mathf.Max(Time.deltaTime, 0.0166667f);
        }

        private static bool SafeCanInput(HeroController hero)
        {
            try
            {
                return hero.CanInput();
            }
            catch
            {
                return false;
            }
        }

        private static bool SafePublicBool(Func<bool> func)
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

        private static bool InvokeBool(HeroController hero, MethodInfo method)
        {
            if (hero == null || method == null)
            {
                return false;
            }

            try
            {
                return (bool)method.Invoke(hero, null);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAttackDirectionLegal(HeroController hero, RLActionFrame frame)
        {
            return frame.AttackDirection != GlobalEnums.AttackDirection.downward || !hero.cState.onGround;
        }
    }
}
