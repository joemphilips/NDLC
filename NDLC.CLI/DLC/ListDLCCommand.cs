using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NDLC.Infrastructure;
using NDLC.Messages;

namespace NDLC.CLI.DLC
{
    public class ListDLCCommand : CommandBase
    {
        public static Command CreateCommand()
        {
			Command command = new Command("list", "List DLCs");
			command.Handler = new ListDLCCommand();
			return command;
        }
        protected override async Task InvokeAsyncBase(InvocationContext context)
        {
			var dlcs = await NameRepository.AsDLCNameRepository().ListDLCs();
			foreach (var (dlcName, dlcId) in dlcs.OrderBy(kv=> kv.Item1.ToString()))
			{
				var dState = (await Repository.GetDLC(dlcId));
				var eventFullName = await NameRepository.AsEventRepository().ResolveName(dState.OracleInfo);
				var e = await Repository.GetEvent(dState.OracleInfo);
				Debug.Assert(eventFullName != null);
				Debug.Assert(e != null);

				context.Console.Out.WriteLine($"{dlcName}. NextStep: ({dState.GetNextStep(Network)}). EventName: {eventFullName}. Outcomes: [{e.Outcomes.Aggregate((x, acc) => $"{x}, {acc}" )}]");
			}
        }
    }
}