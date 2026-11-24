using System.Collections.Generic;

namespace Episeanter.Content.Shared.Network {
    public interface INetModule
    {
        void Start();
        void Stop();
    }

    public static class NetModules
    {
        public static readonly List<INetModule> Server = new();
        public static readonly List<INetModule> Client = new();
    }
}