# HollowKnightRLBridge

Local TCP bridge for Hollow Knight boss RL experiments.

The mod listens on `127.0.0.1:9999`. The Python side sends actions, receives
environment state from the mod, and computes Gym/Gymnasium rewards locally.

## Current Shape

- One RL step holds an action for `step_frames` Unity frames, default `1`.
- Observation size is `66`: 48 state features + 18 action-mask values.
- The action mask is returned in both `obs[-18:]` and `info["action_mask"]`.
- The mod returns state and event counters only. The Python Gym env computes all rewards.
- `reset` clears the event baseline and can refill health/soul.
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
- If the hero dies, the current step returns `done=true`. The mod does not reload the scene in the background; call `env.reset()` to start the next episode.

## Frame Timing

`step_frames` is counted in Unity game frames, using `Time.frameCount` on the mod side. The bridge does not currently force `Application.targetFrameRate`, `QualitySettings.vSyncCount`, or `Time.fixedDeltaTime`.

With `step_frames=1`, an action is injected during the hero update phase, kept through the frame so vanilla input code can read it, then the result is returned after that frame in `LateUpdate`. At 60 FPS this is roughly 16.7 ms per environment step; at 30 FPS it is roughly 33.3 ms.

For training, prefer a stable game frame rate. Lock the game externally to 60 FPS first, and do not change Unity physics timestep unless you have a specific reason.

On reset, the bridge reloads the requested boss scene, waits for the scene transition to finish, restores hero control/damage state, and only returns when combat is ready or a safety timeout is reached. Godhome boss scenes (`GG_*`) default to the vanilla dream entry gate. The reset info includes `reset_combat_ready`, `reset_enemy_ready`, `reset_hero_arena_ready`, and `reset_wait_frames`.

## Python

Install:

```bash
pip install -r requirements.txt
```

Use:

```python
from hk_gym_env import HollowKnightBossEnv

env = HollowKnightBossEnv(step_frames=1)
obs, info = env.reset()

mask = env.action_mask()
obs, reward, terminated, truncated, info = env.step([1, 1, 0, 0, 0, 0, 0])
```

Python reward defaults:

```text
time penalty: -0.01 per step
boss damage:  +0.5 * boss_damage
hero damage:  -10.0 per lost health
hero heal:    +0.25 per healed mask
boss dead:    +100
hero dead:    -100
```

Override reward shaping without recompiling the mod:

```python
def reward_fn(info):
    return info["boss_damage"] - 5.0 * max(0, -info["hero_delta"])

env = HollowKnightBossEnv(reward_fn=reward_fn)
```

Load a boss scene on reset:

```python
env = HollowKnightBossEnv(
    boss_scene="GG_Hornet_2",
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

## Training

Install training dependencies:

```bash
pip install -r requirements.txt
```

Recommended first run:

```bash
python train_rl.py --boss-scene GG_Hornet_2 --timesteps 1000000
```

The default training algorithm is `recurrent_ppo` from SB3-Contrib. It uses the `MultiDiscrete` action space and an LSTM policy so the agent can remember short boss patterns and its own recent movement rhythm. If you want the simpler baseline:

```bash
python train_rl.py --algo ppo --boss-scene GG_Hornet_2 --timesteps 1000000
```

Outputs are written under `runs/<run_name>/`:

```text
final_model.zip
vecnormalize.pkl
config.json
checkpoints/
tensorboard/
monitor/
```

Resume training:

```bash
python train_rl.py --resume runs/GG_Hornet_2_recurrent_ppo_xxx/final_model.zip --timesteps 500000
```

Run a trained policy:

```bash
python play_rl.py --model runs/GG_Hornet_2_recurrent_ppo_xxx/final_model.zip --episodes 3
```

Use only one live Hollow Knight connection at a time. Evaluation is a separate script instead of an in-training `EvalCallback` because the mod server currently accepts one TCP client.

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
