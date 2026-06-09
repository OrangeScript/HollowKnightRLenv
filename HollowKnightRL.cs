using Modding;
using UnityEngine;

namespace HollowKnightRLBridge
{
    public class HollowKnightRL : Mod, ITogglableMod
    {
        private RLServer server;
        private GameObject controllerObj;

        public override string GetVersion() => "2.0.0";

        public override void Initialize()
        {
            Log("HollowKnightRLBridge loaded.");

            controllerObj = new GameObject("RLController");
            UnityEngine.Object.DontDestroyOnLoad(controllerObj);

            RLController rlController = controllerObj.AddComponent<RLController>();
            server = new RLServer(this, rlController);
            server.Start();
        }

        public void Unload()
        {
            Log("HollowKnightRLBridge unloading.");

            if (server != null)
            {
                server.Stop();
                server = null;
            }

            if (controllerObj != null)
            {
                UnityEngine.Object.Destroy(controllerObj);
                controllerObj = null;
            }

            Log("HollowKnightRLBridge unloaded.");
        }
    }
}
