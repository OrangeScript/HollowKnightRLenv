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
        private readonly MethodInfo move;
        private readonly MethodInfo heroJump;
        private readonly MethodInfo heroDash;
        private readonly MethodInfo jump;
        private readonly MethodInfo dash;
        private readonly MethodInfo jumpReleased;

        private RLActionFrame currentFrame;
        private bool holdingInput;
        private bool hasInjectedInput;

        public ActionExecutor()
        {
            Type heroType = typeof(HeroController);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

            canJump = heroType.GetMethod("CanJump", flags);
            canDash = heroType.GetMethod("CanDash", flags);
            canAttack = heroType.GetMethod("CanAttack", flags);
            move = heroType.GetMethod("Move", flags);
            heroJump = heroType.GetMethod("HeroJump", flags);
            heroDash = heroType.GetMethod("HeroDash", flags);
            jump = heroType.GetMethod("Jump", flags);
            dash = heroType.GetMethod("Dash", flags);
            jumpReleased = heroType.GetMethod("JumpReleased", flags);
        }

        public void BeginAction(RLActionFrame frame)
        {
            currentFrame = frame;
            holdingInput = true;
            ApplyFacing(frame);
            TryDirectOneShots(frame);
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

            HeroController hero = HeroController.instance;
            bool attackPressed = frame.Attack;
            if (hero != null)
            {
                hero.move_input = frame.Horizontal;
                hero.vertical_input = frame.Vertical;
                MoveHero(hero, frame.Horizontal);
                attackPressed = frame.Attack && IsAttackDirectionLegal(hero, frame);

                if (hero.cState.dashing)
                {
                    dash?.Invoke(hero, null);
                }

                if (frame.Jump && hero.cState.jumping)
                {
                    jump?.Invoke(hero, null);
                }
            }

            InputHandler input = InputHandler.Instance;
            if (input == null || input.inputActions == null)
            {
                return;
            }

            input.inputX = frame.Horizontal;
            input.inputY = frame.Vertical;

            HeroActions actions = input.inputActions;
            ulong tick = (ulong)Math.Max(1, Time.frameCount);
            float delta = Mathf.Max(Time.deltaTime, 0.0166667f);

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
        }

        private void ReleaseInput()
        {
            HeroController hero = HeroController.instance;
            if (hero != null)
            {
                hero.move_input = 0f;
                hero.vertical_input = 0f;
                MoveHero(hero, 0);
                jumpReleased?.Invoke(hero, null);
            }

            InputHandler input = InputHandler.Instance;
            if (input == null || input.inputActions == null)
            {
                hasInjectedInput = false;
                return;
            }

            input.inputX = 0f;
            input.inputY = 0f;

            HeroActions actions = input.inputActions;
            ulong tick = (ulong)Math.Max(1, Time.frameCount);
            float delta = Mathf.Max(Time.deltaTime, 0.0166667f);

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

        private void TryDirectOneShots(RLActionFrame frame)
        {
            HeroController hero = HeroController.instance;
            if (hero == null || !SafeCanInput(hero))
            {
                return;
            }

            try
            {
                hero.move_input = frame.Horizontal;
                hero.vertical_input = frame.Vertical;

                if (frame.Jump && InvokeBool(hero, canJump))
                {
                    heroJump?.Invoke(hero, null);
                    jump?.Invoke(hero, null);
                }

                if (frame.Dash && InvokeBool(hero, canDash))
                {
                    heroDash?.Invoke(hero, null);
                    dash?.Invoke(hero, null);
                }

                if (frame.Attack && InvokeBool(hero, canAttack) && IsAttackDirectionLegal(hero, frame))
                {
                    hero.Attack(frame.AttackDirection);
                }
            }
            catch
            {
            }
        }

        private static void ApplyFacing(RLActionFrame frame)
        {
            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                return;
            }

            try
            {
                if (frame.Face < 0)
                {
                    hero.FaceLeft();
                }
                else if (frame.Face > 0)
                {
                    hero.FaceRight();
                }
            }
            catch
            {
            }
        }

        private void MoveHero(HeroController hero, int horizontal)
        {
            if (hero == null || move == null)
            {
                return;
            }

            try
            {
                move.Invoke(hero, new object[] { (float)horizontal });
            }
            catch
            {
            }
        }

        private static bool IsAttackDirectionLegal(HeroController hero, RLActionFrame frame)
        {
            return frame.AttackDirection != GlobalEnums.AttackDirection.downward || !hero.cState.onGround;
        }
    }
}
