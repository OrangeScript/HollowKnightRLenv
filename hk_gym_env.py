import json
import socket
from typing import Any, Dict, Optional, Tuple

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


class HollowKnightBossEnv(gym.Env):
    metadata = {"render_modes": []}

    def __init__(
        self,
        host: str = "127.0.0.1",
        port: int = 9999,
        action_mode: str = "discrete",
        step_frames: int = 3,
        timeout: float = 5.0,
        refill_on_reset: bool = True,
        hard_reset: bool = False,
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
            "timeout_ms": int(self.timeout * 1000),
        }
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

        reward = float(obj.get("reward", 0.0))
        terminated = bool(obj.get("done", False))
        truncated = bool(obj.get("truncated", False))
        info = dict(obj.get("info") or {})
        self.last_info = info
        return obs, reward, terminated, truncated, info

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
