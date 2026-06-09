import sys

from hk_gym_env import HollowKnightBossEnv


def main():
    boss_scene = sys.argv[1] if len(sys.argv) > 1 else None
    boss_profile = sys.argv[2] if len(sys.argv) > 2 else None
    env = HollowKnightBossEnv(boss_scene=boss_scene, boss_profile=boss_profile)
    obs, info = env.reset()
    print("obs shape:", obs.shape)
    print("scene:", info.get("scene"))
    print("can_input:", info.get("can_input"))
    print("boss_profile:", info.get("boss_profile"), info.get("boss_profile_name"))
    print("boss_features:", info.get("boss_feature_names"), info.get("boss_features"))
    print("target:", info.get("target_name"), info.get("target_hp"))
    print(
        "damage_state:",
        info.get("hero_damage_mode"),
        "can_take_damage=",
        info.get("hero_can_take_damage"),
        "take_no_damage=",
        info.get("hero_take_no_damage"),
        "invuln=",
        info.get("hero_invulnerable"),
        "accepting_input=",
        info.get("hero_accepting_input"),
        "transition=",
        info.get("hero_transition_state"),
        "boss_transition=",
        info.get("boss_scene_transitioning"),
        "pd_invincible=",
        info.get("hero_pd_is_invincible"),
    )
    print(
        "reset_ready:",
        info.get("reset_combat_ready"),
        "enemy_ready:",
        info.get("reset_enemy_ready"),
        "arena_ready:",
        info.get("reset_hero_arena_ready"),
        "wait_frames:",
        info.get("reset_wait_frames"),
    )
    print("actions:", info.get("action_names"))
    print("mask:", env.action_mask().tolist())
    obs, reward, terminated, truncated, info = env.step([1, 1, 0, 0, 0, 0, 0])
    print("step:", reward, terminated, truncated, info.get("target_name"), info.get("target_hp"))
    env.close()


if __name__ == "__main__":
    main()
