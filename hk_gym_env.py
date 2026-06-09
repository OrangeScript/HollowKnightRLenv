import json
import socket
from typing import Any, Callable, Dict, Optional, Tuple

import numpy as np

try:
    import gymnasium as gym
    from gymnasium import spaces
except ImportError:  # pragma: no cover - compatibility path for older setups.
    import gym
    from gym import spaces


ACTION_NAMES = [
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
    "focus_heal",
]

OBSERVATION_SIZE = 66

DEFAULT_REWARD_WEIGHTS = {
    "time": -0.01,
    "boss_damage": 0.5,
    "hero_damage": 10.0,
    "hero_heal": 0.25,
    "boss_kill": 100.0,
    "hero_death": -100.0,
}


class HollowKnightBossEnv(gym.Env):
    metadata = {"render_modes": []}

    def __init__(
        self,
        host: str = "127.0.0.1",
        port: int = 9999,
        action_mode: str = "multidiscrete",
        step_frames: int = 1,
        timeout: float = 20.0,
        refill_on_reset: bool = True,
        hard_reset: bool = False,
        boss_scene: Optional[str] = None,
        entry_gate: Optional[str] = None,
        auto_reset: bool = False,
        use_python_reward: bool = True,
        reward_fn: Optional[Callable[[Dict[str, Any]], float]] = None,
        reward_weights: Optional[Dict[str, float]] = None,
        legacy_gym_api: bool = False,
        connect: bool = True,
    ):
        super().__init__()
        self.host = host
        self.port = port
        self.action_mode = action_mode
        self.step_frames = int(step_frames)
        self.timeout = float(timeout)
        self.refill_on_reset = bool(refill_on_reset)
        self.hard_reset = bool(hard_reset)
        self.boss_scene = boss_scene
        self.entry_gate = entry_gate
        self.use_python_reward = bool(use_python_reward)
        self.reward_fn = reward_fn
        self.reward_weights = dict(DEFAULT_REWARD_WEIGHTS)
        if reward_weights:
            self.reward_weights.update(reward_weights)
        self.legacy_gym_api = bool(legacy_gym_api)

        self.conn: Optional[socket.socket] = None
        self._recv_buffer = b""
        self.last_info: Dict[str, Any] = {}

        if action_mode == "discrete":
            self.action_space = spaces.Discrete(len(ACTION_NAMES))
        elif action_mode == "multidiscrete":
            self.action_space = spaces.MultiDiscrete([3, 3, 2, 2, 2, 2, 2])
        else:
            raise ValueError("action_mode must be 'discrete' or 'multidiscrete'")

        self.observation_space = spaces.Box(
            low=-np.inf,
            high=np.inf,
            shape=(OBSERVATION_SIZE,),
            dtype=np.float32,
        )

        if connect:
            self.connect()
            if auto_reset:
                self.reset()

    @property
    def action_names(self):
        return ACTION_NAMES

    def connect(self):
        if self.conn is not None:
            return

        self.conn = socket.create_connection((self.host, self.port), timeout=self.timeout)
        self.conn.settimeout(self.timeout)

    def step(self, action):
        payload = {
            "command": "step",
            "action": self._encode_action(action),
            "frames": self.step_frames,
            "timeout_ms": int(self.timeout * 1000),
        }
        obj = self._request(payload)
        obs, reward, terminated, truncated, info = self._decode_step(obj)

        if self.legacy_gym_api:
            return obs, reward, terminated or truncated, info

        return obs, reward, terminated, truncated, info

    def reset(self, *, seed=None, options=None):
        try:
            super().reset(seed=seed)
        except TypeError:
            pass

        options = options or {}
        payload = {
            "command": "reset",
            "refill": bool(options.get("refill", self.refill_on_reset)),
            "hard_reset": bool(options.get("hard_reset", self.hard_reset)),
            "boss_scene": options.get("boss_scene", self.boss_scene),
            "entry_gate": options.get("entry_gate", self.entry_gate),
            "timeout_ms": int(self.timeout * 1000),
        }
        payload = {key: value for key, value in payload.items() if value is not None}
        obj = self._request(payload)
        obs, _, _, _, info = self._decode_step(obj)

        if self.legacy_gym_api:
            return obs

        return obs, info

    def get_info(self) -> Dict[str, Any]:
        obj = self._request({"command": "info", "timeout_ms": int(self.timeout * 1000)})
        _, _, _, _, info = self._decode_step(obj)
        return info

    def action_mask(self) -> np.ndarray:
        mask = self.last_info.get("action_mask")
        if mask is None:
            return np.ones(len(ACTION_NAMES), dtype=np.int8)

        return np.asarray(mask, dtype=np.int8)

    def close(self):
        if self.conn is None:
            return

        try:
            self._request({"command": "close", "timeout_ms": int(self.timeout * 1000)})
        except (OSError, RuntimeError, TimeoutError):
            pass

        try:
            self.conn.close()
        finally:
            self.conn = None

    def _encode_action(self, action):
        if isinstance(action, dict):
            return action

        if self.action_mode == "discrete":
            return int(action)

        arr = np.asarray(action, dtype=np.int64).reshape(-1)
        if arr.size != 7:
            raise ValueError("multidiscrete action must have 7 elements")

        return arr.tolist()

    def _decode_step(self, obj: Dict[str, Any]) -> Tuple[np.ndarray, float, bool, bool, Dict[str, Any]]:
        obs = np.asarray(obj.get("obs", np.zeros(OBSERVATION_SIZE)), dtype=np.float32)
        if obs.shape != (OBSERVATION_SIZE,):
            raise ValueError(f"unexpected observation shape {obs.shape}, expected {(OBSERVATION_SIZE,)}")

        mod_reward = float(obj.get("reward", 0.0))
        terminated = bool(obj.get("done", False))
        truncated = bool(obj.get("truncated", False))
        info = dict(obj.get("info") or {})
        info["mod_reward"] = mod_reward
        reward = self._compute_reward(info) if self.use_python_reward else mod_reward
        self.last_info = info
        return obs, reward, terminated, truncated, info

    def _compute_reward(self, info: Dict[str, Any]) -> float:
        if self.reward_fn is not None:
            return float(self.reward_fn(info))

        weights = self.reward_weights
        hero_delta = float(info.get("hero_delta", 0.0))
        reward = float(weights["time"])
        reward += float(info.get("boss_damage", 0.0)) * float(weights["boss_damage"])

        if hero_delta < 0:
            reward += hero_delta * float(weights["hero_damage"])
        elif hero_delta > 0:
            reward += hero_delta * float(weights["hero_heal"])

        if bool(info.get("boss_dead", False)):
            reward += float(weights["boss_kill"])

        if bool(info.get("hero_dead", False)):
            reward += float(weights["hero_death"])

        return float(reward)

    def _request(self, payload: Any) -> Dict[str, Any]:
        self._send(payload)
        line = self._recv_line()
        obj = json.loads(line)
        if not obj.get("ok", False):
            raise RuntimeError(obj.get("error") or "HollowKnightRLBridge returned ok=false")

        return obj

    def _send(self, payload: Any):
        if self.conn is None:
            self.connect()

        if isinstance(payload, (dict, list)):
            text = json.dumps(payload, separators=(",", ":"))
        else:
            text = str(payload)

        self.conn.sendall((text + "\n").encode("utf-8"))

    def _recv_line(self) -> str:
        if self.conn is None:
            raise ConnectionError("socket is not connected")

        while b"\n" not in self._recv_buffer:
            chunk = self.conn.recv(4096)
            if not chunk:
                raise ConnectionError("socket closed")
            self._recv_buffer += chunk

        line, self._recv_buffer = self._recv_buffer.split(b"\n", 1)
        return line.decode("utf-8")


HKBossEnv = HollowKnightBossEnv
HKBoosEnv = HollowKnightBossEnv
