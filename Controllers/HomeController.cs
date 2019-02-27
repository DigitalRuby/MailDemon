#region Imports

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using MimeKit;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

using Newtonsoft.Json;

#endregion Imports

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

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Subscribe(string id)
        {
            string result = TempData["result"] as string;
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }
            SubscribeModel model = (string.IsNullOrWhiteSpace(result) ? new SubscribeModel() : JsonConvert.DeserializeObject<SubscribeModel>(result));
            model.Id = id;
            model.Title = Resources.SubscribeTitle.FormatHtml(id);
            return View(model);
        }

        [HttpPost]
        [ActionName("Subscribe")]
        public async Task<IActionResult> SubscribePost(string id, Dictionary<string, object> formFields)
        {
            if (id.Length == 0)
            {
                return NotFound();
            }
            string error = null;
            if (RequireCaptcha && formFields.TryGetValue("captcha", out object captchaValue))
            {
                error = await MailDemonWebApp.Recaptcha.Verify(captchaValue as string, "Subscribe", HttpContext.GetRemoteIPAddress().ToString());
            }
            SubscribeModel model = new SubscribeModel { Message = error, Error = !string.IsNullOrWhiteSpace(error) };
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
                try
                {
                    string token = db.PreSubscribeToMailingList(model.Fields, email, model.Id, HttpContext.GetRemoteIPAddress().ToString());
                    return RedirectToAction(nameof(SubscribeConfirm), new { id = model.Id });
                }
                catch (Exception ex)
                {
                    MailDemonLog.Error(ex);
                    model.Error = true;
                    model.Message += "<br/>" + Resources.UnknownError;
                    return View(nameof(Subscribe), model);
                }
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

        public IActionResult Login(string returnUrl)
        {
            return View(new LoginModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ActionName(nameof(Login))]
        public async Task<IActionResult> LoginPost(LoginModel login)
        {
            if (User.Identity.IsAuthenticated)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Redirect("/");
            }
            else if (string.IsNullOrWhiteSpace(login.UserName) || string.IsNullOrWhiteSpace(login.Password))
            {
                login.Error = true;
                login.Message = Resources.UsernameOrPasswordIsBlank;
            }
            else if (login.UserName != MailDemonWebApp.AdminLogin.Key && login.Password != MailDemonWebApp.AdminLogin.Value)
            {
                login.Error = true;
                login.Message = Resources.LoginFailed;
            }
            else
            {
                var claims = new[] { new Claim(ClaimTypes.Name, login.UserName), new Claim(ClaimTypes.Role, "Administrator") };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
                if (string.IsNullOrWhiteSpace(login.ReturnUrl))
                {
                    return Redirect("/");
                }
                else
                {
                    return Redirect(login.ReturnUrl);
                }
            }

            return View(login);
        }

        [Authorize]
        public IActionResult Lists()
        {
            return View(db.Select<MailList>().OrderBy(l => l.Name));
        }

        [Authorize]
        public IActionResult EditList(string id)
        {
            long.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long longId);
            MailList list = db.Select<MailList>(longId) ?? new MailList();
            return View(new MailListModel { Value = list, Message = TempData["Message"] as string});
        }

        [HttpPost]
        [Authorize]
        [ActionName(nameof(EditList))]
        public IActionResult EditListPost(string id, MailListModel model)
        {
            try
            {
                db.Upsert(model.Value);
                TempData["Message"] = Resources.Success;
                return RedirectToAction(nameof(EditList), new { id = model.Value.Id });
            }
            catch (Exception ex)
            {
                model.Error = true;
                model.Message = ex.ToString();
                return View(model);
            }
        }

        [Authorize]
        public IActionResult Templates(string id)
        {
            List<MailTemplateBase> templates = new List<MailTemplateBase>();
            foreach (MailTemplate template in db.Select<MailTemplate>(t => string.IsNullOrWhiteSpace(id) || t.ListName == id))
            {
                templates.Add(new MailTemplateBase { Id = template.Id, ListName = template.ListName, LastModified = template.LastModified, Name = template.Name });
            }
            return View(templates.OrderBy(t => t.Name));
        }

        [Authorize]
        public IActionResult EditTemplate(string id)
        {
            long.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long longId);
            MailTemplate template = db.Select<MailTemplate>(longId) ?? new MailTemplate();
            return View(new MailTemplateModel { Value = template, Message = TempData["Message"] as string });
        }

        [HttpPost]
        [Authorize]
        [ActionName(nameof(EditTemplate))]
        public IActionResult EditTemplatePost(string id, MailTemplateModel model)
        {
            try
            {
                if (db.Select<MailList>().FirstOrDefault(l => l.Name == model.Value.ListName) == null)
                {
                    throw new ArgumentException(Resources.ListNotFound);
                }
                model.Value.LastModified = DateTime.UtcNow;
                model.Value.Dirty = true;
                db.Upsert(model.Value);
                TempData["Message"] = Resources.Success;
                return RedirectToAction(nameof(EditTemplate), new { id = model.Value.Id });
            }
            catch (Exception ex)
            {
                model.Error = true;
                model.Message = ex.ToString();
                return View(model);
            }
        }

        [Authorize]
        [ResponseCache(NoStore = true)]
        public IActionResult DebugTemplate(string id)
        {
            id = (id ?? string.Empty).Trim().Replace('-', ' ');
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

        [Authorize]
        [ResponseCache(NoStore = true)]
        public IActionResult Error(string code)
        {
            var feature = this.HttpContext.Features.Get<IExceptionHandlerFeature>();
            return View((object)(feature?.Error?.ToString() ?? "Code: " + (code ?? "Unknown")));
        }
    }
}