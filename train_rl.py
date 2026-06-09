import argparse
import sys
from datetime import datetime
from pathlib import Path
from typing import Any, Dict

from stable_baselines3 import PPO
from stable_baselines3.common.callbacks import BaseCallback, CallbackList, CheckpointCallback

from boss_profiles import list_boss_profiles, resolve_boss_profile
from rl_common import find_vecnormalize_path, make_vec_env, reward_weights_from_args, write_json


class HollowKnightInfoCallback(BaseCallback):
    def __init__(self, boss_profile: Any):
        super().__init__()
        self.boss_profile = resolve_boss_profile(boss_profile)

    def _on_step(self) -> bool:
        infos = self.locals.get("infos", [])
        if not infos:
            return True

        info = infos[0]
        for key in (
            "hero_health",
            "hero_soul",
            "boss_total_hp",
            "target_hp",
            "boss_damage",
            "hero_delta",
            "episode_steps",
        ):
            value = info.get(key)
            if isinstance(value, (int, float)):
                self.logger.record(f"hk/{key}", value)

        for key in self.boss_profile.log_keys:
            value = info.get(key)
            if isinstance(value, (int, float)):
                self.logger.record(f"hk/{key}", value)

        self.logger.record("hk/boss_dead", 1.0 if info.get("boss_dead") else 0.0)
        self.logger.record("hk/hero_dead", 1.0 if info.get("hero_dead") else 0.0)
        self.logger.record("hk/time_limit", 1.0 if info.get("TimeLimit.truncated") else 0.0)

        return True


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Train a Hollow Knight boss policy.")
    parser.add_argument("--boss-profile", choices=list_boss_profiles(), default="hornet")
    parser.add_argument("--boss-scene", default=None)
    parser.add_argument("--entry-gate", default=None)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=9999)
    parser.add_argument("--timeout", type=float, default=20.0)
    parser.add_argument("--step-frames", type=int, default=1)
    parser.add_argument("--max-episode-steps", type=int, default=3600)
    parser.add_argument("--timesteps", type=int, default=1_000_000)
    parser.add_argument("--algo", choices=("recurrent_ppo", "ppo"), default="recurrent_ppo")
    parser.add_argument("--device", default="auto")
    parser.add_argument("--run-name", default=None)
    parser.add_argument("--log-dir", default="runs")
    parser.add_argument("--resume", default=None)
    parser.add_argument("--normalize-path", default=None)
    parser.add_argument("--no-normalize", action="store_true")
    parser.add_argument("--checkpoint-freq", type=int, default=25_000)

    parser.add_argument("--learning-rate", type=float, default=3e-4)
    parser.add_argument("--n-steps", type=int, default=1024)
    parser.add_argument("--batch-size", type=int, default=256)
    parser.add_argument("--n-epochs", type=int, default=4)
    parser.add_argument("--gamma", type=float, default=0.995)
    parser.add_argument("--gae-lambda", type=float, default=0.95)
    parser.add_argument("--clip-range", type=float, default=0.2)
    parser.add_argument("--ent-coef", type=float, default=0.02)
    parser.add_argument("--vf-coef", type=float, default=0.5)
    parser.add_argument("--max-grad-norm", type=float, default=0.5)

    parser.add_argument("--time-penalty", type=float, default=-0.01)
    parser.add_argument("--boss-damage-reward", type=float, default=0.5)
    parser.add_argument("--hero-damage-penalty", type=float, default=10.0)
    parser.add_argument("--hero-heal-reward", type=float, default=0.25)
    parser.add_argument("--boss-kill-reward", type=float, default=100.0)
    parser.add_argument("--hero-death-penalty", type=float, default=-100.0)
    return parser.parse_args(argv)


def load_algorithm(algo: str):
    if algo == "ppo":
        return PPO, "MlpPolicy"

    try:
        from sb3_contrib import RecurrentPPO
    except ImportError as exc:
        raise RuntimeError(
            "recurrent_ppo requires sb3-contrib. Run: pip install -r requirements.txt"
        ) from exc

    return RecurrentPPO, "MlpLstmPolicy"


def default_run_name(args: Any) -> str:
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    profile = resolve_boss_profile(args.boss_profile)
    scene = args.boss_scene or profile.boss_scene or "current_scene"
    return f"{scene}_{args.algo}_{stamp}"


def model_kwargs(policy: str, env: Any, args: Any, tensorboard_dir: Path) -> Dict[str, Any]:
    return {
        "policy": policy,
        "env": env,
        "learning_rate": args.learning_rate,
        "n_steps": args.n_steps,
        "batch_size": args.batch_size,
        "n_epochs": args.n_epochs,
        "gamma": args.gamma,
        "gae_lambda": args.gae_lambda,
        "clip_range": args.clip_range,
        "ent_coef": args.ent_coef,
        "vf_coef": args.vf_coef,
        "max_grad_norm": args.max_grad_norm,
        "verbose": 1,
        "device": args.device,
        "tensorboard_log": str(tensorboard_dir),
    }


def main(argv=None):
    args = parse_args(argv)
    algo_cls, policy = load_algorithm(args.algo)
    boss_profile = resolve_boss_profile(args.boss_profile)

    run_name = args.run_name or default_run_name(args)
    run_dir = Path(args.log_dir) / run_name
    checkpoint_dir = run_dir / "checkpoints"
    monitor_path = run_dir / "monitor" / "train"
    tensorboard_dir = run_dir / "tensorboard"
    run_dir.mkdir(parents=True, exist_ok=True)
    checkpoint_dir.mkdir(parents=True, exist_ok=True)

    reward_weights = reward_weights_from_args(args)
    normalize_path = Path(args.normalize_path) if args.normalize_path else None
    if normalize_path is None and args.resume:
        normalize_path = find_vecnormalize_path(args.resume)

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
        monitor_path=monitor_path,
        normalize=not args.no_normalize,
        gamma=args.gamma,
        normalize_path=normalize_path,
        training=True,
    )

    config = vars(args).copy()
    config["boss_profile_name"] = boss_profile.display_name
    config["reward_weights"] = reward_weights
    config["run_dir"] = str(run_dir)
    config["normalize_path"] = str(normalize_path) if normalize_path else None
    write_json(run_dir / "config.json", config)

    if args.resume:
        model = algo_cls.load(args.resume, env=env, device=args.device)
    else:
        model = algo_cls(**model_kwargs(policy, env, args, tensorboard_dir))

    callbacks = CallbackList(
        [
            CheckpointCallback(
                save_freq=max(1, args.checkpoint_freq),
                save_path=str(checkpoint_dir),
                name_prefix=args.algo,
                save_vecnormalize=not args.no_normalize,
            ),
            HollowKnightInfoCallback(args.boss_profile),
        ]
    )

    try:
        model.learn(
            total_timesteps=args.timesteps,
            callback=callbacks,
            tb_log_name=run_name,
            reset_num_timesteps=not bool(args.resume),
            progress_bar=True,
        )
    finally:
        model.save(str(run_dir / "final_model"))
        if not args.no_normalize and hasattr(env, "save"):
            env.save(str(run_dir / "vecnormalize.pkl"))
        env.close()


if __name__ == "__main__":
    main(sys.argv[1:])
