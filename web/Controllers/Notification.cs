using Autodesk.Forge;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace forgeSample.Controllers
{
    public class Notification
    {
        private const string BASE_URL = "https://developer.api.autodesk.com";

        public static async Task CheckNewIssues()
        {
            string hubId = "b.56d422c4-b585-4458-999a-970208b40d78";
            string projectId = "b.014c6054-6da1-4ed8-b199-1975c07f608a";
            string folderId = "urn:adsk.wipprod:fs.folder:co.1X3m8NAlRL-xNDQsXk_bxQ";

            Credentials credentials = await Credentials.FromDatabaseAsync(Utils.GetAppSetting("USERID"));

            string containerId = await GetContainerAsync(credentials, hubId, projectId);
            dynamic issues = await GetIssuesAsync(credentials, containerId);

            foreach (dynamic issue in issues.data)
            {
                string comments = await GetCommnetsAsync(credentials, containerId, (string)issue.id);
                if (comments.IndexOf("SMS") != -1) continue;

                TwilioClient.Init(Utils.GetAppSetting("TWILIO_ACCOUNT_SID"), Utils.GetAppSetting("TWILIO_TOKEN"));

                var message = MessageResource.Create(
                    from: new Twilio.Types.PhoneNumber(Utils.GetAppSetting("TWILIO_FROM_NUMBER")),
                    body: "Foram feitas observacoes no seu projeto, acesse o site da Prefeitura de Sao Paulo para visualizar",
                    to: new Twilio.Types.PhoneNumber("+5511985742828")
                );

                await AddCommnetAsync(credentials, containerId, (string)issue.id, "SMS enviado");
            }
        }

        public static async Task<string> GetContainerAsync(Credentials credentials, string hubId, string projectId)
        {
            ProjectsApi projectsApi = new ProjectsApi();
            projectsApi.Configuration.AccessToken = credentials.TokenInternal;
            var project = await projectsApi.GetProjectAsync(hubId, projectId);
            var issues = project.data.relationships.issues.data;
            if (issues.type != "issueContainerId") return null;
            return issues["id"];
        }

        public static async Task<JObject> GetIssuesAsync(Credentials credentials, string containerId)
        {
            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/issues/v1/containers/{container_id}/quality-issues?filter[created_at]={minuteAgo}", RestSharp.Method.GET);
            request.AddParameter("container_id", containerId, ParameterType.UrlSegment);
            request.AddParameter("minuteAgo", DateTime.Now.AddMinutes(-90).ToString("o"), ParameterType.UrlSegment);
            request.AddHeader("Authorization", "Bearer " + credentials.TokenInternal);
            return JObject.Parse((await client.ExecuteTaskAsync<IRestResponse>(request)).Content);
        }

        public static async Task<string> GetCommnetsAsync(Credentials credentials, string containerId, string issueId)
        {
            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/issues/v1/containers/{container_id}/quality-issues/{issue_id}/comments", RestSharp.Method.GET);
            request.AddParameter("container_id", containerId, ParameterType.UrlSegment);
            request.AddParameter("issue_id", issueId, ParameterType.UrlSegment);
            request.AddHeader("Authorization", "Bearer " + credentials.TokenInternal);
            return (await client.ExecuteTaskAsync<IRestResponse>(request)).Content;
        }

        public static async Task<string> AddCommnetAsync(Credentials credentials, string containerId, string issueId, string comment)
        {
            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/issues/v1/containers/{container_id}/comments", RestSharp.Method.POST);
            request.AddParameter("container_id", containerId, ParameterType.UrlSegment);
            request.AddParameter("issue_id", issueId, ParameterType.UrlSegment);
            request.AddHeader("Content-Type", "application/vnd.api+json");
            request.AddParameter("text/json", Newtonsoft.Json.JsonConvert.SerializeObject(JObject.Parse(  "{'data': { 'type': 'comments', 'attributes': { 'issue_id': '" + issueId + "', 'body': '" + comment + "' } } }")) , ParameterType.RequestBody);
            request.AddHeader("Authorization", "Bearer " + credentials.TokenInternal);
            string ret = (await client.ExecuteTaskAsync<IRestResponse>(request)).Content;
            return ret;
        }
    }
}
