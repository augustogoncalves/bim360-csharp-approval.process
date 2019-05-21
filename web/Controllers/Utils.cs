using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace forgeSample.Controllers
{
    public class Utils
    {
        public static string NickName
        {
            get
            {
                return GetAppSetting("FORGE_CLIENT_ID");
            }
        }

        public static string GetAppSetting(string settingKey)
        {
            return Environment.GetEnvironmentVariable(settingKey);
        }

        public static string S3BucketName
        {
            get
            {
                return "dwgvalidation" + NickName.ToLower();
            }
        }

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }
    }
}
