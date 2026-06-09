import sys

from hk_gym_env import HollowKnightBossEnv


def main():
    boss_scene = sys.argv[1] if len(sys.argv) > 1 else None
    env = HollowKnightBossEnv(boss_scene=boss_scene)
    obs, info = env.reset()
    print("obs shape:", obs.shape)
    print("scene:", info.get("scene"))
    print("can_input:", info.get("can_input"))
    print("target:", info.get("target_name"), info.get("target_hp"))
    print("actions:", info.get("action_names"))
    print("mask:", env.action_mask().tolist())
    obs, reward, terminated, truncated, info = env.step([1, 1, 0, 0, 0, 0, 0])
    print("step:", reward, terminated, truncated, info.get("target_name"), info.get("target_hp"))
    print("mod_reward:", info.get("mod_reward"))
    env.close()


if __name__ == "__main__":
    main()
