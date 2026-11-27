using Episeanter.Content.Shared.Events;
using UnityEngine;

namespace Episeanter.Content.Shared.Transforms;

public readonly struct GridTransformChangedEvent : IEvent
{
    public readonly GridTransform Source;

    public GridTransformChangedEvent(GridTransform source)
    {
        Source = source;
    }
}

public readonly struct GridTransformGridChangedEvent : IEvent
{
    public readonly GridTransform Source;
    public readonly Transform OldGrid;
    public readonly Transform NewGrid;

    public GridTransformGridChangedEvent(GridTransform source, Transform oldGrid, Transform newGrid)
     {
        Source = source;
        OldGrid = oldGrid;
        NewGrid = newGrid;
    }
}
