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
    public interface IViewRenderService
    {
        Task<string> RenderToStringAsync<TModel>(string viewName, TModel model, ExpandoObject viewBag = null, bool isMainPage = false);
    }

    public class ViewRenderService : IDisposable, IViewRenderService, ITempDataProvider, IServiceProvider
    {
        private static readonly System.Net.IPAddress localIPAddress = System.Net.IPAddress.Parse("127.0.0.1");

        private readonly Dictionary<string, object> tempData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly IRazorViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ViewRenderService(IRazorViewEngine viewEngine,
            IHttpContextAccessor httpContextAccessor,
            ITempDataProvider tempDataProvider,
            IServiceProvider serviceProvider)
        {
            _viewEngine = viewEngine;
            _httpContextAccessor = httpContextAccessor;
            _tempDataProvider = tempDataProvider ?? this;
            _serviceProvider = serviceProvider ?? this;
        }

        public void Dispose()
        {
            
        }

        public async Task<string> RenderToStringAsync<TModel>(string viewName, TModel model, ExpandoObject viewBag = null, bool isMainPage = false)
        {
            HttpContext httpContext;
            if (_httpContextAccessor?.HttpContext != null)
            {
                httpContext = _httpContextAccessor.HttpContext;
            }
            else
            {
                DefaultHttpContext defaultContext = new DefaultHttpContext { RequestServices = _serviceProvider };
                defaultContext.Connection.RemoteIpAddress = localIPAddress;
                httpContext = defaultContext;
            }
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            using (var sw = new StringWriter())
            {
                var viewResult = _viewEngine.FindView(actionContext, viewName, false);

                // Fallback - the above seems to consistently return null when using the EmbeddedFileProvider
                if (viewResult.View == null)
                {
                    viewResult = _viewEngine.GetView("~/", viewName, isMainPage);
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
                    new TempDataDictionary(actionContext.HttpContext, _tempDataProvider),
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
