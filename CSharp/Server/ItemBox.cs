using Barotrauma;
using HarmonyLib;
using System.Reflection;
using System.Linq;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System;
using Microsoft.Xna.Framework;
using System.ComponentModel;
using Barotrauma.Networking;

namespace BaroMod_sjx
{

	partial class ConditionStorage : ItemComponent, IServerSerializable
	{
		/*
		private CoroutineHandle? sendStateCoroutine;
		private int lastSentState;
		private float sendStateTimer;
		*/
		partial void OnCountPredictionChanged()
		{
			/*
			sendStateTimer = 0.5f;
			if (sendStateCoroutine == null)
			{
				sendStateCoroutine = CoroutineManager.StartCoroutine(SendStateAfterDelay());
			}*/
		}

		/*
		private IEnumerable<CoroutineStatus> SendStateAfterDelay()
		{
			while (sendStateTimer > 0.0f)
			{
				sendStateTimer -= CoroutineManager.DeltaTime;
				yield return CoroutineStatus.Running;
			}

			if (Item.Removed || GameMain.NetworkMember == null)
			{
				yield return CoroutineStatus.Success;
			}

			sendStateCoroutine = null;
			if (lastSentState != currentItemCount) { Item.CreateServerEvent(this); }
			yield return CoroutineStatus.Success;
		}*/

		public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null)
		{
			EventData eventData = ExtractEventData<EventData>(extraData);
			msg.WriteRangedInteger(eventData.ItemCount, 0, maxItemCount);
		}
	}
}