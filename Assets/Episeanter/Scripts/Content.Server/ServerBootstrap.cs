using UnityEngine;
using Episeanter.Content.Server.Maps;
using Episeanter.Content.Shared.Network;

static class ServerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        if (!NetModules.Server.Contains(ServerGridModule.Instance))
            NetModules.Server.Add(ServerGridModule.Instance);
    }
}