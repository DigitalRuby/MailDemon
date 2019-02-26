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

namespace MailDemon
{
    public class HomeController : Controller
    {
        private readonly MailDemonDatabase db;

        public bool RequireCaptcha { get; set;  } = true;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            db.Dispose();
        }

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
        public IActionResult Subscribe(string id)
        {
            string result = TempData["result"] as string;
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }
            SignUpModel model = (string.IsNullOrWhiteSpace(result) ? new SignUpModel() : JsonConvert.DeserializeObject<SignUpModel>(result));
            model.Id = id;
            model.Title = string.Empty;// string.Format(MailDemonWebApp.SubscribeTitle, id.Replace('_', ' ').Replace('-', ' '));
            return View(model);
        }

        [HttpPost]
        [ActionName("Signup")]
        public async Task<IActionResult> SubscribePost(string id, Dictionary<string, object> formFields)
        {
            if (id.Length == 0)
            {
                return NotFound();
            }
            string error = null;
            if (RequireCaptcha && formFields.TryGetValue("captcha", out object captchaValue))
            {
                error = await MailDemonWebApp.Recaptcha.Verify(captchaValue as string, "signup", HttpContext.GetRemoteIPAddress().ToString());
            }
            SignUpModel model = new SignUpModel { Message = error, Error = !string.IsNullOrWhiteSpace(error) };
            string email = null;
            model.Id = (id ?? string.Empty).Trim();
            foreach (KeyValuePair<string, object> field in formFields)
            {
                if (field.Key.StartsWith("ff_"))
                {
                    string value = field.Value?.ToString()?.Trim();
                    string name = field.Key.Split('_')[1];
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        if (field.Key.EndsWith("_optional", StringComparison.OrdinalIgnoreCase))
                        {
                            model.Fields[name] = string.Empty;
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
                    model.Message += "<br/>" + Resources.EmailIsInvalid;
                }
                model.Title = Resources.SubscribeTitle.FormatHtml(model.Id);
                return View(nameof(Subscribe), model);
            }
            else
            {
                string token = db.PreSubscribeToMailingList(model.Fields, email, model.Id, HttpContext.GetRemoteIPAddress().ToString());
                return RedirectToAction(nameof(SubscribeConfirm), new { id = model.Id });
            }
        }

        public IActionResult SubscribeConfirm(string id)
        {
            id = (id ?? string.Empty).Trim();
            string text;
            if (id.Length == 0)
            {
                return NotFound();
            }
            text = Resources.SubscribeConfirm.FormatHtml(id);
            return View((object)text);
        }

        public IActionResult SubscribeSuccess(string id, string token)
        {
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }
            token = (token ?? string.Empty).Trim();
            if (db.ConfirmSubscribeToMailingList(id, token))
            {
                string success = Resources.SubscribeSuccess.FormatHtml(id);
                return View((object)success);
            }
            string error = Resources.SubscribeError.FormatHtml(id);
            return View((object)error);
        }

        public IActionResult Unsubscribe(string id, string token)
        {
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }
            token = (token ?? string.Empty).Trim();
            if (db.UnsubscribeFromMailingList(id, token))
            {
                string success = Resources.UnsubscribeSuccess.FormatHtml(id);
                return View((object)success);
            }
            string error = Resources.UnsubscribeError.FormatHtml(id);
            return View((object)error);
        }

        public IActionResult DebugTemplate(string id)
        {
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }
            MailListRegistration tempReg = new MailListRegistration
            {
                EmailAddress = "test@domain.com",
                IPAddress = HttpContext.GetRemoteIPAddress().ToString(),
                Fields = new Dictionary<string, object> { { "firstName", "Bob" }, { "lastName", "Smith" }, { "company", "Fake Company" } },
                ListName = "Default",
                SubscribedDate = DateTime.UtcNow,
                SubscribeToken = Guid.NewGuid().ToString("N"),
                Expires = DateTime.MinValue
            };
            return View(id, tempReg);
        }
    }
}