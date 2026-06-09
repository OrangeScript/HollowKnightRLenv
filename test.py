import gym
from gym import spaces
import numpy as np
import socket
import json

class HKBoosEnv(gym.Env):
    def __init__(self,host='127.0.0.1',port=9999):
        super().__init__()
        self.host = host
        self.port = port
        self.conn = socket.create_connection((host,port))

        self.action_space = spaces.Discrete(12)

        self.observation_space = spaces.Box(low=-1.0, high=1.0, shape=(32,), dtype=np.float32)
    
    def step(self, action):
        self.conn.sendall(f"{action}\n".encode("utf-8"))

        data = self._recv_line()
        obj = json.loads(data)
        obs = np.array(obj["obs"],dtype=np.float32)
        reward = float(obj["reward"])
        done = bool(obj["done"])

        return obs,reward,done,{}

    def reset(self, *, seed = None, options = None):
        self.conn.sendall(b"reset\n")
        data = self._recv_line()
        obj = json.loads(data)
        obs = np.array(obj["obs"], dtype=np.float32)
        return obs
    
    def _recv_line(self):
        buf = b""
        while True:
            chunk = self.conn.recv(1024)
            if not chunk:
                raise ConnectionError("socket closed")
            buf += chunk
            if b"\n" in buf:
                line, buf = buf.split(b"\n", 1)
                return line.decode("utf-8")
    
    def close(self):
        self.conn.close()
