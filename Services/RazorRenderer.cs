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
            serviceProvider = services.BuildServiceProvider();
            viewRenderer = new ViewRenderService(serviceProvider.GetRequiredService<IRazorViewEngine>(), null, null, serviceProvider);
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
                options.FileProviders.Add(new MailDemonDatabaseFileProvider(rootPath));
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

        public Task<string> RenderToStringAsync<TModel>(string viewName, TModel model, ExpandoObject viewBag = null, bool isMainPage = false)
        {
            return viewRenderer.RenderToStringAsync(viewName, model, viewBag, isMainPage);
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
