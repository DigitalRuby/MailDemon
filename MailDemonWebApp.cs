﻿#region Imports

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
using Microsoft.AspNetCore.Antiforgery;
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
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;

#endregion Imports

namespace MailDemon
{
    public class MailDemonWebApp : IStartup, IServiceProvider, IAuthority, IMailDemonDatabaseProvider, IDisposable
    {
        private IWebHost host;
        private IServiceProvider serviceProvider;
        private readonly ManualResetEvent runningEvent = new ManualResetEvent(false);

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
        /// Authority (base url)
        /// </summary>
        public string Authority { get; private set; }

        /// <summary>
        /// Mail service
        /// </summary>
        public MailDemonService MailService { get; set; }

        /// <summary>
        /// Logger
        /// </summary>
        public ILogger Logger { get; private set; }

        /// <summary>
        /// Shared instance
        /// </summary>
        public static MailDemonWebApp Instance { get; private set; }

        private void OnShutdown()
        {
            
        }

        private void InitializeDB(IApplicationBuilder app)
        {
            using var db = new MailDemonDatabase(Configuration);
            db.Initialize();

            // migrate away from litedb
            string migrationPath = Path.Combine(Directory.GetCurrentDirectory(), "MailDemon.db");
            if (File.Exists(migrationPath))
            {
                Logger.LogWarning("Migrating from old database {path}", migrationPath);
                var tran = db.Database.BeginTransaction();
                try
                {
                    using (FileStream fs = File.OpenRead(migrationPath))
                    using (LiteDB.LiteDatabase oldDb = new(fs))
                    {
                        foreach (MailList list in oldDb.GetCollection<MailList>().FindAll())
                        {
                            db.Lists.Add(list);
                        }
                        foreach (MailTemplate template in oldDb.GetCollection<MailTemplate>().FindAll())
                        {
                            db.Templates.Add(template);
                        }
                        foreach (MailListSubscription sub in oldDb.GetCollection<MailListSubscription>().FindAll())
                        {
                            db.Subscriptions.Add(sub);
                        }
                        db.SaveChanges();
                        tran.Commit();
                        tran = null;
                        Logger.LogWarning("Migration success");
                    }
                    File.Delete(migrationPath);
                }
                finally
                {
                    tran?.Rollback();
                }
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="args">Args</param>
        /// <param name="rootDirectory">Root directory</param>
        /// <param name="config">Configuration</param>
        public MailDemonWebApp(string[] args, string rootDirectory, IConfigurationRoot config)
        {
            Args = args;
            RootDirectory = rootDirectory;
            Configuration = config;
            Instance = this;
        }

        /// <summary>
        /// Dispose of the web app
        /// </summary>
        public void Dispose()
        {
            host?.Dispose();
            host = null;
        }

        /// <summary>
        /// Run the application
        /// </summary>
        /// <param name="cancelToken">Cancel token</param>
        /// <returns>Task</returns>
        public async virtual Task StartAsync(CancellationToken cancelToken)
        {
            IWebHostBuilder builder = WebHost.CreateDefaultBuilder(Args);
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

            // configure logging to console
            builder.ConfigureLogging((context, logging) =>
            {
                logging.AddConfiguration(context.Configuration.GetSection("Logging"));
                logging.AddConsole();
            });

            argsDictionary.TryGetValue("--server.urls", out string serverUrl);
            IConfigurationSection web = Configuration.GetSection("mailDemonWeb");
            Authority = web["authority"];
            string certPathWeb = web["sslCertificateFile"];
            string certPathPrivateWeb = web["sslCertificatePrivateKeyFile"];
            SecureString certPasswordWeb = web["sslCertificatePassword"]?.ToSecureString();
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
                                return CertificateCache.Instance.LoadSslCertificateAsync(certPathWeb, certPathPrivateWeb,
                                    certPasswordWeb, Logger).Sync();
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
            Logger = host.Services.GetRequiredService<ILogger<MailDemonWebApp>>();
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
                    o.AccessDeniedPath = "/MailDemonLogin";
                    o.LoginPath = "/MailDemonLogin";
                    o.LogoutPath = "/MailDemonLogin";
                    o.Cookie.HttpOnly = true;
                });
                services.AddResponseCompression(options => { options.EnableForHttps = true; });
                services.AddResponseCaching();
                services.AddHttpContextAccessor();
                services.AddSingleton<IMailSender>((provider) => MailService);
                services.AddSingleton<IViewRenderService, ViewRenderService>();
                services.AddSingleton<IAuthority>(this);
                services.AddSingleton<IMailDemonDatabaseProvider>(this);
                services.AddSingleton<BulkMailSender>();
                services.AddSingleton<IBulkMailSender>(provider => provider.GetRequiredService<BulkMailSender>());
                services.AddTransient<IMailCreator, MailCreator>();
                services.AddHostedService<SubscriptionCleanup>();
                services.AddDataProtection().SetApplicationName(GetType().Name).PersistKeysToFileSystem(new System.IO.DirectoryInfo(Directory.GetCurrentDirectory()));
                services.AddMvc(options =>
                {
                    options.EnableEndpointRouting = true;
                });
                services.AddRazorPages().AddRazorRuntimeCompilation(options =>
                {
                    IMemoryCache memoryCache = serviceProvider.GetRequiredService<IMemoryCache>();
                    options.FileProviders.Clear();
                    options.FileProviders.Add(new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot")));
                    options.FileProviders.Add(new MailDemonDatabaseFileProvider(this, memoryCache, AppContext.BaseDirectory));
                });
                services.AddAntiforgery(options =>
                {
                    options.Cookie.Name = "ANTI_FORGERY_C";
                    options.FormFieldName = "ANTI_FORGERY_F";
                    options.HeaderName = "ANTI_FORGERY_H";
                    options.SuppressXFrameOptionsHeader = false;
                });
            }
            return (serviceProvider = services.BuildServiceProvider());
        }

        void IStartup.Configure(IApplicationBuilder app)
        {
            IWebHostEnvironment env = app.ApplicationServices.GetService<IWebHostEnvironment>();
            IHostApplicationLifetime lifetime = app.ApplicationServices.GetService<IHostApplicationLifetime>();
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
                InitializeDB(app);
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
                app.UseStaticFiles(new StaticFileOptions
                {
                    OnPrepareResponse = (ctx) =>
                    {
                        ctx.Context.Response.GetTypedHeaders().CacheControl =
                        new Microsoft.Net.Http.Headers.CacheControlHeaderValue()
                        {
                            Public = true,
                            MaxAge = TimeSpan.FromDays(7.0)
                        };
                        ctx.Context.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.Vary] =
                            new string[] { "Accept-Encoding" };
                    }
                });
                app.UseCookiePolicy(new CookiePolicyOptions
                {
                });
                app.UseResponseCompression();
                app.UseResponseCaching();
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseMiddleware<RateLimitMiddleware>();
                app.UseEndpoints(routes =>
                {
                    routes.MapControllers();
                    routes.MapRazorPages();
                    routes.MapControllerRoute("home", "/{action=Index}/{id?}", new { controller = "Home" });
                });
            }
            ServerUrl = ServerUrl.Trim('/', '?');
            if (string.IsNullOrWhiteSpace(Authority))
            {
                Authority = ServerUrl;
            }
            else
            {
                Authority = Authority.Trim('/', '?');
            }
            lifetime.ApplicationStopping.Register(OnShutdown);
            Logger.LogWarning("Mail demon web service started");
            runningEvent.Set();
        }

        /// <summary>
        /// IServiceProvider implementation
        /// </summary>
        /// <param name="serviceType">Service type</param>
        /// <returns>Found service</returns>
        object IServiceProvider.GetService(Type serviceType)
        {
            return serviceProvider.GetService(serviceType);
        }

        /// <summary>
        /// IMailDemonDatabaseProvider
        /// </summary>
        /// <param name="config">Configuration</param>
        /// <returns>MailDemonDatabase</returns>
        MailDemonDatabase IMailDemonDatabaseProvider.GetDatabase(IConfiguration config)
        {
            return new MailDemonDatabase(config ?? Configuration);
        }
    }
}
