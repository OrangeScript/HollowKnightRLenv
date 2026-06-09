# HollowKnightRLBridge

Local TCP bridge for Hollow Knight boss RL experiments.

The mod listens on `127.0.0.1:9999`. The Python side sends actions and receives
`obs/reward/done/info` in a Gym/Gymnasium-compatible environment.

## Current Shape

- One RL step holds an action for `step_frames` Unity frames, default `1`.
- Observation size is `66`: 48 state features + 18 action-mask values.
- The action mask is returned in both `obs[-18:]` and `info["action_mask"]`.
- `reset` clears the reward baseline and can refill health/soul.
- `hard_reset=true` asks the game to reload the current scene.

## Discrete Action Space

| id | action |
| --- | --- |
| 0 | noop |
| 1 | left |
| 2 | right |
| 3 | up |
| 4 | down |
| 5 | jump |
| 6 | left_jump |
| 7 | right_jump |
| 8 | attack |
| 9 | left_attack |
| 10 | right_attack |
| 11 | up_attack |
| 12 | down_attack |
| 13 | dash |
| 14 | left_dash |
| 15 | right_dash |
| 16 | spell |
| 17 | focus_heal |

Important semantics:

- Actions are injected through `HeroActions`, the same input layer used by keyboard/controller input.
- `left/right` update both button state and `moveVector`, which is what `HeroController.LookForInput()` reads for movement.
- `jump`, `dash`, `attack`, `spell`, and `focus_heal` are not direct private-method calls; the vanilla `HeroController` state machine handles them.
- `down_attack` is only available while airborne. On ground its mask is 0 and the mod will not inject attack.
- `spell` is the quick spell/cast action.
- `focus_heal` is the heal/focus hold action.
- If the hero dies, the mod starts a scene reload for the active boss scene to avoid vanilla death/respawn corrupting a training episode.

## Python

Install:

```bash
pip install -r requirements.txt
```

Use:

```python
from hk_gym_env import HollowKnightBossEnv

env = HollowKnightBossEnv(action_mode="multidiscrete", step_frames=1)
obs, info = env.reset()

mask = env.action_mask()
obs, reward, terminated, truncated, info = env.step(1)
```

Load a boss scene on reset:

```python
env = HollowKnightBossEnv(
    boss_scene="GG_Hornet_2",
    action_mode="discrete",
    step_frames=1,
    timeout=20.0,
)
obs, info = env.reset()
```

Load immediately when the env object is created:

```python
env = HollowKnightBossEnv(boss_scene="GG_Hornet_2", auto_reset=True)
```

Smoke test:

```bash
python test.py GG_Hornet_2
```

The scene name must be the actual Hollow Knight scene name. If omitted, reset stays in the current scene.

MultiDiscrete mode:

```python
env = HollowKnightBossEnv(action_mode="multidiscrete")
# [move_x, move_y, jump, dash, attack, spell, focus_heal]
# move_x/move_y: 0=-1, 1=0, 2=1
obs, reward, terminated, truncated, info = env.step([2, 1, 0, 0, 1, 0, 0])
```

For Hollow Knight, `multidiscrete` is the preferred training mode because several actions are naturally simultaneous. Discrete mode is mainly a small baseline action table.

## Protocol

Legacy integer action:

```text
8
```

JSON step:

```json
{"command":"step","action":8,"frames":3}
```

JSON reset:

```json
{"command":"reset","refill":true,"hard_reset":false}
```

JSON dict action:

```json
{"command":"step","action":{"horizontal":1,"jump":true},"frames":4}
```
