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
        private readonly MailDemonDatabase db;

        public HomeController(MailDemonDatabase db)
        {
            this.db = db;
        }

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
            if (id.Length == 0)
            {
                return NotFound();
            }
            SignUpModel model = (string.IsNullOrWhiteSpace(result) ? new SignUpModel() : JsonConvert.DeserializeObject<SignUpModel>(result));
            model.Id = id;
            model.Title = string.Format(MailDemonWebApp.SignUpTitle, id.Replace('_', ' ').Replace('-', ' '));
            return View(model);
        }

        [HttpPost]
        [ActionName("Signup")]
        public async Task<IActionResult> SignupPost(string id)
        {
            if (id.Length == 0)
            {
                return NotFound();
            }
            string captcha = HttpContext.Request.Form["captcha"];
            string error = await MailDemonWebApp.Recaptcha.Verify(captcha, "signup", HttpContext.GetRemoteIPAddress().ToString());
            SignUpModel model = new SignUpModel { Message = error, Error = !string.IsNullOrWhiteSpace(error) };
            string email = null;
            model.Id = (id ?? string.Empty).Trim();
            foreach (KeyValuePair<string, StringValues> field in HttpContext.Request.Form)
            {
                if (field.Key.StartsWith("ff_"))
                {
                    string value = field.Value.ToString().Trim();
                    string name = field.Key.Split('_')[1];
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        if (field.Key.EndsWith("_optional", StringComparison.OrdinalIgnoreCase))
                        {
                            model.Fields[name] = value;
                        }
                        else
                        {
                            model.Message += "<br/>" + field.Key.Split('_')[1] + " is required";
                            model.Error = true;
                        }
                    }
                    else if (name.Contains("email", StringComparison.OrdinalIgnoreCase))
                    {
                        if (value.TryParseEmailAddress(out _))
                        {
                            email = (email ?? value);
                        }
                        else
                        {
                            model.Error = true;
                        }
                    }
                    else
                    {
                        model.Fields[name] = value;
                    }
                }
            }
            TempData["result"] = JsonConvert.SerializeObject(model);
            if (model.Error || email == null)
            {
                if (email == null)
                {
                    model.Message += "<br/>email is invalid";
                }
                model.Title = string.Format(MailDemonWebApp.SignUpTitle, model.Id);
                return View(nameof(Signup), model);
            }
            else
            {
                string token = db.PreSubscribeToMailingList(model.Fields, email, model.Id, HttpContext.GetRemoteIPAddress().ToString());
                return RedirectToAction(nameof(SignupConfirm), new { id = model.Id });
            }
        }

        public IActionResult SignupConfirm(string id)
        {
            id = (id ?? string.Empty).Trim();
            string text;
            if (id.Length == 0)
            {
                return NotFound();
            }
            text = string.Format(MailDemonWebApp.SignUpConfirm.Replace("\n", "<br/>"), id);
            return View((object)text);
        }

        public IActionResult SignupSuccess(string id, string token)
        {
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }
            token = (token ?? string.Empty).Trim();
            if (db.ConfirmSubscribeToMailingList(id, token))
            {
                string success = string.Format(MailDemonWebApp.SignUpSuccess.Replace("\n", "<br/>"), id);
                return View((object)success);
            }
            else
            {
                string error = string.Format(MailDemonWebApp.SignUpError.Replace("\n", "<br/>"), id);
                return View((object)error);
            }
        }
    }
}