#region Imports

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;

#endregion Imports

namespace MailDemon
{
    /// <summary>
    /// Renders razor pages with the absolute minimum setup of MVC, easy to use in console application, does not require any other classes or setup.
    /// </summary>
    public class RazorRenderer : IViewRenderService, ILoggerFactory, ILogger, IWebHostEnvironment
    {
        private readonly string rootPath;
        private readonly Assembly entryAssembly;
        private readonly ServiceCollection services;
        private readonly ServiceProvider serviceProvider;
        private readonly ViewRenderService viewRenderer;

        IFileProvider IWebHostEnvironment.WebRootFileProvider { get; set; }
        string IWebHostEnvironment.WebRootPath { get; set; }
        string IHostEnvironment.ApplicationName { get; set; }
        IFileProvider IHostEnvironment.ContentRootFileProvider { get; set; }
        string IHostEnvironment.ContentRootPath { get; set; }
        string IHostEnvironment.EnvironmentName { get; set; }

        public RazorRenderer(string rootPath, Assembly entryAssembly)
        {
            this.rootPath = rootPath;
            this.entryAssembly = entryAssembly ?? Assembly.GetExecutingAssembly();
            services = new ServiceCollection();
            ConfigureDefaultServices(services);
            Microsoft.EntityFrameworkCore.DbContextOptions<MailDemonDatabase> dbOptions = MailDemonDatabaseSetup.ConfigureDB(null);
            services.AddTransient<MailDemonDatabase>((provider) => new MailDemonDatabase(dbOptions));
            serviceProvider = services.BuildServiceProvider();
            viewRenderer = new ViewRenderService(rootPath, serviceProvider.GetRequiredService<IRazorViewEngine>(), null, null, serviceProvider);
        }

        private void ConfigureDefaultServices(IServiceCollection services)
        {
            IWebHostEnvironment webEnv = this as IWebHostEnvironment;
            webEnv.EnvironmentName = "Production";
            webEnv.ApplicationName = entryAssembly.GetName().Name;
            webEnv.ContentRootPath = rootPath;
            webEnv.ContentRootFileProvider = new PhysicalFileProvider(webEnv.ContentRootPath);
            webEnv.WebRootFileProvider = webEnv.ContentRootFileProvider;
            webEnv.WebRootPath = webEnv.ContentRootPath;
            services.AddSingleton<IWebHostEnvironment>(this);
            services.AddSingleton<IHostEnvironment>(this);
            services.AddRazorPages().AddRazorRuntimeCompilation(options =>
            {
                options.FileProviders.Clear();
                options.FileProviders.Add(new MailDemonDatabaseFileProvider(serviceProvider, rootPath));
            });
            services.AddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();
            services.AddSingleton<ILoggerFactory>(this);
            var diagnosticSource = new DiagnosticListener(webEnv.ApplicationName);
            services.AddSingleton<DiagnosticSource>(diagnosticSource);
            services.AddSingleton(diagnosticSource);
            services.AddMvc();
        }

        public void Dispose()
        {
        }

        /// <inheritdoc />
        public Task<string> RenderViewToStringAsync<TModel>(string viewName, TModel model, ExpandoObject viewBag = null, bool isMainPage = true)
        {
            return viewRenderer.RenderViewToStringAsync(viewName, model, viewBag, isMainPage);
        }

        /// <inheritdoc />
        public Task<string> RenderStringToStringAsync<TModel>(string key, string text, TModel model, ExpandoObject viewBag = null, bool isMainPage = true)
        {
            return viewRenderer.RenderStringToStringAsync(key, text, model, viewBag, isMainPage);
        }

        void ILoggerFactory.AddProvider(ILoggerProvider provider)
        {
            
        }

        IDisposable ILogger.BeginScope<TState>(TState state)
        {
            return this;
        }

        ILogger ILoggerFactory.CreateLogger(string categoryName)
        {
            return this;
        }

        bool ILogger.IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
        {
            return false;
        }

        void ILogger.Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
        }
    }
}
