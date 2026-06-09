import argparse
from pathlib import Path

import numpy as np

from boss_profiles import list_boss_profiles
from rl_common import find_vecnormalize_path, make_vec_env, reward_weights_from_args


def parse_args():
    parser = argparse.ArgumentParser(description="Run a trained Hollow Knight policy.")
    parser.add_argument("--model", required=True)
    parser.add_argument("--vecnormalize", default=None)
    parser.add_argument("--boss-profile", choices=list_boss_profiles(), default="hornet")
    parser.add_argument("--boss-scene", default=None)
    parser.add_argument("--entry-gate", default=None)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=9999)
    parser.add_argument("--timeout", type=float, default=20.0)
    parser.add_argument("--step-frames", type=int, default=1)
    parser.add_argument("--max-episode-steps", type=int, default=3600)
    parser.add_argument("--episodes", type=int, default=3)
    parser.add_argument("--device", default="auto")
    parser.add_argument("--stochastic", action="store_true")
    parser.add_argument("--no-normalize", action="store_true")

    parser.add_argument("--time-penalty", type=float, default=-0.01)
    parser.add_argument("--boss-damage-reward", type=float, default=0.5)
    parser.add_argument("--hero-damage-penalty", type=float, default=10.0)
    parser.add_argument("--hero-heal-reward", type=float, default=0.25)
    parser.add_argument("--boss-kill-reward", type=float, default=100.0)
    parser.add_argument("--hero-death-penalty", type=float, default=-100.0)
    return parser.parse_args()


def load_model(path: str, env, device: str):
    try:
        from sb3_contrib import RecurrentPPO

        return RecurrentPPO.load(path, env=env, device=device), True
    except Exception:
        from stable_baselines3 import PPO

        return PPO.load(path, env=env, device=device), False


def predict(model, obs, deterministic: bool, recurrent: bool, state, episode_start):
    if recurrent:
        return model.predict(
            obs,
            state=state,
            episode_start=episode_start,
            deterministic=deterministic,
        )

    action, _ = model.predict(obs, deterministic=deterministic)
    return action, state


def main():
    args = parse_args()
    reward_weights = reward_weights_from_args(args)
    normalize_path = Path(args.vecnormalize) if args.vecnormalize else None
    if normalize_path is None:
        normalize_path = find_vecnormalize_path(args.model)

    normalize = not args.no_normalize and normalize_path is not None
    if not args.no_normalize and normalize_path is None:
        print("warning: no VecNormalize stats found; running without observation normalization")

    env = make_vec_env(
        boss_profile=args.boss_profile,
        boss_scene=args.boss_scene,
        entry_gate=args.entry_gate,
        host=args.host,
        port=args.port,
        step_frames=args.step_frames,
        timeout=args.timeout,
        max_episode_steps=args.max_episode_steps,
        reward_weights=reward_weights,
        monitor_path=None,
        normalize=normalize,
        gamma=0.995,
        normalize_path=normalize_path,
        training=False,
    )

    model, recurrent = load_model(args.model, env, args.device)
    obs = env.reset()
    state = None
    episode_start = np.ones((env.num_envs,), dtype=bool)
    episode_reward = 0.0
    episode_len = 0
    finished = 0

    try:
        while finished < args.episodes:
            action, state = predict(
                model,
                obs,
                deterministic=not args.stochastic,
                recurrent=recurrent,
                state=state,
                episode_start=episode_start,
            )
            obs, rewards, dones, infos = env.step(action)
            episode_reward += float(rewards[0])
            episode_len += 1
            episode_start = dones

            if dones[0]:
                info = infos[0]
                finished += 1
                print(
                    "episode",
                    finished,
                    "reward",
                    round(episode_reward, 3),
                    "len",
                    episode_len,
                    "boss_dead",
                    info.get("boss_dead"),
                    "hero_dead",
                    info.get("hero_dead"),
                )
                episode_reward = 0.0
                episode_len = 0
    finally:
        env.close()


if __name__ == "__main__":
    main()
