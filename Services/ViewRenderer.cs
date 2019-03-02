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
        Task<string> RenderToStringAsync<TModel>(string viewName, TModel model, ExpandoObject viewBag = null, bool isMainPage = false);
    }

    /// <summary>
    /// Razor view renderer that takes depenencies
    /// </summary>
    public class ViewRenderService : IDisposable, IViewRenderService, ITempDataProvider, IServiceProvider
    {
        private static readonly System.Net.IPAddress localIPAddress = System.Net.IPAddress.Parse("127.0.0.1");

        private readonly Dictionary<string, object> tempData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly IRazorViewEngine viewEngine;
        private readonly ITempDataProvider tempDataProvider;
        private readonly IServiceProvider serviceProvider;
        private readonly IHttpContextAccessor httpContextAccessor;

        public ViewRenderService(IRazorViewEngine viewEngine, IHttpContextAccessor httpContextAccessor, ITempDataProvider tempDataProvider, IServiceProvider serviceProvider)
        {
            this.viewEngine = viewEngine;
            this.httpContextAccessor = httpContextAccessor;
            this.tempDataProvider = tempDataProvider ?? this;
            this.serviceProvider = serviceProvider ?? this;
        }

        public void Dispose()
        {
            
        }

        /// <inheritdoc />
        public async Task<string> RenderToStringAsync<TModel>(string viewName, TModel model, ExpandoObject viewBag = null, bool isMainPage = false)
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
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            using (var sw = new StringWriter())
            {
                var viewResult = viewEngine.FindView(actionContext, viewName, isMainPage);

                // Fallback - the above seems to consistently return null when using the EmbeddedFileProvider
                if (viewResult.View == null)
                {
                    viewResult = viewEngine.GetView("~/", viewName, isMainPage);
                }

                if (viewResult.View == null)
                {
                    return null;
                }

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
                var viewContext = new ViewContext(
                    actionContext,
                    viewResult.View,
                    viewDictionary,
                    new TempDataDictionary(actionContext.HttpContext, tempDataProvider),
                    sw,
                    new HtmlHelperOptions()
                );
                
                await viewResult.View.RenderAsync(viewContext);
                return sw.ToString();
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
