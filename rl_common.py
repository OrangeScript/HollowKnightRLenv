import json
from pathlib import Path
from typing import Any, Dict, Optional

from gymnasium.wrappers import TimeLimit
from stable_baselines3.common.monitor import Monitor
from stable_baselines3.common.vec_env import DummyVecEnv, VecNormalize

from hk_gym_env import DEFAULT_REWARD_WEIGHTS, HollowKnightBossEnv


def reward_weights_from_args(args: Any) -> Dict[str, float]:
    weights = dict(DEFAULT_REWARD_WEIGHTS)
    weights.update(
        {
            "time": float(args.time_penalty),
            "boss_damage": float(args.boss_damage_reward),
            "hero_damage": float(args.hero_damage_penalty),
            "hero_heal": float(args.hero_heal_reward),
            "boss_kill": float(args.boss_kill_reward),
            "hero_death": float(args.hero_death_penalty),
        }
    )
    return weights


def make_hk_env(
    *,
    boss_scene: Optional[str],
    entry_gate: Optional[str],
    host: str,
    port: int,
    step_frames: int,
    timeout: float,
    max_episode_steps: int,
    reward_weights: Dict[str, float],
    monitor_path: Optional[Path],
):
    def _init():
        env = HollowKnightBossEnv(
            host=host,
            port=port,
            boss_scene=boss_scene,
            entry_gate=entry_gate,
            step_frames=step_frames,
            timeout=timeout,
            action_mode="multidiscrete",
            refill_on_reset=True,
            hard_reset=False,
            reward_weights=reward_weights,
        )

        if max_episode_steps > 0:
            env = TimeLimit(env, max_episode_steps=max_episode_steps)

        if monitor_path is not None:
            monitor_path.parent.mkdir(parents=True, exist_ok=True)
            env = Monitor(env, filename=str(monitor_path))

        return env

    return _init


def make_vec_env(
    *,
    boss_scene: Optional[str],
    entry_gate: Optional[str],
    host: str,
    port: int,
    step_frames: int,
    timeout: float,
    max_episode_steps: int,
    reward_weights: Dict[str, float],
    monitor_path: Optional[Path],
    normalize: bool,
    gamma: float,
    normalize_path: Optional[Path] = None,
    training: bool = True,
):
    env = DummyVecEnv(
        [
            make_hk_env(
                boss_scene=boss_scene,
                entry_gate=entry_gate,
                host=host,
                port=port,
                step_frames=step_frames,
                timeout=timeout,
                max_episode_steps=max_episode_steps,
                reward_weights=reward_weights,
                monitor_path=monitor_path,
            )
        ]
    )

    if not normalize:
        return env

    if normalize_path is not None and normalize_path.exists():
        env = VecNormalize.load(str(normalize_path), env)
    else:
        env = VecNormalize(
            env,
            norm_obs=True,
            norm_reward=True,
            clip_obs=10.0,
            gamma=gamma,
        )

    env.training = training
    env.norm_reward = training
    return env


def find_vecnormalize_path(model_path: Optional[str]) -> Optional[Path]:
    if not model_path:
        return None

    path = Path(model_path)
    parent = path.parent
    direct = parent / "vecnormalize.pkl"
    if direct.exists():
        return direct

    candidates = sorted(
        parent.glob("*vecnormalize*.pkl"),
        key=lambda item: item.stat().st_mtime,
        reverse=True,
    )
    if candidates:
        return candidates[0]

    run_direct = parent.parent / "vecnormalize.pkl"
    if run_direct.exists():
        return run_direct

    return None


def write_json(path: Path, payload: Dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2, ensure_ascii=False)
