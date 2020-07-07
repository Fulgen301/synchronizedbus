using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

using ProtoBuf;
using System.IO;
using System.Runtime.Remoting;

namespace SynchronizedBus
{
    public class SynchronizedBusSystem : ModSystem
    {
        public override bool AllowRuntimeReload => false;

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return true;
        }

        public override double ExecuteOrder()
        {
            return 0;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            SynchronizedBusExtensionMethods.StartClientSide(api);
            base.StartClientSide(api);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            SynchronizedBusExtensionMethods.StartServerSide(api);
            base.StartServerSide(api);
        }
    }

    public static class SynchronizedBusExtensionMethods
    {
        [ProtoContract]
        private class SynchronizedEvent
        {
            [ProtoMember(1)]
            public string Name { get; set; }

            [ProtoMember(2)]
            public byte[] Data { get; set; }

            [ProtoMember(3)]
            public string DataType { get; set; }
        }

        private static IClientNetworkChannel clientChannel;
        private static IServerNetworkChannel serverChannel;

        private static ICoreAPI api;

        /// <summary>
        /// Initializes the client side channel. Must be called at startup if SynchronizedBusSystem is disabled.
        /// </summary>
        /// <param name="capi">The Client API</param>
        public static void StartClientSide(ICoreClientAPI capi)
        {
            api = capi;

            if (clientChannel == null)
            {
                clientChannel = capi.Network.RegisterChannel("synchronizedbus")
                    .RegisterMessageType<SynchronizedEvent>()
                    .SetMessageHandler<SynchronizedEvent>(OnPacket);
            }
        }

        /// <summary>
        /// Initializes the server side channel. Must be called at startup if SynchronizedBusSystem is disabled.
        /// </summary>
        /// <param name="sapi">The Server API</param>
        public static void StartServerSide(ICoreServerAPI sapi)
        {
            api = sapi;

            if (serverChannel == null)
            {
                serverChannel = sapi.Network.RegisterChannel("synchronizedbus")
                    .RegisterMessageType<SynchronizedEvent>()
                    .SetMessageHandler<SynchronizedEvent>((_, packet) => OnPacket(packet));
            }
        }
        private static SynchronizedEvent ToSynchronizedEvent(string eventName, IAttribute data)
        {
            SynchronizedEvent synchronizedEvent = new SynchronizedEvent
            {
                Name = eventName
            };

            if (data != null)
            {
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    using (BinaryWriter binaryWriter = new BinaryWriter(memoryStream))
                    {
                        data.ToBytes(binaryWriter);
                    }

                    synchronizedEvent.Data = memoryStream.ToArray();
                }

                synchronizedEvent.DataType = data.GetType().AssemblyQualifiedName;
            }

            return synchronizedEvent;
        }

        /// <summary>
        /// If called on the client, pushes an event to the server-side event bus.
        /// If called on the server, pushes an event to the client-side event buses of all clients.
        /// </summary>
        /// <param name="eventAPI">The event API</param>
        /// <param name="eventName">The event's name</param>
        /// <param name="data">Optional event data</param>
        public static void PushRemoteEvent(this IEventAPI eventAPI, string eventName, IAttribute data = null)
        {
            SynchronizedEvent synchronizedEvent = ToSynchronizedEvent(eventName, data);

            if (eventAPI is IServerEventAPI)
            {
                serverChannel.BroadcastPacket(synchronizedEvent);
            }
            else
            {
                clientChannel.SendPacket(synchronizedEvent);
            }
        }

        /// <summary>
        /// If called on the client, pushes an event to both the local and the server-side event bus.
        /// If called on the server, pushes an event to both the local and the client-side event buses of all clients.
        /// </summary>
        /// <param name="eventAPI">The event API</param>
        /// <param name="eventName">The event's name</param>
        /// <param name="data">Optional event data</param>
        /// 
        public static void PushSynchronizedEvent(this IEventAPI eventAPI, string eventName, IAttribute data = null)
        {
            eventAPI.PushEvent(eventName, data);
            eventAPI.PushRemoteEvent(eventName, data);
        }

        /// <summary>
        /// Pushes an event to the client-side event buses of the specified players.
        /// </summary>
        /// <param name="eventAPI">The server-side event API</param>
        /// <param name="eventName">The event's name</param>
        /// <param name="data">Optional event data</param>
        /// <param name="players">The players the event should be sent to</param>
        public static void PushRemoteEventToPlayers(this IServerEventAPI eventAPI, string eventName, IAttribute data = null, params IServerPlayer[] players)
        {
            serverChannel.SendPacket(ToSynchronizedEvent(eventName, data), players);
        }

        /// <summary>
        /// Pushes an event to both the local and the client-side event buses of the specified players.
        /// </summary>
        /// <param name="eventAPI">The server-side event API</param>
        /// <param name="eventName">The event's name</param>
        /// <param name="data">Optional event data</param>
        /// <param name="players">The players the event should be sent to</param>
        public static void PushSynchronizedEventToPlayers(this IServerEventAPI eventAPI, string eventName, IAttribute data = null, params IServerPlayer[] players)
        {
            eventAPI.PushEvent(eventName, data);
            eventAPI.PushRemoteEventToPlayers(eventName, data, players);
        }

        private static void OnPacket(SynchronizedEvent synchronizedEvent)
        {
            IAttribute data = null;

            if (synchronizedEvent.Data != null)
            {
                data = (IAttribute) Activator.CreateInstance(Type.GetType(synchronizedEvent.DataType));

                using (MemoryStream memoryStream = new MemoryStream(synchronizedEvent.Data))
                {
                    using (BinaryReader binaryReader = new BinaryReader(memoryStream))
                    {
                        data.FromBytes(binaryReader);
                    }
                }
            }

            api.Event.PushEvent(synchronizedEvent.Name, data);
        }
    }
}
