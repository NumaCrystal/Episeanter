using Mirror;

namespace Episeanter.Content.Shared.Network
{
    public class ESNetworkManager : NetworkManager
    {
        public override void OnStartServer()
        {
            base.OnStartServer();

            foreach (INetModule module in NetModules.Server)
                module.Start();
        }

        public override void OnStopServer()
        {
            foreach (INetModule module in NetModules.Server)
                module.Stop();

            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            foreach (INetModule module in NetModules.Client)
                module.Start();
        }

        public override void OnStopClient()
        {
            foreach (INetModule module in NetModules.Client)
                module.Stop();

            base.OnStopClient();
        }
    }
}