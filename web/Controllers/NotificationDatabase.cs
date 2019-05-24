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

using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace forgeSample.Controllers
{
    public static class NotificationDB
    {
        private static MongoClient _client = null;
        private static IMongoDatabase _database = null;

        private static string OAuthDatabase { get { return Utils.GetAppSetting("OAUTH_DATABASE"); } }

        private static MongoClient Client
        {
            get
            {
                if (_client == null) _client = new MongoClient(OAuthDatabase);
                return _client;
            }
        }


        private static IMongoDatabase Database
        {
            get
            {
                if (_database == null) _database = Client.GetDatabase(OAuthDatabase.Split('/').Last().Split('?').First());
                return _database;
            }
        }

        public async static Task<bool> Register(string itemId, string phoneNumber)
        {
            var users = Database.GetCollection<BsonDocument>("notifications");
            if (await IsRegistered(itemId))
            {
                var filterBuilder = Builders<BsonDocument>.Filter;
                var filter = filterBuilder.Eq("_id", itemId);
                var update = Builders<BsonDocument>.Update.AddToSet("phone", phoneNumber);

                try { var result = await users.UpdateOneAsync(filter, update); }
                catch (Exception e) { Console.WriteLine(e); return false; }
            }
            else
            {
                JArray phoneNumbers = new JArray();
                phoneNumbers.Add(phoneNumber);
                dynamic doc = new JObject();
                doc.phone = phoneNumbers;               
                var document = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(doc.ToString()); 
                document["_id"] = itemId;
                try { await users.InsertOneAsync(document); }
                catch (Exception e) { Console.WriteLine(e); return false; }
            }

            return true;
        }

        public static async Task<bool> IsRegistered(string itemId)
        {
            var filterBuilder = Builders<BsonDocument>.Filter;
            var filter = filterBuilder.Eq("_id", itemId);
            var users = Database.GetCollection<BsonDocument>("notifications");
            try { long count = await users.CountAsync(filter); return (count == 1); }
            catch (Exception e) { Console.WriteLine(e); return false; }
        }

        public static async Task<BsonDocument> GetPhones(string itemId)
        {
            var filterBuilder = Builders<BsonDocument>.Filter;
            var filter = filterBuilder.Eq("_id", itemId);
            var users = Database.GetCollection<BsonDocument>("notifications");
            try
            {
                var docs = await users.FindAsync(filter);
                var doc = docs.First();
                return doc;
            }
            catch (Exception e) { Console.WriteLine(e); return null; }
        }
    }
}