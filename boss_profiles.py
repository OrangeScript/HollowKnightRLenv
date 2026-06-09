from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Dict, Iterable, Optional, Tuple, Type, Union


BASE_OBSERVATION_SIZE = 66
BOSS_FEATURE_CAPACITY = 12
OBSERVATION_SIZE = BASE_OBSERVATION_SIZE + BOSS_FEATURE_CAPACITY


def default_reward(info: Dict[str, Any], weights: Dict[str, float]) -> float:
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


@dataclass
class BossProfile:
    profile_id: str = "default"
    display_name: str = "Default"
    boss_scene: Optional[str] = None
    entry_gate: Optional[str] = None
    observation_size: int = OBSERVATION_SIZE
    log_keys: Tuple[str, ...] = field(default_factory=tuple)

    def reward(self, info: Dict[str, Any], weights: Dict[str, float]) -> float:
        return default_reward(info, weights)


@dataclass
class HornetProfile(BossProfile):
    profile_id: str = "hornet"
    display_name: str = "Hornet"
    boss_scene: Optional[str] = "GG_Hornet_2"
    log_keys: Tuple[str, ...] = (
        "hornet_obstacle_distance",
        "hornet_obstacle_count",
    )

    def reward(self, info: Dict[str, Any], weights: Dict[str, float]) -> float:
        reward = default_reward(info, weights)
        distance = float(info.get("hornet_obstacle_distance", -1.0))

        if distance >= 0.0:
            if distance < 2.5:
                reward -= (2.5 - distance) * 0.04
            elif distance > 5.0:
                reward += 0.005

        return float(reward)


@dataclass
class MarkothProfile(BossProfile):
    profile_id: str = "markoth"
    display_name: str = "Markoth"
    boss_scene: Optional[str] = "GG_Markoth"
    log_keys: Tuple[str, ...] = (
        "markoth_shield_distance",
        "markoth_weapon_distance",
        "markoth_platform_distance",
        "markoth_platform_count",
    )

    def reward(self, info: Dict[str, Any], weights: Dict[str, float]) -> float:
        reward = default_reward(info, weights)
        shield_distance = float(info.get("markoth_shield_distance", -1.0))
        weapon_distance = float(info.get("markoth_weapon_distance", -1.0))
        platform_distance = float(info.get("markoth_platform_distance", -1.0))

        if shield_distance >= 0.0 and shield_distance < 2.0:
            reward -= (2.0 - shield_distance) * 0.04

        if weapon_distance >= 0.0 and weapon_distance < 3.0:
            reward -= (3.0 - weapon_distance) * 0.05

        if platform_distance >= 0.0:
            if platform_distance < 4.0:
                reward += 0.01
            elif platform_distance > 8.0:
                reward -= 0.01

        return float(reward)


PROFILE_TYPES: Dict[str, Type[BossProfile]] = {
    "default": BossProfile,
    "hornet": HornetProfile,
    "markoth": MarkothProfile,
}


def list_boss_profiles() -> Tuple[str, ...]:
    return tuple(PROFILE_TYPES.keys())


def resolve_boss_profile(profile: Optional[Union[str, BossProfile]]) -> BossProfile:
    if isinstance(profile, BossProfile):
        return profile

    if profile is None:
        profile = "default"

    key = str(profile).lower()
    if key not in PROFILE_TYPES:
        known = ", ".join(list_boss_profiles())
        raise ValueError(f"unknown boss profile '{profile}', expected one of: {known}")

    return PROFILE_TYPES[key]()


def profile_log_keys(profile: Optional[Union[str, BossProfile]]) -> Iterable[str]:
    return resolve_boss_profile(profile).log_keys
