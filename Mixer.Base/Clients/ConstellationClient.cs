﻿using Mixer.Base.Model.Client;
using Mixer.Base.Model.Constellation;
using Mixer.Base.Util;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace Mixer.Base.Clients
{
    public enum ConstellationEventTypeEnum
    {
        [Name("Announcements")]
        announcement__announce,

        [Name("Channel Followed")]
        channel__id__followed,
        [Name("Channel Hosted")]
        channel__id__hosted,
        [Name("Channel Unhosted")]
        channel__id__unhosted,
        [Name("Channel Status")]
        channel__id__status,
        [Name("Channel Subscribed")]
        channel__id__subscribed,
        [Name("Channel Resubscribed")]
        channel__id__resubscribed,
        [Name("Channel Resubscribed Shared")]
        channel__id__resubShared,
        [Name("Channel Updated")]
        channel__id__update,

        [Name("Interactive Connected")]
        interactive__id__connect,
        [Name("Interactive Disconnected")]
        interactive__id__disconnected,

        [Name("Team Deleted")]
        team__id__deleted,
        [Name("Team Member Accepted")]
        team__id__memberAccepted,
        [Name("Team Member Invited")]
        team__id__memberInvited,
        [Name("Team Member Removed")]
        team__id__memberRemoved,
        [Name("Team Owner Changed")]
        team__id__ownerChanged,

        [Name("User Achievement")]
        user__id__achievement,
        [Name("User Followed")]
        user__id__followed,
        [Name("User Notification")]
        user__id__notify,
        [Name("User Subscribed")]
        user__id__subscribed,
        [Name("User Resubscribed")]
        user__id__resubscribed,
        [Name("User Joined Team")]
        user__id__teamAccepted,
        [Name("User Invited To Team")]
        user__id__teamInvited,
        [Name("User Left Team")]
        user__id__teamRemoved,
        [Name("User Updated")]
        user__id__update,
    }

    public class ConstellationEventType : IEquatable<ConstellationEventType>
    {
        public ConstellationEventTypeEnum Type { get; set; }
        public uint ID { get; set; }

        public ConstellationEventType(ConstellationEventTypeEnum type, uint id = 0)
        {
            this.Type = type;
            this.ID = id;
        }

        public override string ToString()
        {
            return ConstellationClient.ConvertEventTypesToStrings(new List<ConstellationEventType>() { this }).First();
        }

        public override int GetHashCode() { return this.Type.GetHashCode() + this.ID.GetHashCode(); }

        public override bool Equals(object obj)
        {
            if (obj is ConstellationEventType)
            {
                return this.Equals((ConstellationEventType)obj);
            }
            return false;
        }

        public bool Equals(ConstellationEventType other)
        {
            return this.Type.Equals(other.Type) && this.ID.Equals(other.ID);
        }
    }

    public class ConstellationClient : WebSocketClientBase
    {
        internal static IEnumerable<string> ConvertEventTypesToStrings(IEnumerable<ConstellationEventType> eventTypes)
        {
            List<string> stringEventTypes = new List<string>();
            foreach (ConstellationEventType eventType in eventTypes)
            {
                string eventName = eventType.Type.ToString();
                eventName = eventName.Replace("__", ":");
                eventName = eventName.Replace(":id:", string.Format(":{0}:", eventType.ID));
                stringEventTypes.Add(eventName);
            }
            return stringEventTypes;
        }

        public event EventHandler<ConstellationLiveEventModel> OnSubscribedEventOccurred;

        public static async Task<ConstellationClient> Create(MixerConnection connection)
        {
            Validator.ValidateVariable(connection, "connection");

            AuthorizationToken authToken = await connection.GetAuthorizationToken();

            return new ConstellationClient(authToken);
        }

        private ConstellationClient(AuthorizationToken authToken)
        {
            Validator.ValidateVariable(authToken, "authToken");

            AuthenticationHeaderValue authHeader = new AuthenticationHeaderValue("Bearer", authToken.AccessToken);
            this.webSocket.Options.SetRequestHeader("Authorization", authHeader.ToString());
            this.webSocket.Options.SetRequestHeader("X-Is-Bot", true.ToString());
        }

        public async Task<bool> Connect()
        {
            this.OnDisconnectOccurred -= ConstellationClient_OnDisconnectOccurred;
            this.OnEventOccurred -= ConstellationClient_OnEventOccurred;

            this.OnEventOccurred += ConstellationClient_HelloMethodHandler;

            await this.ConnectInternal("wss://constellation.mixer.com");

            await this.WaitForResponse(() => { return this.connectSuccessful; });

            this.OnEventOccurred -= ConstellationClient_HelloMethodHandler;

            if (this.connectSuccessful)
            {
                this.OnDisconnectOccurred += ConstellationClient_OnDisconnectOccurred;
                this.OnEventOccurred += ConstellationClient_OnEventOccurred;
            }

            return this.connectSuccessful;
        }

        public async Task<bool> Ping()
        {
            MethodPacket packet = new MethodPacket() { method = "ping" };
            ReplyPacket reply = await this.SendAndListen(packet);
            return this.VerifyNoErrors(reply);
        }

        public async Task<bool> LiveSubscribe(IEnumerable<ConstellationEventType> events)
        {
            IEnumerable<string> eventStrings = ConstellationClient.ConvertEventTypesToStrings(events);

            MethodPacket packet = new MethodPacket() { method = "livesubscribe" };
            packet.parameters = new JObject();
            packet.parameters.Add("events", new JArray(eventStrings));

            ReplyPacket reply = await this.SendAndListen(packet);
            return this.VerifyNoErrors(reply);
        }

        public async Task<bool> LiveUnsubscribe(IEnumerable<ConstellationEventType> events)
        {
            IEnumerable<string> eventStrings = ConstellationClient.ConvertEventTypesToStrings(events);

            MethodPacket packet = new MethodPacket() { method = "liveunsubscribe" };
            packet.parameters = new JObject();
            packet.parameters.Add("events", new JArray(eventStrings));

            ReplyPacket reply = await this.SendAndListen(packet);
            return this.VerifyNoErrors(reply);
        }

        private void ConstellationClient_OnEventOccurred(object sender, EventPacket eventPacket)
        {
            switch (eventPacket.eventName)
            {
                case "live":
                    this.SendSpecificEvent<ConstellationLiveEventModel>(eventPacket, this.OnSubscribedEventOccurred);
                    break;
            }
        }

        private void ConstellationClient_HelloMethodHandler(object sender, EventPacket e)
        {
            JToken authenticationValue;
            if (e.eventName.Equals("hello") && e.data.TryGetValue("authenticated", out authenticationValue) && (bool)authenticationValue)
            {
                this.connectSuccessful = true;
                this.authenticateSuccessful = true;
            }
        }

        private async void ConstellationClient_OnDisconnectOccurred(object sender, WebSocketCloseStatus e)
        {
            this.connectSuccessful = false;
            this.authenticateSuccessful = false;
            await this.Connect();
        }
    }
}