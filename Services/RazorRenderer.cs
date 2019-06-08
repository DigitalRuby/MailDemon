#region Imports

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;

#endregion Imports

namespace MailDemon
{
    /// <summary>
    /// Renders razor pages with the absolute minimum setup of MVC, easy to use in console application, does not require any other classes or setup.
    /// </summary>
    public class RazorRenderer : IViewRenderService, ILoggerFactory, ILogger
    {
        private readonly string rootPath;
        private readonly ServiceCollection services;
        private readonly ServiceProvider serviceProvider;
        private readonly ViewRenderService viewRenderer;

        public RazorRenderer(string rootPath)
        {
            this.rootPath = rootPath;
            services = new ServiceCollection();
            ConfigureDefaultServices(services);
            Microsoft.EntityFrameworkCore.DbContextOptions<MailDemonDatabase> dbOptions = MailDemonDatabaseSetup.ConfigureDB(null);
            services.AddTransient<MailDemonDatabase>((provider) => new MailDemonDatabase(dbOptions));
            serviceProvider = services.BuildServiceProvider();
            viewRenderer = new ViewRenderService(rootPath, serviceProvider.GetRequiredService<IRazorViewEngine>(), null, null, serviceProvider);
        }

        private void ConfigureDefaultServices(IServiceCollection services)
        {
            var environment = new HostingEnvironment
            {
                WebRootFileProvider = new PhysicalFileProvider(rootPath),
                ApplicationName = typeof(RazorRenderer).Assembly.GetName().Name,
                ContentRootPath = rootPath,
                WebRootPath = rootPath,
                EnvironmentName = "DEVELOPMENT",
                ContentRootFileProvider = new PhysicalFileProvider(rootPath)
            };
            services.AddSingleton<IHostingEnvironment>(environment);
            services.Configure<RazorViewEngineOptions>(options =>
            {
                options.FileProviders.Clear();
                options.FileProviders.Add(new MailDemonDatabaseFileProvider(serviceProvider, rootPath));
            });
            services.AddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();
            services.AddSingleton<ILoggerFactory>(this);
            var diagnosticSource = new DiagnosticListener(environment.ApplicationName);
            services.AddSingleton<DiagnosticSource>(diagnosticSource);
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
            throw new NotImplementedException();
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
