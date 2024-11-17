﻿using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json.Linq;

namespace MailDemon
{
    public class RecaptchaSettings
    {
        private static readonly string url = "https://www.google.com/recaptcha/api/siteverify?secret={0}&response={1}&remoteip={2}";
        private static readonly HttpClient client = new();

        /// <summary>
        /// Site key
        /// </summary>
        public string SiteKey { get; }

        /// <summary>
        /// Secret key
        /// </summary>
        public string SecretKey { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="siteKey">Site key</param>
        /// <param name="secretKey">Secret key</param>
        public RecaptchaSettings(string siteKey, string secretKey)
        {
            SiteKey = siteKey;
            SecretKey = secretKey;
        }

        /// <summary>
        /// Verify captcha from form
        /// </summary>
        /// <param name="url">Url</param>
        /// <param name="response">Response</param>
        /// <param name="action">Action</param>
        /// <param name="remoteip">Remote ip</param>
        /// <param name="formFields">Form fields</param>
        /// <param name="logger">Logger</param>
        /// <returns>String error or null if success in verify</returns>
        public async Task<string> Verify(string url, string response, string action, string remoteip,
            Dictionary<string, string> formFields, ILogger logger)
        {
            /* {
                "success": true|false,
                "challenge_ts": timestamp,  // timestamp of the challenge load (ISO format yyyy-MM-dd'T'HH:mm:ssZZ)
                "hostname": string,         // the hostname of the site where the reCAPTCHA was solved
                "error-codes": [...]        // optional
            } */

            response = (response ?? string.Empty);
            string jsonReceived = await client.GetStringAsync(string.Format(RecaptchaSettings.url, SecretKey, response, remoteip));
            JToken token = JToken.Parse(jsonReceived);
            string actualAction = token.Value<string>("action");
            bool success = token.Value<bool>("success");
            float? score = token.Value<float?>("score");
            if (success && actualAction == action && score != null && score.Value >= 0.5f)
            {
                return null;
            }
            StringBuilder errorCodes = new();
            JToken errors = token["error-codes"];
            if (errors != null)
            {
                foreach (JToken errorToken in token["error-codes"])
                {
                    errorCodes.Append(errorToken.Value<string>());
                    errorCodes.Append(',');
                }
                if (errorCodes.Length != 0)
                {
                    errorCodes.Length--;
                }
            }
            logger.LogWarning("Catpcha failed, url: {url}, success: {success}, score: {score}, action: {action} actual action: {actionAction}, error-codes: {errorCodes}, form: {form}",
                url, success, score, action, actualAction, errorCodes, DictionaryToString(formFields));
            return "Unknown Error";
        }

        private static string DictionaryToString<TKey, TValue>(IDictionary<TKey, TValue> dictionary)
        {
            return "{" + string.Join(",", dictionary.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + "}";
        }
    }
}
