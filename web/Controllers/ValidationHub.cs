using System;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace forgeSample.Controllers
{
    /// <summary>
    /// Class uses for SignalR
    /// </summary>
    public class ValidationHub : Microsoft.AspNetCore.SignalR.Hub
    {
        public string GetConnectionId() { return Context.ConnectionId; }

        public async static Task ValidationFinished(IHubContext<ValidationHub> context,string connectionId, string inputFile, JObject body)
        {
            await context.Clients.Client(connectionId).SendAsync("validationFinished", body);
        }

        public async static Task ExtractionFinished(IHubContext<ValidationHub> context, JObject body)
        {
            string connectionId = body["hook"]["scope"]["workflow"].Value<String>();
            await context.Clients.Client(connectionId).SendAsync("extractionFinished", body);
        }
    }
}