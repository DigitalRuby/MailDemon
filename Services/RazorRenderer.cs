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
using Microsoft.Extensions.ObjectPool;

namespace MailDemon
{
    public class RazorRenderer : IViewRenderService
    {
        private readonly ServiceCollection services;
        private readonly ServiceProvider serviceProvider;
        private readonly ViewRenderService viewRenderer;

        public RazorRenderer()
        {
            // Initialize the necessary services
            services = new ServiceCollection();
            ConfigureDefaultServices(services);
            serviceProvider = services.BuildServiceProvider();
            viewRenderer = new ViewRenderService(serviceProvider.GetRequiredService<IRazorViewEngine>(), null, null, serviceProvider);
        }

        private static void ConfigureDefaultServices(IServiceCollection services)
        {
            string rootPath = Directory.GetCurrentDirectory();
            var environment = new HostingEnvironment
            {
                WebRootFileProvider = new PhysicalFileProvider(rootPath),
                ApplicationName = "RazorRenderer"
            };
            services.AddSingleton<IHostingEnvironment>(environment);
            services.Configure<RazorViewEngineOptions>(options =>
            {
                options.FileProviders.Clear();
                options.FileProviders.Add(new MailDemonDatabaseFileProvider(rootPath));
            });
            services.AddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();
            var diagnosticSource = new DiagnosticListener("Microsoft.AspNetCore");
            services.AddSingleton<DiagnosticSource>(diagnosticSource);
            services.AddMvc();
        }

        public Task<string> RenderToStringAsync<TModel>(string viewName, TModel model, ExpandoObject viewBag = null, bool isMainPage = false)
        {
            return viewRenderer.RenderToStringAsync(viewName, model, viewBag, isMainPage);
        }
    }
}
