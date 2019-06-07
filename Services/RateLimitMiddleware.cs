using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace MailDemon
{
    /// <summary>
    /// Rate limit parameters
    /// </summary>
    public class RateLimitMiddlewareParameters
    {
        /// <summary>
        /// Max requests per minute if not authenticated
        /// </summary>
        public int MaxRequestsPerMinute { get; set; } = 30;

        /// <summary>
        /// Override for specific url/path regardless of private api key or not
        /// </summary>
        public Dictionary<string, int> RateLimitPaths { get; set; }

        /// <summary>
        /// Accept encoding header, null to not add
        /// </summary>
        public string VaryHeader { get; set; } = "Accept-Encoding";
    }

    /// <summary>
    /// Allow rate limit of requests
    /// </summary>
    public class RateLimitMiddleware
    {
        private class CacheEntry
        {
            public int Count;
        }

        private readonly RateLimitMiddlewareParameters parameters = new RateLimitMiddlewareParameters();
        private readonly RequestDelegate nextDelegate;
        private readonly IMemoryCache cache;
        private readonly TimeSpan expiration = TimeSpan.FromMinutes(1.0);

        private int GetMaxRequestsPerMinute(HttpContext context)
        {
            if (context == null || context.Request == null || context.User.Identity.IsAuthenticated)
            {
                context.Response.GetTypedHeaders().CacheControl =
                    new Microsoft.Net.Http.Headers.CacheControlHeaderValue()
                    {
                        NoCache = true,
                        NoStore = true
                    };
                return int.MaxValue;
            }
            else if (parameters.RateLimitPaths != null && parameters.RateLimitPaths.TryGetValue(context.Request.Path.Value, out int limit))
            {
                return limit;
            }
            return parameters.MaxRequestsPerMinute;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="nextDelegate">Next delegate</param>
        /// <param name="cache">Memory cache</param>
        /// <param name="parameters">Parameters</param>
        public RateLimitMiddleware(RequestDelegate nextDelegate, IMemoryCache cache, RateLimitMiddlewareParameters parameters = null)
        {
            this.nextDelegate = nextDelegate;
            this.cache = cache;
            this.parameters = parameters ?? this.parameters;
        }

        /// <summary>
        /// Invoke rate limit middleware
        /// </summary>
        /// <param name="context">Http context</param>
        /// <returns>Task</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            int maxRequestPerMinute = GetMaxRequestsPerMinute(context);
            if (parameters.VaryHeader != null)
            {
                context.Response.Headers["Vary"] = parameters.VaryHeader;
            }
            string key = "_RL_" + context.GetRemoteIPAddress().ToString();
            CacheEntry count = cache.GetOrCreate(key, (i) =>
            {
                i.AbsoluteExpirationRelativeToNow = expiration;
                i.Size = (key.Length * 2) + 16; // 12 bytes for C# object plus 4 bytes int
                return new CacheEntry();
            });
            int currentCount = Interlocked.Increment(ref count.Count);
            //context.Response.Headers["X-RateLimit-Remaining"] = (maxRequestPerMinute - currentCount).ToString(CultureInfo.InvariantCulture);
            //context.Response.Headers["X-RateLimit-Limit"] = maxRequestPerMinute.ToString(CultureInfo.InvariantCulture);
            if (currentCount > maxRequestPerMinute)
            {
                context.Response.StatusCode = 429; // unauthorized
                await context.Response.WriteAsync("Rate limit exceeded. Use back button on your browser.");
            }
            else
            {
                await this.nextDelegate(context);
            }
        }
    }
}
