using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Autodesk.Forge;
using Autodesk.Forge.Model;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.SignalR;
using static Autodesk.Forge.Model.PostBucketsPayload;

namespace forgeSample.Controllers
{
    public class ValidationController : ControllerBase
    {

        private IHubContext<ValidationHub> _hubContext;
        private IHostingEnvironment _env;
        public ValidationController(IHostingEnvironment env, IHubContext<ValidationHub> hubContext)
        {
            _env = env;
            _hubContext = hubContext;
        }

        public class UploadFile
        {
            public string sessionId { get; set; }
            public IFormFile fileToUpload { get; set; }

        }

        [HttpPost]
        [Route("api/validation")]
        public async Task<IActionResult> StartValidation([FromForm]UploadFile input)
        {
            // save the file on the server
            var filePath = Path.Combine(_env.ContentRootPath, input.fileToUpload.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create)) await input.fileToUpload.CopyToAsync(stream);

            string bucketKey = string.Format("{0}-validation", OAuthController.GetAppSetting("FORGE_CLIENT_ID").ToLower());
            string objectName = string.Format("{0}-{1}", DateTime.Now.ToString("yyyyMMddhhmmss"), Path.GetFileName(filePath));

            // upload to OSS
            await UploadToBucket(bucketKey, objectName, filePath);

            // translate for vieweing
            await TranslateObject(bucketKey, objectName, input.sessionId);

            return Ok();
        }

        private async Task<bool> UploadToBucket(string bucketKey, string objectName, string filePath)
        {
            // get the bucket...
            dynamic oauth = await OAuthController.GetInternalAsync();
            ObjectsApi objects = new ObjectsApi();
            objects.Configuration.AccessToken = oauth.access_token;

            // upload file to OSS Bucket
            // 1. ensure bucket existis
            BucketsApi buckets = new BucketsApi();
            buckets.Configuration.AccessToken = oauth.access_token;
            try
            {
                PostBucketsPayload bucketPayload = new PostBucketsPayload(bucketKey, null, PostBucketsPayload.PolicyKeyEnum.Transient);
                await buckets.CreateBucketAsync(bucketPayload, "US");
            }
            catch { }; // in case bucket already exists

            // upload the file/object, which will create a new object

            dynamic uploadedObj;
            using (StreamReader streamReader = new StreamReader(filePath))
            {
                uploadedObj = await objects.UploadObjectAsync(
                    bucketKey,
                    objectName,
                    (int)streamReader.BaseStream.Length,
                    streamReader.BaseStream,
                    "application/octet-stream");
            }

            // cleanup
            System.IO.File.Delete(filePath);

            return true;
        }

        public async Task<dynamic> TranslateObject(string bucketKey, string objectName, string sessionId)
        {
            dynamic oauth = await OAuthController.GetInternalAsync();

            // prepare the webhook callback
            DerivativeWebhooksApi webhook = new DerivativeWebhooksApi();
            webhook.Configuration.AccessToken = oauth.access_token;
            dynamic existingHooks = await webhook.GetHooksAsync(DerivativeWebhookEvent.ExtractionFinished);

            // get the callback from your settings (e.g. web.config)
            string callbackUlr = OAuthController.GetAppSetting("FORGE_WEBHOOK_URL") + "/api/forge/callback/modelderivative";

            bool createHook = true; // need to create, we don't know if our hook is already there...
            foreach (KeyValuePair<string, dynamic> hook in new DynamicDictionaryItems(existingHooks.data))
            {
                if (hook.Value.scope.workflow.Equals(sessionId))
                {
                    // ok, found one hook with the same workflow, no need to create...
                    createHook = false;
                    if (!hook.Value.callbackUrl.Equals(callbackUlr))
                    {
                        await webhook.DeleteHookAsync(DerivativeWebhookEvent.ExtractionFinished, new System.Guid(hook.Value.hookId));
                        createHook = true; // ops, the callback URL is outdated, so delete and prepare to create again
                    }
                }
            }

            // need to (re)create the hook?
            if (createHook) await webhook.CreateHookAsync(DerivativeWebhookEvent.ExtractionFinished, callbackUlr, sessionId);

            // prepare the payload
            List<JobPayloadItem> outputs = new List<JobPayloadItem>()
            {
            new JobPayloadItem(
              JobPayloadItem.TypeEnum.Svf,
              new List<JobPayloadItem.ViewsEnum>()
              {
                JobPayloadItem.ViewsEnum._2d,
                JobPayloadItem.ViewsEnum._3d
              })
            };
            string urn = Base64Encode(string.Format("urn:adsk.objects:os.object:{0}/{1}", bucketKey, objectName));
            JobPayload job = new JobPayload(new JobPayloadInput(urn), new JobPayloadOutput(outputs), new JobPayloadMisc(sessionId));

            // start the translation
            DerivativesApi derivative = new DerivativesApi();
            derivative.Configuration.AccessToken = oauth.access_token;
            dynamic jobPosted = await derivative.TranslateAsync(job, true/* force re-translate if already here, required data:write*/);
            return jobPosted;
        }

        [HttpPost]
        [Route("/api/forge/callback/modelderivative")]
        public async Task<IActionResult> DerivativeCallback([FromBody]JObject body)
        {
            await ValidationHub.ExtractionFinished(_hubContext, body);
            return Ok();
        }

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }
    }
}
