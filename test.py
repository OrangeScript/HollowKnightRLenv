from hk_gym_env import HollowKnightBossEnv


def main():
    env = HollowKnightBossEnv()
    obs, info = env.reset()
    print("obs shape:", obs.shape)
    print("scene:", info.get("scene"))
    print("can_input:", info.get("can_input"))
    print("target:", info.get("target_name"), info.get("target_hp"))
    print("actions:", info.get("action_names"))
    print("mask:", env.action_mask().tolist())
    for i in range(1000):
        obs, reward, terminated, truncated, info = env.step(15)
    print("step:", reward, terminated, truncated, info.get("target_name"), info.get("target_hp"))
    env.close()


if __name__ == "__main__":
    main()
