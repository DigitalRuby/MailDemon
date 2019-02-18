using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

using MimeKit;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

using Newtonsoft.Json;

namespace MailDemon.Controllers
{
    public class HomeController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Signup(string id)
        {
            string result = TempData["result"] as string;
            id = (id ?? string.Empty).Trim();
            SignUpModel model = (string.IsNullOrWhiteSpace(result) ? new SignUpModel() : JsonConvert.DeserializeObject<SignUpModel>(result));
            model.Id = id;
            model.Title = string.Format(MailDemonWebApp.SignUpTitle, id.Replace('_', ' ').Replace('-', ' '));
            return View(model);
        }

        [HttpPost]
        [ActionName("Signup")]
        public async Task<IActionResult> SignupPost(string id)
        {
            string captcha = HttpContext.Request.Form["captcha"];
            string error = await MailDemonWebApp.Recaptcha.Verify(captcha, "signup", HttpContext.GetRemoteIPAddress().ToString());
            SignUpModel model = new SignUpModel { Message = error, Error = !string.IsNullOrWhiteSpace(error) };
            model.Id = (id ?? string.Empty).Trim();
            foreach (KeyValuePair<string, StringValues> field in HttpContext.Request.Form)
            {
                if (field.Key.StartsWith("ff_"))
                {
                    string value = field.Value.ToString().Trim();
                    string name = field.Key.Split('_')[1];
                    model.Fields[name] = value;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        if (!field.Key.EndsWith("_optional"))
                        {
                            model.Message += "<br/>" + field.Key.Split('_')[1] + " is required";
                            model.Error = true;
                        }
                    }
                    else if (name.Contains("email", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!value.IsValidEmailAddress())
                        {
                            model.Message += "<br/>email is invalid";
                            model.Error = true;
                        }
                    }
                }
            }
            TempData["result"] = JsonConvert.SerializeObject(model);
            if (model.Error)
            {
                model.Title = string.Format(MailDemonWebApp.SignUpTitle, model.Id);
                return View(nameof(Signup), model);
            }
            else
            {
                // TODO: insert into database
                return RedirectToAction(nameof(SignupSuccess), new { model.Id });
            }
        }

        public IActionResult SignupSuccess(string id)
        {
            id = (id ?? string.Empty).Trim();
            string success = string.Format(MailDemonWebApp.SignUpSuccess.Replace("\n", "<br/>"), id);
            return View((object)success);
        }
    }
}