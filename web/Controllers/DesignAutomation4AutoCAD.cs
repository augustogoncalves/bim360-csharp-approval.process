/////////////////////////////////////////////////////////////////////
// Copyright (c) Autodesk, Inc. All rights reserved
// Written by Forge Partner Development
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
/////////////////////////////////////////////////////////////////////

using Amazon.S3;
using Autodesk.Forge;
using Autodesk.Forge.Core;
using Autodesk.Forge.DesignAutomation;
using Autodesk.Forge.DesignAutomation.Model;
using Autodesk.Forge.Model;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Activity = Autodesk.Forge.DesignAutomation.Model.Activity;
using Alias = Autodesk.Forge.DesignAutomation.Model.Alias;
using AppBundle = Autodesk.Forge.DesignAutomation.Model.AppBundle;
using Parameter = Autodesk.Forge.DesignAutomation.Model.Parameter;
using WorkItem = Autodesk.Forge.DesignAutomation.Model.WorkItem;
using WorkItemStatus = Autodesk.Forge.DesignAutomation.Model.WorkItemStatus;

namespace forgeSample.Controllers
{
    public class DesignAutomation4AutoCAD
    {
        private const string APPNAME = "DWGValidation";
        private const string APPBUNBLENAME = "DWGValidation.zip";
        private const string ACTIVITY_NAME = "DWGValidation";
        private const string ENGINE_NAME = "Autodesk.AutoCAD+23";

        /// NickName.AppBundle+Alias
        private string AppBundleFullName { get { return string.Format("{0}.{1}+{2}", Utils.NickName, APPNAME, Alias); } }
        /// NickName.Activity+Alias
        private string ActivityFullName { get { return string.Format("{0}.{1}+{2}", Utils.NickName, ACTIVITY_NAME, Alias); } }
        /// Prefix for AppBundles and Activities
        public static string NickName { get { return Utils.GetAppSetting("FORGE_CLIENT_ID"); } }
        /// Alias for the app (e.g. DEV, STG, PROD). This value may come from an environment variable
        public static string Alias { get { return "dev"; } }
        // Design Automation v3 API
        private DesignAutomationClient _designAutomation;

        public DesignAutomation4AutoCAD()
        {
            // need to initialize manually as this class runs in background
            ForgeService service =
                new ForgeService(
                    new HttpClient(
                        new ForgeHandler(Microsoft.Extensions.Options.Options.Create(new ForgeConfiguration()
                        {
                            ClientId = Utils.GetAppSetting("FORGE_CLIENT_ID"),
                            ClientSecret = Utils.GetAppSetting("FORGE_CLIENT_SECRET")
                        }))
                        {
                            InnerHandler = new HttpClientHandler()
                        })
                );
            _designAutomation = new DesignAutomationClient(service);
        }

        public async Task EnsureAppBundle(string contentRootPath)
        {
            // get the list and check for the name
            Page<string> appBundles = await _designAutomation.GetAppBundlesAsync();
            bool existAppBundle = false;
            foreach (string appName in appBundles.Data)
            {
                if (appName.Contains(AppBundleFullName))
                {
                    existAppBundle = true;
                    continue;
                }
            }

            if (!existAppBundle)
            {
                // check if ZIP with bundle is here
                string packageZipPath = Path.Combine(contentRootPath + "/bundles/", APPBUNBLENAME);
                if (!File.Exists(packageZipPath)) throw new Exception("DWG Validation bundle not found at " + packageZipPath);

                AppBundle appBundleSpec = new AppBundle()
                {
                    Package = APPNAME,
                    Engine = ENGINE_NAME,
                    Id = APPNAME,
                    Description = string.Format("Description for {0}", APPBUNBLENAME),

                };
                AppBundle newAppVersion = await _designAutomation.CreateAppBundleAsync(appBundleSpec);
                if (newAppVersion == null) throw new Exception("Cannot create new app");

                // create alias pointing to v1
                Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
                Alias newAlias = await _designAutomation.CreateAppBundleAliasAsync(APPNAME, aliasSpec);

                // upload the zip with .bundle
                RestClient uploadClient = new RestClient(newAppVersion.UploadParameters.EndpointURL);
                RestRequest request = new RestRequest(string.Empty, Method.POST);
                request.AlwaysMultipartFormData = true;
                foreach (KeyValuePair<string, string> x in newAppVersion.UploadParameters.FormData) request.AddParameter(x.Key, x.Value);
                request.AddFile("file", packageZipPath);
                request.AddHeader("Cache-Control", "no-cache");
                await uploadClient.ExecuteTaskAsync(request);
            }
        }

        private async Task EnsureActivity()
        {
            Page<string> activities = await _designAutomation.GetActivitiesAsync();

            bool existActivity = false;
            foreach (string activity in activities.Data)
            {
                if (activity.Contains(ActivityFullName))
                {
                    existActivity = true;
                    continue;
                }
            }

            if (!existActivity)
            {
                // create activity
                string commandLine = string.Format(@"$(engine.path)\\accoreconsole.exe /i $(args[inputFile].path) /al $(appbundles[{0}].path) /s $(settings[script].path)", APPNAME);
                Activity activitySpec = new Activity()
                {
                    Id = ACTIVITY_NAME,
                    Appbundles = new List<string>() { AppBundleFullName },
                    CommandLine = new List<string>() { commandLine },
                    Engine = ENGINE_NAME,
                    Parameters = new Dictionary<string, Parameter>()
                    {
                        { "inputFile", new Parameter() { Description = "Input DWG File", LocalName = "$(inputFile)", Ondemand = false, Required = true, Verb = Verb.Get, Zip = false } },
                        { "results", new Parameter() { Description = "Output JSON Results", LocalName = "results.json", Ondemand = false, Required = true, Verb = Verb.Put, Zip = false } },
                    },
                    Settings = new Dictionary<string, ISetting>()
                    {
                        { "script", new StringSetting(){ Value = "RUNVALIDATION\n" } }
                    }
                };
                Activity newActivity = await _designAutomation.CreateActivityAsync(activitySpec);

                // specify the alias for this Activity
                Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
                Alias newAlias = await _designAutomation.CreateActivityAliasAsync(ACTIVITY_NAME, aliasSpec);
            }
        }

        private XrefTreeArgument BuildDownloadURL(string accessToken, string bucketKey, string objectName)
        {
            string downloadUrl = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", bucketKey, objectName);

            return new XrefTreeArgument()
            {
                Url = downloadUrl,
                Verb = Verb.Get,
                Headers = new Dictionary<string, string>()
                {
                    { "Authorization", "Bearer " + accessToken }
                }
            };
        }

        private async Task<XrefTreeArgument> BuildUploadURL(string resultFilename)
        {
            var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(Utils.GetAppSetting("AWS_ACCESS_KEY"), Utils.GetAppSetting("AWS_SECRET_KEY"));
            IAmazonS3 client = new AmazonS3Client(awsCredentials, Amazon.RegionEndpoint.USWest2);

            if (!await client.DoesS3BucketExistAsync(Utils.S3BucketName))
                await client.EnsureBucketExistsAsync(Utils.S3BucketName);

            Dictionary<string, object> props = new Dictionary<string, object>();
            props.Add("Verb", "PUT");
            Uri uploadToS3 = new Uri(client.GeneratePreSignedURL(Utils.S3BucketName, resultFilename, DateTime.Now.AddMinutes(10), props));

            return new XrefTreeArgument()
            {
                Url = uploadToS3.ToString(),
                Verb = Verb.Put
            };
        }

        public async Task ClearAccount()
        {
            // uncomment these lines to clear all appbundles & activities under your account
            await _designAutomation.DeleteForgeAppAsync("me");
            System.Threading.Thread.Sleep(5000);
        }

        public async Task StartDWGValidation(string bucketKey, string objectName, string contentRootPath, string connectionId)
        {
            // check Design Automation for Revit setup
            await EnsureAppBundle(contentRootPath);
            await EnsureActivity();

            string resultJson = objectName.Replace(".dwg", ".json");
            string callbackUrl = string.Format("{0}/api/forge/callback/designautomation/autocad/{1}/{2}", Utils.GetAppSetting("FORGE_WEBHOOK_URL"), connectionId, resultJson);

            WorkItem workItemSpec = new WorkItem()
            {
                ActivityId = ActivityFullName,
                Arguments = new Dictionary<string, IArgument>()
                    {
                        { "inputFile", BuildDownloadURL((await OAuthController.GetInternalAsync()).access_token, bucketKey, objectName) },
                        { "results", await BuildUploadURL(resultJson) },
                        { "onComplete", new XrefTreeArgument { Verb = Verb.Post, Url = callbackUrl } }
                    }
            };
            WorkItemStatus workItemStatus = await _designAutomation.CreateWorkItemAsync(workItemSpec);
        }
    }
}