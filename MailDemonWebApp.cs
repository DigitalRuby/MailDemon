#region Imports

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

#endregion Imports

namespace MailDemon
{
    public class MailDemonWebApp : IStartup, IServiceProvider
    {
        private IWebHost host;
        private IServiceProvider serviceProvider;
        private readonly ManualResetEvent runningEvent = new ManualResetEvent(false);
        private readonly MailDemonService mailService;

        /// <summary>
        /// Configuration
        /// </summary>
        public IConfiguration Configuration { get; private set; }

        /// <summary>
        /// Root directory
        /// </summary>
        public string RootDirectory { get; private set; }

        /// <summary>
        /// Recaptcha settings
        /// </summary>
        public RecaptchaSettings Recaptcha { get; private set; }

        /// <summary>
        /// Admin login
        /// </summary>
        public KeyValuePair<string, string> AdminLogin;

        /// <summary>
        /// Command line args
        /// </summary>
        public string[] Args { get; set; }

        /// <summary>
        /// Cancel token
        /// </summary>
        public CancellationToken CancelToken { get; set; }

        /// <summary>
        /// Server url
        /// </summary>
        public string ServerUrl { get; private set; }

        /// <summary>
        /// Shared instance
        /// </summary>
        public static MailDemonWebApp Instance { get; private set; }

        private void OnShutdown()
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="args">Args</param>
        /// <param name="rootDirectory">Root directory</param>
        /// <param name="config">Configuration</param>
        /// <param name="mailService">Mail service</param>
        public MailDemonWebApp(string[] args, string rootDirectory, IConfigurationRoot config, MailDemonService mailService)
        {
            Args = args;
            RootDirectory = rootDirectory;
            Configuration = config;
            this.mailService = mailService;
            Instance = this;
        }

        /// <summary>
        /// IServiceProvider override
        /// </summary>
        /// <param name="serviceType">Service type</param>
        /// <returns>Object</returns>
        public object GetService(Type serviceType)
        {
            return serviceProvider.GetService(serviceType);
        }

        /// <summary>
        /// Run the application
        /// </summary>
        /// <param name="cancelToken">Cancel token</param>
        /// <returns>Task</returns>
        public async virtual Task StartAsync(CancellationToken cancelToken)
        {
            IWebHostBuilder builder = WebHost.CreateDefaultBuilder(Args);
            builder.ConfigureLogging(logging =>
            {

#if !DEBUG

                logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);

#endif

            });
            Dictionary<string, string> argsDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Args.Length % 2 != 0)
            {
                throw new ArgumentException("Arg count must be even. Use a space in between parameter names and values");
            }
            for (int i = 0; i < Args.Length;)
            {
                argsDictionary[Args[i++]] = Args[i++].Trim();
            }
            builder.UseKestrel(c => c.AddServerHeader = false);
            builder.UseIISIntegration();
            builder.UseContentRoot(RootDirectory);
            builder.UseConfiguration(Configuration);
            argsDictionary.TryGetValue("--server.urls", out string serverUrl);
            IConfigurationSection web = Configuration.GetSection("mailDemonWeb");
            string certPathWeb = web["sslCertificateFile"];
            string certPathPrivateWeb = web["sslCertificatePrivateKeyFile"];
            SecureString certPasswordWeb = web["sslCertificatePassword"].ToSecureString();
            if (File.Exists(certPathWeb) && File.Exists(certPathPrivateWeb))
            {
                builder.ConfigureKestrel((opt) =>
                {
                    opt.Listen((serverUrl == null ? System.Net.IPAddress.Any : System.Net.IPAddress.Parse(serverUrl)), 443, listenOptions =>
                    {
                        listenOptions.UseHttps((sslOpt) =>
                        {
                            sslOpt.ServerCertificateSelector = (ctx, name) =>
                            {
                                return MailDemonExtensionMethods.LoadSslCertificate(certPathWeb, certPathPrivateWeb, certPasswordWeb);
                            };
                        });
                    });
                });
            }
            else if (serverUrl != null)
            {
                builder.UseUrls(serverUrl.Split(',', '|', ';'));
            }
            host = builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStartup>(this);
            }).Build();
            Recaptcha = new RecaptchaSettings(web["recaptchaSiteKey"], web["recaptchaSecretKey"]);
            AdminLogin = new KeyValuePair<string, string>(web["adminUser"], web["adminPassword"]);
            Task runTask = host.RunAsync(CancelToken);

            // do not return the task until we know we are running, for tests for example, we don't want requests coming
            // in until everything is setup and listening properly
            if (!runningEvent.WaitOne(10000)) // if 10 seconds and not running, fail
            {
                throw new ApplicationException("Failed to start app " + GetType().Name);
            }

            await runTask;
        }

        /// <summary>
        /// Get the local ip address
        /// </summary>
        /// <returns>Local ip address</returns>
        public static IPAddress GetLocalIPAddress()
        {
            try
            {
                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (IPAddress ip in host.AddressList)
                {
                    if (ip.IsIPv4MappedToIPv6)
                    {
                        return ip.MapToIPv4();
                    }
                    return ip;
                }
            }
            catch
            {

            }
            return null;
        }

        IServiceProvider IStartup.ConfigureServices(IServiceCollection services)
        {
            services.AddMemoryCache((o) =>
            {
                o.CompactionPercentage = 0.9;
                o.ExpirationScanFrequency = TimeSpan.FromMinutes(1.0);
                o.SizeLimit = 1024 * 1024 * 32; // 32 mb
            });
            bool enableWeb = bool.Parse(Configuration.GetSection("mailDemonWeb")["enable"]);
            if (enableWeb)
            {
                services.Configure<CookiePolicyOptions>(options =>
                {
                    options.CheckConsentNeeded = context => true;
                    options.MinimumSameSitePolicy = SameSiteMode.None;
                });
                services.Configure<CookieTempDataProviderOptions>(options =>
                {
                    options.Cookie.IsEssential = true;
                });
                services.Configure<GzipCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Optimal);
                services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
                {
                    o.AccessDeniedPath = "/";
                    o.LoginPath = "/MailDemonLogin";
                    o.LogoutPath = "/MailDemonLogin";
                    o.Cookie.HttpOnly = true;
                });
                services.AddResponseCompression(options => { options.EnableForHttps = true; });
                services.AddResponseCaching();
                services.AddHttpContextAccessor();
                services.AddSingleton<IMailSender>((provider) => mailService);
                services.AddSingleton<IBulkMailSender>(new BulkMailSender());
                services.AddSingleton<IViewRenderService, ViewRenderService>();
                services.AddSingleton<IServiceProvider>(this);
                services.AddTransient<IMailCreator, MailCreator>();
                services.AddTransient<MailDemonDatabase>();
                services.AddHostedService<SubscriptionCleanup>();
                services.AddMvc((options) =>
                {

                }).SetCompatibilityVersion(CompatibilityVersion.Latest).AddJsonOptions(options =>
                {
                    options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                });
                services.Configure<RazorViewEngineOptions>(opts =>
                {
                    opts.AllowRecompilingViewsOnFileChange = true;
                    opts.FileProviders.Add(new MailDemonDatabaseFileProvider(RootDirectory));
                });
                services.AddAntiforgery(options =>
                {
                    options.Cookie.Name = "ANTI_FORGERY_C";
                    options.FormFieldName = "ANTI_FORGERY_F";
                    options.HeaderName = "ANTI_FORGERY_H";
                    options.SuppressXFrameOptionsHeader = false;
                });
            }
            serviceProvider = services.BuildServiceProvider();
            return this;
        }

        void IStartup.Configure(IApplicationBuilder app)
        {
            IHostingEnvironment env = app.ApplicationServices.GetService<IHostingEnvironment>();
            ILoggerFactory loggerFactory = app.ApplicationServices.GetService<ILoggerFactory>();
            IApplicationLifetime lifetime = app.ApplicationServices.GetService<IApplicationLifetime>();
            loggerFactory.AddProvider(new MailDemonLogProvider());
            bool enableWeb = bool.Parse(Configuration.GetSection("mailDemonWeb")["enable"]);
            IServerAddressesFeature serverAddressesFeature = app.ServerFeatures.Get<IServerAddressesFeature>();
            string address = serverAddressesFeature?.Addresses.LastOrDefault();
            if (address == null)
            {
                ServerUrl = "http://" + GetLocalIPAddress() + ":52664";
            }
            else
            {
                ServerUrl = address;
            }

            if (enableWeb)
            {
                app.UseStatusCodePagesWithReExecute("/Error/{0}");
                if (env.IsDevelopment())
                {
                    app.UseDeveloperExceptionPage();
                }
                else
                {
                    app.UseExceptionHandler("/Error");
                }
                app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
                var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("en-US") };
                app.UseRequestLocalization(new RequestLocalizationOptions
                {
                    DefaultRequestCulture = new RequestCulture("en"),
                    SupportedCultures = supportedCultures,
                    SupportedUICultures = supportedCultures
                });
                app.UseStaticFiles();
                app.UseAuthentication();
                app.UseResponseCompression();
                app.UseResponseCaching();
                app.UseMiddleware<RateLimitMiddleware>();
                app.UseMvc(routes =>
                {
                    routes.MapRoute("home", "/{action=Index}/{id?}", new { controller = "Home" });
                });
                // confusingly and strangely, WTF Microsoft, the UseCookiePolicy must come AFTER app.UseMvc for TempData to work.
                app.UseCookiePolicy();
                app.Use(async (context, next) =>
                {
                    await next.Invoke();
                });
            }
            ServerUrl = ServerUrl.Trim('/', '?');
            lifetime.ApplicationStopping.Register(OnShutdown);
            MailDemonLog.Warn("Mail demon web service started");
            runningEvent.Set();
        }
    }
}
