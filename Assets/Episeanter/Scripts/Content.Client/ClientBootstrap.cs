using UnityEngine;
using Episeanter.Content.Client.Maps;
using Episeanter.Content.Shared.Network;

static class ClientBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        if (!NetModules.Client.Contains(ClientGridModule.Instance))
            NetModules.Client.Add(ClientGridModule.Instance);
    }
}