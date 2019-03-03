#region Imports

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

#endregion Imports

namespace MailDemon
{
    /// <summary>
    /// View render service interface
    /// </summary>
    public interface IViewRenderService
    {
        /// <summary>
        /// Render a view to a string
        /// </summary>
        /// <typeparam name="TModel">Type of model</typeparam>
        /// <param name="viewName">View name or full path</param>
        /// <param name="model">Model</param>
        /// <param name="viewBag">View bag</param>
        /// <param name="isMainPage">Is main page (true) or partial page (false)</param>
        /// <returns>Rendered view or null if not found</returns>
        Task<string> RenderViewToStringAsync<TModel>(string viewName, TModel model, ExpandoObject viewBag = null, bool isMainPage = false);

        /// <summary>
        /// Render a string to a string
        /// </summary>
        /// <typeparam name="TModel">Type of model</typeparam>
        /// <param name="key">Key, should contain only valid file chars and be unique for the text</param>
        /// <param name="text">Template text</param>
        /// <param name="model">Model</param>
        /// <param name="viewBag">View bag</param>
        /// <param name="isMainPage">Is main page( true) or partial page (false)</param>
        /// <returns>Rendered view or null if text is null / empty</returns>
        Task<string> RenderStringToStringAsync<TModel>(string key, string text, TModel model, ExpandoObject viewBag = null, bool isMainPage = false);
    }

    /// <summary>
    /// Razor view renderer that takes depenencies
    /// </summary>
    public class ViewRenderService : IDisposable, IViewRenderService, ITempDataProvider, IServiceProvider
    {
        private static readonly System.Net.IPAddress localIPAddress = System.Net.IPAddress.Parse("127.0.0.1");

        private readonly string rootPath;
        private readonly Dictionary<string, object> tempData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly IRazorViewEngine viewEngine;
        private readonly ITempDataProvider tempDataProvider;
        private readonly IServiceProvider serviceProvider;
        private readonly IHttpContextAccessor httpContextAccessor;

        private ActionContext GetActionContext()
        {
            HttpContext httpContext;
            if (httpContextAccessor?.HttpContext != null)
            {
                httpContext = httpContextAccessor.HttpContext;
            }
            else
            {
                DefaultHttpContext defaultContext = new DefaultHttpContext { RequestServices = serviceProvider };
                defaultContext.Connection.RemoteIpAddress = localIPAddress;
                httpContext = defaultContext;
            }
            return new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        }

        private async Task<string> RenderViewAsync<TModel>(IView view, ActionContext actionContext, TModel model, ExpandoObject viewBag)
        {
            var viewDictionary = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = model
            };
            if (viewBag != null)
            {
                foreach (KeyValuePair<string, object> kv in (viewBag as IDictionary<string, object>))
                {
                    viewDictionary.Add(kv.Key, kv.Value);
                }
            }
            using (StringWriter sw = new StringWriter())
            {
                var viewContext = new ViewContext
                (
                    actionContext,
                    view,
                    viewDictionary,
                    new TempDataDictionary(actionContext.HttpContext, tempDataProvider),
                    sw,
                    new HtmlHelperOptions()
                );

                await view.RenderAsync(viewContext);
                return sw.ToString();
            }
        }

        public ViewRenderService(IRazorViewEngine viewEngine, IHttpContextAccessor httpContextAccessor, ITempDataProvider tempDataProvider, IServiceProvider serviceProvider) :
            this(Directory.GetCurrentDirectory(), viewEngine, httpContextAccessor, tempDataProvider, serviceProvider)
        {
        }

        public ViewRenderService(string rootPath, IRazorViewEngine viewEngine, IHttpContextAccessor httpContextAccessor, ITempDataProvider tempDataProvider, IServiceProvider serviceProvider)
        {
            this.rootPath = rootPath;
            this.viewEngine = viewEngine;
            this.httpContextAccessor = httpContextAccessor;
            this.tempDataProvider = tempDataProvider ?? this;
            this.serviceProvider = serviceProvider ?? this;
        }

        public void Dispose()
        {
            
        }

        /// <inheritdoc />
        public async Task<string> RenderViewToStringAsync<TModel>(string viewName, TModel model, ExpandoObject viewBag = null, bool isMainPage = false)
        {
            ActionContext actionContext = GetActionContext();
            var viewResult = viewEngine.FindView(actionContext, viewName, isMainPage);

            // Fallback - the above seems to consistently return null when using the EmbeddedFileProvider
            if (viewResult.View == null)
            {
                viewResult = viewEngine.GetView("~/", viewName, isMainPage);
            }

            return await RenderViewAsync(viewResult.View, actionContext, model, viewBag);
        }

        /// <inheritdoc />
        public async Task<string> RenderStringToStringAsync<TModel>(string key, string text, TModel model, ExpandoObject viewBag = null, bool isMainPage = false)
        {
            if (!key.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
            {
                key += ".cshtml";
            }
            string fileName = Path.Combine(rootPath, key);
            try
            {
                await File.WriteAllTextAsync(fileName, text);
                return await RenderViewToStringAsync(key, model, viewBag, isMainPage);
            }
            finally
            {
                File.Delete(fileName);
            }
        }

        object IServiceProvider.GetService(Type serviceType)
        {
            return null;
        }

        IDictionary<string, object> ITempDataProvider.LoadTempData(HttpContext context)
        {
            return tempData;
        }

        void ITempDataProvider.SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
