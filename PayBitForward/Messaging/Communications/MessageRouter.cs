﻿using System;
using System.Collections.Generic;
using System.Net;

namespace PayBitForward.Messaging
{
    public delegate IConverser RequestNewConversation(Message mesg);

    public class MessageRouter
    { 
        public INetworkCommunicator Communicator { get; private set; }

        private Dictionary<Guid, IConverser> Conversers = new Dictionary<Guid, IConverser>();

        private Dictionary<Guid, IPEndPoint> EndPoints = new Dictionary<Guid, IPEndPoint>();

        private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(typeof(MessageRouter));

        public MessageRouter(INetworkCommunicator comm)
        {
            Log.Debug("Initializing message routing table");

            Communicator = comm;
            Communicator.OnEnvelope += HandleEnvelope;
        }

        public void AddConversation(IConverser converser, IPEndPoint endPoint)
        {
            Log.DebugFormat("Adding conversation with endpoint {0}:{1} and id {2}", endPoint.Address, endPoint.Port, converser.ConversationId);
            converser.OnMessageToSend += HandleMessage;
            converser.OnConversationOver += HandleConversationOver;

            Conversers.Add(converser.ConversationId, converser);
            EndPoints.Add(converser.ConversationId, endPoint);

            converser.Start();
        }

        private void HandleEnvelope(Envelope env)
        {
            if(!Conversers.ContainsKey(env.MessageContent.ConversationId))
            {
                Log.DebugFormat("Conversation with id {0} does not exist; requesting new conversation worker from app", env.MessageContent.ConversationId);
                var c = OnConversationRequest?.Invoke(env.MessageContent);

                // Make sure we ignore an invalid response
                if(c == null)
                {
                    return;
                }

                AddConversation(c, env.EndPoint);
            }

            // Log.Debug("Passing message to conversation worker to handle incoming message");
            Conversers[env.MessageContent.ConversationId].HandleMessage(env.MessageContent);
        }

        private void HandleMessage(IConverser converser, Message mesg)
        {
            // Log.Debug("Received a message to send from a conversation worker");

            if(EndPoints.ContainsKey(converser.ConversationId))
            {
                // Log.Debug("Passing message to communicator for transmission");
                var env = new Envelope(mesg, EndPoints[converser.ConversationId]);
                Communicator.SendEnvelope(env);
            }
        }

        private void HandleConversationOver(IConverser converser)
        {
            Log.DebugFormat("Conversation with id {0} signaled that it is finished; stopping conversation", converser.ConversationId);
            Conversers[converser.ConversationId].Stop();

            Conversers.Remove(converser.ConversationId);
            EndPoints.Remove(converser.ConversationId);
        }

        public event RequestNewConversation OnConversationRequest;
    }
}