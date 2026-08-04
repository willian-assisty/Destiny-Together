using System;
using System.Collections.Generic;

namespace DestinyTogether.Core
{
    /// <summary>
    /// Pub/sub tipado e sincrono para eventos de aplicacao (cena carregada, conexao perdida,
    /// selecao de UI). NUNCA para logica de jogo — logica de jogo trafega por PlayerCommand
    /// (sobe) e SimEvent (desce). Misturar os dois canais e como projetos assim viram sopa.
    /// </summary>
    public sealed class EventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            Type t = typeof(T);
            if (!_handlers.TryGetValue(t, out var list))
            {
                list = new List<Delegate>();
                _handlers[t] = list;
            }
            list.Add(handler);
            return new Subscription(this, t, handler);
        }

        public void Publish<T>(T evt)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list) || list.Count == 0) return;
            // Copia para tolerar unsubscribe durante o despacho.
            var snapshot = list.ToArray();
            foreach (var d in snapshot)
                ((Action<T>)d).Invoke(evt);
        }

        public void Clear() => _handlers.Clear();

        private void Unsubscribe(Type t, Delegate handler)
        {
            if (_handlers.TryGetValue(t, out var list))
                list.Remove(handler);
        }

        private sealed class Subscription : IDisposable
        {
            private EventBus _bus;
            private readonly Type _type;
            private readonly Delegate _handler;

            public Subscription(EventBus bus, Type type, Delegate handler)
            {
                _bus = bus;
                _type = type;
                _handler = handler;
            }

            public void Dispose()
            {
                _bus?.Unsubscribe(_type, _handler);
                _bus = null;
            }
        }
    }
}
