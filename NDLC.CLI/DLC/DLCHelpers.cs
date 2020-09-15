﻿using NBitcoin;
using NBitcoin.DataEncoders;
using NDLC.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;

namespace NDLC.CLI.DLC
{
	public static class DLCHelpers
	{
		public static JObject ExportStateJObject(this DLCTransactionBuilder builder)
		{
			return JObject.Parse(builder.ExportState());
		}

		public static void WriteTransaction(this InvocationContext ctx, Transaction tx, Network network)
		{
			if (ctx.ParseResult.ValueForOption<bool>("psbt"))
			{
				var psbt = PSBT.FromTransaction(tx, network);
				for (int i = 0; i < tx.Inputs.Count; i++)
				{
					psbt.Inputs[i].FinalScriptSig = tx.Inputs[i].ScriptSig;
					psbt.Inputs[i].FinalScriptWitness = tx.Inputs[i].WitScript;
				}
				ctx.WritePSBT(psbt);
			}
			else
			{
				if (ctx.ParseResult.ValueForOption<bool>("json"))
				{
					ctx.Console.Out.Write(tx.ToString());
				}
				else
				{
					ctx.Console.Out.Write(tx.ToHex());
				}
			}
		}
		public static void WriteObject(this InvocationContext ctx, object obj, JsonSerializerSettings settings)
		{
			var json = ctx.ParseResult.ValueForOption<bool>("json");
			var txt = JsonConvert.SerializeObject(obj, settings);
			if (json)
				ctx.Console.Out.Write(txt);
			else
				ctx.Console.Out.Write(Encoders.Base64.EncodeData(UTF8Encoding.UTF8.GetBytes(txt)));
		}
		public static void WritePSBT(this InvocationContext ctx, PSBT psbt)
		{
			if (ctx.ParseResult.ValueForOption<bool>("json"))
			{
				ctx.Console.Out.Write(psbt.ToString());
			}
			else
			{
				ctx.Console.Out.Write(psbt.ToBase64());
			}
		}

		public static Offer GetOffer(this InvocationContext ctx, JsonSerializerSettings settings)
		{
			var offer = ctx.ParseResult.CommandResult.GetArgumentValueOrDefault<string>("offer");
			if (offer is null)
				throw new CommandOptionRequiredException("offer");
			try
			{
				var obj = JsonConvert.DeserializeObject<Offer>(UTF8Encoding.UTF8.GetString(Encoders.Base64.DecodeData(offer)), settings);
				if (obj is null)
					throw new CommandException("offer", "Invalid offer");
				return obj;
			}
			catch
			{
				throw new CommandException("offer", "Invalid offer");
			}
		}

		public static bool FillOutcomes(ContractInfo[] contractInfos, Repository.Event evt)
		{
			var unspecifiedOutcomes = evt.Outcomes.Select(e => new DiscreteOutcome(e)).ToHashSet();
			for (int i = 0; i < contractInfos.Length; i++)
			{
				if (!unspecifiedOutcomes.TryGetValue(contractInfos[i].Outcome, out var outcome) ||
					outcome?.OutcomeString is null)
					return false;
				unspecifiedOutcomes.Remove(outcome);
				contractInfos[i] = new ContractInfo(outcome, contractInfos[i].Payout);
			}
			if (unspecifiedOutcomes.Count != 0)
				return false;
			return true;
		}
	}
}
