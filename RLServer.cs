using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Modding;
using Newtonsoft.Json;

namespace HollowKnightRLBridge
{
    internal class RLServer
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly Mod mod;
        private readonly RLController controller;
        private TcpListener listener;
        private Thread serverThread;
        private volatile bool running;

        public RLServer(Mod mod, RLController controller)
        {
            this.mod = mod;
            this.controller = controller;
        }

        public void Start()
        {
            if (running)
            {
                return;
            }

            running = true;
            serverThread = new Thread(ServerLoop)
            {
                IsBackground = true,
                Name = "HollowKnightRLBridge TCP"
            };
            serverThread.Start();

            mod.Log("RLServer started on 127.0.0.1:9999.");
        }

        public void Stop()
        {
            running = false;

            try
            {
                listener?.Stop();
            }
            catch
            {
            }

            try
            {
                serverThread?.Join(500);
            }
            catch
            {
            }

            mod.Log("RLServer stopped.");
        }

        private void ServerLoop()
        {
            listener = new TcpListener(IPAddress.Loopback, 9999);
            listener.Start();

            while (running)
            {
                TcpClient client = null;

                try
                {
                    client = listener.AcceptTcpClient();
                    mod.Log("Python client connected.");

                    using (client)
                    {
                        HandleClient(client);
                    }
                }
                catch (SocketException)
                {
                    if (running)
                    {
                        mod.Log("RLServer socket closed unexpectedly.");
                    }
                }
                catch (IOException e)
                {
                    mod.Log("Python client IO error: " + e.Message);
                }
                catch (Exception e)
                {
                    if (running)
                    {
                        mod.Log("RLServer client error: " + e);
                    }
                }
                finally
                {
                    try
                    {
                        client?.Close();
                    }
                    catch
                    {
                    }

                    mod.Log("Python client disconnected.");
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            client.NoDelay = true;
            using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true })
            {
                while (running && client.Connected)
                {
                    string line = reader.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    RLResponse response = HandleLine(line, out bool close);
                    writer.WriteLine(JsonConvert.SerializeObject(response, JsonSettings));

                    if (close)
                    {
                        break;
                    }
                }
            }
        }

        private RLResponse HandleLine(string line, out bool close)
        {
            close = false;

            try
            {
                RLRequest request = RLRequest.Parse(line);
                switch (request.Command)
                {
                    case RLCommandType.Step:
                        return RLResponse.FromResult(controller.Step(request.ActionFrame, request.ActionId, request.Frames, request.TimeoutMs));
                    case RLCommandType.Reset:
                        return RLResponse.FromResult(controller.ResetEpisode(request.Refill, request.HardReset, request.TargetScene, request.EntryGate, request.TimeoutMs));
                    case RLCommandType.Info:
                        return RLResponse.FromResult(controller.GetInfo(request.TimeoutMs));
                    case RLCommandType.Close:
                        close = true;
                        return new RLResponse
                        {
                            Ok = true,
                            Observation = new float[StateReader.ObservationSize],
                            Reward = 0f,
                            Done = false,
                            Truncated = false,
                            Info = new Dictionary<string, object> { ["closed"] = true }
                        };
                    default:
                        return RLResponse.FromResult(controller.GetInfo(request.TimeoutMs));
                }
            }
            catch (Exception e)
            {
                mod.Log("RL request error: " + e.Message);
                return RLResponse.ErrorResponse(e.Message);
            }
        }
    }
}
