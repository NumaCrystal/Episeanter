using System;
using System.Collections.Generic;

namespace Episeanter.Content.Shared.Events
{
    public enum EventPriority
    {
        Lowest = 0,
        Low = 100,
        Normal = 200,
        High = 300,
        Highest = 400
    }
    
    public static class EventBus
    {
        private class Subscriber<T>
        {
            public Action<T> Callback;
            public EventPriority Priority;
            public int Order;
        }

        private static readonly Dictionary<Type, object> Subscribers = new();

        private static int _order;

        public static void Subscribe<T>(
            Action<T> callback,
            EventPriority priority = EventPriority.Normal) where T : IEvent
        {
            var type = typeof(T);

            if (!Subscribers.TryGetValue(type, out var value))
            {
                value = new List<Subscriber<T>>();
                Subscribers[type] = value;
            }

            var subscribers = (List<Subscriber<T>>)value;

            subscribers.Add(new Subscriber<T>
            {
                Callback = callback,
                Priority = priority,
                Order = _order++
            });

            subscribers.Sort((a, b) =>
            {
                int priorityCompare = b.Priority.CompareTo(a.Priority);

                return priorityCompare != 0
                    ? priorityCompare
                    : a.Order.CompareTo(b.Order);
            });
        }

        public static void Unsubscribe<T>(Action<T> callback) where T : IEvent
        {
            if (!Subscribers.TryGetValue(typeof(T), out var value))
                return;

            var subscribers = (List<Subscriber<T>>)value;

            subscribers.RemoveAll(x => x.Callback == callback);
        }

        public static void Publish<T>(T gameEvent) where T : IEvent
        {
            if (!Subscribers.TryGetValue(typeof(T), out var value))
                return;

            var subscribers = (List<Subscriber<T>>)value;

            // Snapshot защищает обход от изменений подписок
            var snapshot = subscribers.ToArray();

            foreach (var subscriber in snapshot)
            {
                subscriber.Callback?.Invoke(gameEvent);
            }
        }
    }
}