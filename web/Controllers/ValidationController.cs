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
using Amazon.S3;

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

        public class InputSubmit
        {
            public IFormFile fileToUpload { get; set; }
            public string projectNumber { get; set; }
        }


        private const int UPLOAD_CHUNK_SIZE = 5; // Mb

        [HttpPost]
        [Route("api/submit")]
        public async Task<IActionResult> SubmitProject([FromForm]InputSubmit input)
        {
            string projectId = "b.014c6054-6da1-4ed8-b199-1975c07f608a";
            string folderId = "urn:adsk.wipprod:fs.folder:co.1X3m8NAlRL-xNDQsXk_bxQ";

            Credentials credentials = await Credentials.FromDatabaseAsync(Utils.GetAppSetting("USERID"));
            dynamic token2lo = await OAuthController2L.GetInternalAsync();

            var fileSavePath = Path.Combine(_env.ContentRootPath, input.fileToUpload.FileName);
            using (var stream = new FileStream(fileSavePath, FileMode.Create))
                await input.fileToUpload.CopyToAsync(stream);

            // prepare storage
            ProjectsApi projectApi = new ProjectsApi();
            projectApi.Configuration.AccessToken = credentials.TokenInternal;
            StorageRelationshipsTargetData storageRelData = new StorageRelationshipsTargetData(StorageRelationshipsTargetData.TypeEnum.Folders, folderId);
            CreateStorageDataRelationshipsTarget storageTarget = new CreateStorageDataRelationshipsTarget(storageRelData);
            CreateStorageDataRelationships storageRel = new CreateStorageDataRelationships(storageTarget);
            BaseAttributesExtensionObject attributes = new BaseAttributesExtensionObject(string.Empty, string.Empty, new JsonApiLink(string.Empty), null);
            CreateStorageDataAttributes storageAtt = new CreateStorageDataAttributes(input.fileToUpload.FileName, attributes);
            CreateStorageData storageData = new CreateStorageData(CreateStorageData.TypeEnum.Objects, storageAtt, storageRel);
            CreateStorage storage = new CreateStorage(new JsonApiVersionJsonapi(JsonApiVersionJsonapi.VersionEnum._0), storageData);
            dynamic storageCreated = await projectApi.PostStorageAsync(projectId, storage);

            string[] storageIdParams = ((string)storageCreated.data.id).Split('/');
            string[] bucketKeyParams = storageIdParams[storageIdParams.Length - 2].Split(':');
            string bucketKey = bucketKeyParams[bucketKeyParams.Length - 1];
            string objectName = storageIdParams[storageIdParams.Length - 1];

            // upload the file/object, which will create a new object
            ObjectsApi objects = new ObjectsApi();
            objects.Configuration.AccessToken = credentials.TokenInternal;

            // get file size
            long fileSize = (new FileInfo(fileSavePath)).Length;

            // decide if upload direct or resumable (by chunks)
            if (fileSize > UPLOAD_CHUNK_SIZE * 1024 * 1024) // upload in chunks
            {
                long chunkSize = 2 * 1024 * 1024; // 2 Mb
                long numberOfChunks = (long)Math.Round((double)(fileSize / chunkSize)) + 1;

                long start = 0;
                chunkSize = (numberOfChunks > 1 ? chunkSize : fileSize);
                long end = chunkSize;
                string sessionId = Guid.NewGuid().ToString();

                // upload one chunk at a time
                using (BinaryReader reader = new BinaryReader(new FileStream(fileSavePath, FileMode.Open)))
                {
                    for (int chunkIndex = 0; chunkIndex < numberOfChunks; chunkIndex++)
                    {
                        string range = string.Format("bytes {0}-{1}/{2}", start, end, fileSize);

                        long numberOfBytes = chunkSize + 1;
                        byte[] fileBytes = new byte[numberOfBytes];
                        MemoryStream memoryStream = new MemoryStream(fileBytes);
                        reader.BaseStream.Seek((int)start, SeekOrigin.Begin);
                        int count = reader.Read(fileBytes, 0, (int)numberOfBytes);
                        memoryStream.Write(fileBytes, 0, (int)numberOfBytes);
                        memoryStream.Position = 0;

                        await objects.UploadChunkAsync(bucketKey, objectName, (int)numberOfBytes, range, sessionId, memoryStream);

                        start = end + 1;
                        chunkSize = ((start + chunkSize > fileSize) ? fileSize - start - 1 : chunkSize);
                        end = start + chunkSize;
                    }
                }
            }
            else // upload in a single call
            {
                using (StreamReader streamReader = new StreamReader(fileSavePath))
                {
                    await objects.UploadObjectAsync(bucketKey, objectName, (int)streamReader.BaseStream.Length, streamReader.BaseStream, "application/octet-stream");
                }
            }

            // cleanup
            string fileName = input.fileToUpload.FileName;
            System.IO.File.Delete(fileSavePath);

            // check if file already exists...
            FoldersApi folderApi = new FoldersApi();
            folderApi.Configuration.AccessToken = credentials.TokenInternal;
            var filesInFolder = await folderApi.GetFolderContentsAsync(projectId, folderId);
            string itemId = string.Empty;
            foreach (KeyValuePair<string, dynamic> item in new DynamicDictionaryItems(filesInFolder.data))
                if (item.Value.attributes.displayName == fileName)
                    itemId = item.Value.id; // this means a file with same name is already there, so we'll create a new version

            // now decide whether create a new item or new version
            if (string.IsNullOrWhiteSpace(itemId))
            {
                // create a new item
                BaseAttributesExtensionObject baseAttribute = new BaseAttributesExtensionObject(projectId.StartsWith("a.") ? "items:autodesk.core:File" : "items:autodesk.bim360:File", "1.0");
                CreateItemDataAttributes createItemAttributes = new CreateItemDataAttributes(fileName, baseAttribute);
                CreateItemDataRelationshipsTipData createItemRelationshipsTipData = new CreateItemDataRelationshipsTipData(CreateItemDataRelationshipsTipData.TypeEnum.Versions, CreateItemDataRelationshipsTipData.IdEnum._1);
                CreateItemDataRelationshipsTip createItemRelationshipsTip = new CreateItemDataRelationshipsTip(createItemRelationshipsTipData);
                StorageRelationshipsTargetData storageTargetData = new StorageRelationshipsTargetData(StorageRelationshipsTargetData.TypeEnum.Folders, folderId);
                CreateStorageDataRelationshipsTarget createStorageRelationshipTarget = new CreateStorageDataRelationshipsTarget(storageTargetData);
                CreateItemDataRelationships createItemDataRelationhips = new CreateItemDataRelationships(createItemRelationshipsTip, createStorageRelationshipTarget);
                CreateItemData createItemData = new CreateItemData(CreateItemData.TypeEnum.Items, createItemAttributes, createItemDataRelationhips);
                BaseAttributesExtensionObject baseAttExtensionObj = new BaseAttributesExtensionObject(projectId.StartsWith("a.") ? "versions:autodesk.core:File" : "versions:autodesk.bim360:File", "1.0");
                CreateStorageDataAttributes storageDataAtt = new CreateStorageDataAttributes(fileName, baseAttExtensionObj);
                CreateItemRelationshipsStorageData createItemRelationshipsStorageData = new CreateItemRelationshipsStorageData(CreateItemRelationshipsStorageData.TypeEnum.Objects, storageCreated.data.id);
                CreateItemRelationshipsStorage createItemRelationshipsStorage = new CreateItemRelationshipsStorage(createItemRelationshipsStorageData);
                CreateItemRelationships createItemRelationship = new CreateItemRelationships(createItemRelationshipsStorage);
                CreateItemIncluded includedVersion = new CreateItemIncluded(CreateItemIncluded.TypeEnum.Versions, CreateItemIncluded.IdEnum._1, storageDataAtt, createItemRelationship);
                CreateItem createItem = new CreateItem(new JsonApiVersionJsonapi(JsonApiVersionJsonapi.VersionEnum._0), createItemData, new List<CreateItemIncluded>() { includedVersion });

                ItemsApi itemsApi = new ItemsApi();
                itemsApi.Configuration.AccessToken = credentials.TokenInternal;
                var newItem = await itemsApi.PostItemAsync(projectId, createItem);
                return newItem;
            }
            else
            {
                // create a new version
                BaseAttributesExtensionObject attExtensionObj = new BaseAttributesExtensionObject(projectId.StartsWith("a.") ? "versions:autodesk.core:File" : "versions:autodesk.bim360:File", "1.0");
                CreateStorageDataAttributes storageDataAtt = new CreateStorageDataAttributes(fileName, attExtensionObj);
                CreateVersionDataRelationshipsItemData dataRelationshipsItemData = new CreateVersionDataRelationshipsItemData(CreateVersionDataRelationshipsItemData.TypeEnum.Items, itemId);
                CreateVersionDataRelationshipsItem dataRelationshipsItem = new CreateVersionDataRelationshipsItem(dataRelationshipsItemData);
                CreateItemRelationshipsStorageData itemRelationshipsStorageData = new CreateItemRelationshipsStorageData(CreateItemRelationshipsStorageData.TypeEnum.Objects, storageCreated.data.id);
                CreateItemRelationshipsStorage itemRelationshipsStorage = new CreateItemRelationshipsStorage(itemRelationshipsStorageData);
                CreateVersionDataRelationships dataRelationships = new CreateVersionDataRelationships(dataRelationshipsItem, itemRelationshipsStorage);
                CreateVersionData versionData = new CreateVersionData(CreateVersionData.TypeEnum.Versions, storageDataAtt, dataRelationships);
                CreateVersion newVersionData = new CreateVersion(new JsonApiVersionJsonapi(JsonApiVersionJsonapi.VersionEnum._0), versionData);

                VersionsApi versionsApis = new VersionsApi();
                versionsApis.Configuration.AccessToken = credentials.TokenInternal;
                dynamic newVersion = await versionsApis.PostVersionAsync(projectId, newVersionData);
                return newVersion;
            }

            return Ok();
        }

        [HttpPost]
        [Route("api/validation")]
        public async Task<IActionResult> StartValidation([FromForm]UploadFile input)
        {
            DesignAutomation4AutoCAD da4a = new DesignAutomation4AutoCAD();
            //await da4a.ClearAccount();

            // save the file on the server
            var filePath = Path.Combine(_env.ContentRootPath, input.fileToUpload.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create)) await input.fileToUpload.CopyToAsync(stream);

            string bucketKey = string.Format("{0}-validation", Utils.GetAppSetting("FORGE_CLIENT_ID").ToLower());
            string objectName = string.Format("{0}-{1}", DateTime.Now.ToString("yyyyMMddhhmmss"), Path.GetFileName(filePath));

            // upload to OSS
            await UploadToBucket(bucketKey, objectName, filePath);

            // translate for vieweing
            await TranslateObject(bucketKey, objectName, input.sessionId);

            // run validation
            await da4a.StartDWGValidation(bucketKey, objectName, _env.WebRootPath, input.sessionId);

            return Ok();
        }

        private async Task<bool> UploadToBucket(string bucketKey, string objectName, string filePath)
        {
            // get the bucket...
            dynamic oauth = await OAuthController2L.GetInternalAsync();
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
            dynamic oauth = await OAuthController2L.GetInternalAsync();

            // prepare the webhook callback
            DerivativeWebhooksApi webhook = new DerivativeWebhooksApi();
            webhook.Configuration.AccessToken = oauth.access_token;
            dynamic existingHooks = await webhook.GetHooksAsync(DerivativeWebhookEvent.ExtractionFinished);

            // get the callback from your settings (e.g. web.config)
            string callbackUlr = Utils.GetAppSetting("FORGE_WEBHOOK_URL") + "/api/forge/callback/modelderivative";

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
            string urn = Utils.Base64Encode(string.Format("urn:adsk.objects:os.object:{0}/{1}", bucketKey, objectName));
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

        [HttpPost]
        [Route("/api/forge/callback/designautomation/autocad/{connectionId}/{resultJson}")]
        public async Task<IActionResult> DesignAutomationCallback(string connectionId, string resultJson, [FromBody]JObject body)
        {
            var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(Utils.GetAppSetting("AWS_ACCESS_KEY"), Utils.GetAppSetting("AWS_SECRET_KEY"));
            IAmazonS3 client = new AmazonS3Client(awsCredentials, Amazon.RegionEndpoint.USWest2);

            if (!await client.DoesS3BucketExistAsync(Utils.S3BucketName)) return Ok();
            Uri downloadFromS3 = new Uri(client.GeneratePreSignedURL(Utils.S3BucketName, resultJson, DateTime.Now.AddMinutes(10), null));

            string resultJsonPath = Path.Combine(_env.WebRootPath, resultJson);
            var keys = await client.GetAllObjectKeysAsync(Utils.S3BucketName, null, null);
            if (!keys.Contains(resultJson)) return Ok(); // file is not there
            await client.DownloadToFilePathAsync(Utils.S3BucketName, resultJson, resultJsonPath, null);
            string contents = System.IO.File.ReadAllText(resultJsonPath);
            System.IO.File.Delete(resultJsonPath);
            //await client.DeleteObjectAsync(Utils.S3BucketName, resultJson);

            await ValidationHub.ValidationFinished(_hubContext, connectionId, resultJson, JObject.Parse(contents));
            return Ok();


        }
    }
}
