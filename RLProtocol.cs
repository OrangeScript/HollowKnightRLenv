using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HollowKnightRLBridge
{
    internal enum RLCommandType
    {
        Step,
        Reset,
        Info,
        Ping,
        Close
    }

    internal sealed class RLRequest
    {
        public RLCommandType Command;
        public int ActionId;
        public RLActionFrame ActionFrame;
        public int Frames = 1;
        public int TimeoutMs = 3000;
        public bool Refill = true;
        public bool HardReset;
        public string TargetScene;
        public string EntryGate;

        public static RLRequest Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return new RLRequest { Command = RLCommandType.Ping };
            }

            line = line.Trim();

            if (int.TryParse(line, out int action))
            {
                return StepFromDiscrete(action, 1, 3000);
            }

            string lower = line.ToLowerInvariant();
            if (lower == "reset")
            {
                return new RLRequest { Command = RLCommandType.Reset };
            }

            if (lower == "info" || lower == "observe" || lower == "observation")
            {
                return new RLRequest { Command = RLCommandType.Info };
            }

            if (lower == "ping")
            {
                return new RLRequest { Command = RLCommandType.Ping };
            }

            if (lower == "close" || lower == "quit")
            {
                return new RLRequest { Command = RLCommandType.Close };
            }

            JObject obj = JObject.Parse(line);
            string commandText = ((string)obj["command"] ?? (string)obj["cmd"] ?? "step").ToLowerInvariant();
            RLRequest request = new RLRequest
            {
                Frames = ClampInt((int?)obj["frames"] ?? (int?)obj["frame_skip"] ?? 1, 1, 60),
                TimeoutMs = ClampInt((int?)obj["timeout_ms"] ?? 3000, 100, 60000),
                Refill = (bool?)obj["refill"] ?? true,
                HardReset = (bool?)obj["hard_reset"] ?? (bool?)obj["reload_scene"] ?? false,
                TargetScene = (string)obj["boss_scene"] ?? (string)obj["scene"] ?? (string)obj["target_scene"],
                EntryGate = (string)obj["entry_gate"] ?? (string)obj["gate"]
            };

            switch (commandText)
            {
                case "reset":
                    request.Command = RLCommandType.Reset;
                    break;
                case "info":
                case "observe":
                case "observation":
                    request.Command = RLCommandType.Info;
                    break;
                case "ping":
                    request.Command = RLCommandType.Ping;
                    break;
                case "close":
                case "quit":
                    request.Command = RLCommandType.Close;
                    break;
                default:
                    request.Command = RLCommandType.Step;
                    JToken token = obj["action"];
                    if (token == null)
                    {
                        request.ActionId = 0;
                        request.ActionFrame = RLActionSpace.FromDiscrete(0);
                    }
                    else
                    {
                        request.ActionFrame = ParseAction(token, out request.ActionId);
                    }

                    break;
            }

            return request;
        }

        private static RLRequest StepFromDiscrete(int action, int frames, int timeoutMs)
        {
            action = ClampInt(action, 0, RLActionSpace.ActionCount - 1);
            return new RLRequest
            {
                Command = RLCommandType.Step,
                ActionId = action,
                ActionFrame = RLActionSpace.FromDiscrete(action),
                Frames = frames,
                TimeoutMs = timeoutMs
            };
        }

        private static RLActionFrame ParseAction(JToken token, out int actionId)
        {
            if (token.Type == JTokenType.Integer)
            {
                actionId = ClampInt((int)token, 0, RLActionSpace.ActionCount - 1);
                return RLActionSpace.FromDiscrete(actionId);
            }

            actionId = -1;

            if (token.Type == JTokenType.Array)
            {
                JArray array = (JArray)token;
                return new RLActionFrame
                {
                    Horizontal = ParseAxis(array, 0),
                    Vertical = ParseAxis(array, 1),
                    Jump = ParseBool(array, 2),
                    Dash = ParseBool(array, 3),
                    Attack = ParseBool(array, 4),
                    Cast = ParseBool(array, 5),
                    Focus = ParseBool(array, 6)
                };
            }

            JObject obj = (JObject)token;
            int horizontal = ClampInt((int?)obj["horizontal"] ?? (int?)obj["move_x"] ?? (int?)obj["x"] ?? 0, -1, 1);
            int vertical = ClampInt((int?)obj["vertical"] ?? (int?)obj["move_y"] ?? (int?)obj["y"] ?? 0, -1, 1);
            return new RLActionFrame
            {
                Horizontal = horizontal,
                Vertical = vertical,
                Face = ClampInt((int?)obj["face"] ?? horizontal, -1, 1),
                Jump = (bool?)obj["jump"] ?? false,
                Dash = (bool?)obj["dash"] ?? false,
                Attack = (bool?)obj["attack"] ?? false,
                Cast = (bool?)obj["spell"] ?? (bool?)obj["cast"] ?? false,
                Focus = (bool?)obj["focus_heal"] ?? (bool?)obj["focus"] ?? false
            };
        }

        private static int ParseAxis(JArray array, int index)
        {
            if (index >= array.Count)
            {
                return 0;
            }

            int value = (int)array[index];
            if (value >= 0 && value <= 2)
            {
                return value - 1;
            }

            return ClampInt(value, -1, 1);
        }

        private static bool ParseBool(JArray array, int index)
        {
            if (index >= array.Count)
            {
                return false;
            }

            JToken value = array[index];
            return value.Type == JTokenType.Boolean ? (bool)value : (int)value != 0;
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }
    }

    internal sealed class RLStepResult
    {
        public float[] Observation;
        public float Reward;
        public bool Done;
        public bool Truncated;
        public Dictionary<string, object> Info = new Dictionary<string, object>();
    }

    internal sealed class RLResponse
    {
        [JsonProperty("ok")]
        public bool Ok;

        [JsonProperty("obs")]
        public float[] Observation;

        [JsonProperty("reward")]
        public float Reward;

        [JsonProperty("done")]
        public bool Done;

        [JsonProperty("truncated")]
        public bool Truncated;

        [JsonProperty("info")]
        public Dictionary<string, object> Info;

        [JsonProperty("error")]
        public string Error;

        public static RLResponse FromResult(RLStepResult result)
        {
            return new RLResponse
            {
                Ok = true,
                Observation = result.Observation,
                Reward = result.Reward,
                Done = result.Done,
                Truncated = result.Truncated,
                Info = result.Info
            };
        }

        public static RLResponse ErrorResponse(string error)
        {
            return new RLResponse
            {
                Ok = false,
                Observation = new float[StateReader.ObservationSize],
                Reward = 0f,
                Done = false,
                Truncated = false,
                Info = new Dictionary<string, object>(),
                Error = error
            };
        }
    }
}
