namespace HollowKnightRLBridge
{
    internal struct RLActionFrame
    {
        public int Horizontal;
        public int Vertical;
        public int Face;
        public bool Jump;
        public bool Dash;
        public bool Attack;
        public bool Cast;
        public bool Focus;

        public GlobalEnums.AttackDirection AttackDirection
        {
            get
            {
                if (Vertical > 0)
                {
                    return GlobalEnums.AttackDirection.upward;
                }

                if (Vertical < 0)
                {
                    return GlobalEnums.AttackDirection.downward;
                }

                return GlobalEnums.AttackDirection.normal;
            }
        }
    }

    internal struct ActionAvailability
    {
        public bool CanInput;
        public bool CanMove;
        public bool CanJump;
        public bool CanDash;
        public bool CanAttack;
        public bool CanDownAttack;
        public bool CanCast;
        public bool CanFocus;
    }

    internal static class RLActionSpace
    {
        public const int ActionCount = 18;

        public static readonly string[] Names =
        {
            "noop",
            "left",
            "right",
            "up",
            "down",
            "jump",
            "left_jump",
            "right_jump",
            "attack",
            "left_attack",
            "right_attack",
            "up_attack",
            "down_attack",
            "dash",
            "left_dash",
            "right_dash",
            "spell",
            "focus_heal"
        };

        public static RLActionFrame FromDiscrete(int action)
        {
            switch (action)
            {
                case 1:
                    return Move(-1);
                case 2:
                    return Move(1);
                case 3:
                    return Vertical(1);
                case 4:
                    return Vertical(-1);
                case 5:
                    return new RLActionFrame { Jump = true };
                case 6:
                    return new RLActionFrame { Horizontal = -1, Face = -1, Jump = true };
                case 7:
                    return new RLActionFrame { Horizontal = 1, Face = 1, Jump = true };
                case 8:
                    return new RLActionFrame { Attack = true };
                case 9:
                    return new RLActionFrame { Horizontal = -1, Face = -1, Attack = true };
                case 10:
                    return new RLActionFrame { Horizontal = 1, Face = 1, Attack = true };
                case 11:
                    return new RLActionFrame { Vertical = 1, Attack = true };
                case 12:
                    return new RLActionFrame { Vertical = -1, Attack = true };
                case 13:
                    return new RLActionFrame { Dash = true };
                case 14:
                    return new RLActionFrame { Horizontal = -1, Face = -1, Dash = true };
                case 15:
                    return new RLActionFrame { Horizontal = 1, Face = 1, Dash = true };
                case 16:
                    return new RLActionFrame { Cast = true };
                case 17:
                    return new RLActionFrame { Focus = true };
                default:
                    return new RLActionFrame();
            }
        }

        public static bool[] BuildMask(ActionAvailability availability)
        {
            bool[] mask = new bool[ActionCount];

            mask[0] = true;
            mask[1] = availability.CanMove;
            mask[2] = availability.CanMove;
            mask[3] = availability.CanMove;
            mask[4] = availability.CanMove;
            mask[5] = availability.CanJump;
            mask[6] = availability.CanMove && availability.CanJump;
            mask[7] = availability.CanMove && availability.CanJump;
            mask[8] = availability.CanAttack;
            mask[9] = availability.CanMove && availability.CanAttack;
            mask[10] = availability.CanMove && availability.CanAttack;
            mask[11] = availability.CanAttack;
            mask[12] = availability.CanDownAttack;
            mask[13] = availability.CanDash;
            mask[14] = availability.CanMove && availability.CanDash;
            mask[15] = availability.CanMove && availability.CanDash;
            mask[16] = availability.CanCast;
            mask[17] = availability.CanFocus;

            return mask;
        }

        private static RLActionFrame Move(int horizontal)
        {
            return new RLActionFrame { Horizontal = horizontal, Face = horizontal };
        }

        private static RLActionFrame Vertical(int vertical)
        {
            return new RLActionFrame { Vertical = vertical };
        }
    }
}
