#region Imports

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Dynamic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
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
    public interface IMailSendService
    {
        Task SendMail(string to, string listName, string templateName, object model, ExpandoObject extraInfo);
    }

    public class HomeController : Controller
    {
        private readonly MailDemonDatabase db;
        private readonly IMailSendService mailSender;

        public bool RequireCaptcha { get; set;  } = true;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            db.Dispose();
        }

        public HomeController(MailDemonDatabase db, IMailSendService mailSender)
        {
            this.db = db;
            this.mailSender = mailSender;
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
            model.ListName = id;
            model.Title = Resources.SubscribeTitle.FormatHtml(id);
            return View(model);
        }

        [HttpPost]
        [ActionName("Subscribe")]
        public async Task<IActionResult> SubscribePost(string id, Dictionary<string, string> formFields)
        {
            if (id.Length == 0)
            {
                return NotFound();
            }
            string error = null;
            if (RequireCaptcha && formFields.TryGetValue("captcha", out string captchaValue))
            {
                error = await MailDemonWebApp.Recaptcha.Verify(captchaValue, "Subscribe", HttpContext.GetRemoteIPAddress().ToString());
            }
            SubscribeModel model = new SubscribeModel { Message = error, Error = !string.IsNullOrWhiteSpace(error) };
            string email = null;
            model.ListName = (id ?? string.Empty).Trim();
            foreach (KeyValuePair<string, string> field in formFields)
            {
                if (field.Key.StartsWith("ff_"))
                {
                    string value = field.Value?.Trim();
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
                model.Title = Resources.SubscribeTitle.FormatHtml(model.ListName);
                return View(nameof(Subscribe), model);
            }
            else
            {
                try
                {
                    MailListRegistration reg = db.PreSubscribeToMailingList(model.Fields, email, model.ListName, HttpContext.GetRemoteIPAddress().ToString());
                    string url = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/{nameof(SubscribeSuccess)}?token={reg.SubscribeToken}";
                    ExpandoObject obj = new ExpandoObject();
                    ((IDictionary<string, object>)obj)[MailTemplate.VarConfirmUrl] = url;
                    await mailSender.SendMail(email, model.ListName, MailTemplate.NameConfirmation, reg, obj);
                    return RedirectToAction(nameof(SubscribeConfirm), new { id = model.ListName });
                }
                catch (Exception ex)
                {
                    MailDemonLog.Error(ex);
                    model.Error = true;
                    model.Message += "<br/>" + ex.Message;
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

        public async Task<IActionResult> SubscribeSuccess(string id, string token)
        {
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }
            token = (token ?? string.Empty).Trim();
            MailListRegistration reg = db.ConfirmSubscribeToMailingList(id, token);
            if (reg != null)
            {
                string url = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/{nameof(Unsubscribe)}/{id}?token={reg.UnsubscribeToken}";
                ExpandoObject obj = new ExpandoObject();
                ((IDictionary<string, object>)obj)[MailTemplate.VarUnsubscribeUrl] = url;
                await mailSender.SendMail(reg.EmailAddress, id, MailTemplate.NameWelcome, reg, obj);
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
            MailList list = db.Select<MailList>(l => l.Name == id).FirstOrDefault() ?? new MailList();
            return View(new MailListModel { Value = list, Message = TempData["Message"] as string});
        }

        [HttpPost]
        [Authorize]
        [ActionName(nameof(EditList))]
        public IActionResult EditListPost(string id, MailListModel model)
        {
            try
            {
                model.Value.Name = model.Value.Name?.Trim();
                model.Value.Company = model.Value.Company?.Trim();
                model.Value.Website = model.Value.Website?.Trim();
                if (string.IsNullOrWhiteSpace(model.Value.Name))
                {
                    throw new ArgumentException(Resources.NameInvalid);
                }
                if (!Regex.IsMatch(model.Value.Name, "^[A-Za-z0-9_\\- ]+$"))
                {
                    throw new ArgumentException("Invalid list name, use only letters, numbers, spaces, hyphen or underscore.");
                }
                db.Upsert(model.Value);
                TempData["Message"] = Resources.Success;
                return RedirectToAction(nameof(EditList), new { id = model.Value.Id });
            }
            catch (Exception ex)
            {
                model.Error = true;
                model.Message = ex.Message;
                return View(model);
            }
        }

        [Authorize]
        public IActionResult Templates(string id)
        {
            List<MailTemplateBase> templates = new List<MailTemplateBase>();
            foreach (MailTemplate template in db.Select<MailTemplate>(t => string.IsNullOrWhiteSpace(id) || t.Name.StartsWith(id + "_")))
            {
                templates.Add(new MailTemplateBase { Id = template.Id, LastModified = template.LastModified, Name = MailTemplate.GetFullName(id, template.Name) });
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
                model.Value.Name = model.Value.Name?.Trim();
                if (string.IsNullOrWhiteSpace(model.Value.Name))
                {
                    throw new ArgumentException(Resources.NameInvalid);
                }
                string[] parts = model.Value.Name.Split('|');
                if (parts.Length != 2 || !Regex.IsMatch(parts[0], "^[A-Za-z0-9_\\- ]+$") || !Regex.IsMatch(parts[1], "^[A-Za-z0-9_\\- ]+$"))
                {
                    throw new ArgumentException("Invalid template name, use only letters, numbers, spaces, hyphen or underscore. Name format is [listname]|[templatename].");
                }
                if (db.Select<MailList>().FirstOrDefault(l => l.Name == parts[0]) == null)
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
                model.Message = ex.Message;
                return View(model);
            }
        }

        [Authorize]
        [ResponseCache(NoStore = true)]
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

        [Authorize]
        [ResponseCache(NoStore = true)]
        public IActionResult Error(string code)
        {
            var feature = this.HttpContext.Features.Get<IExceptionHandlerFeature>();
            return View((object)(feature?.Error?.ToString() ?? "Code: " + (code ?? "Unknown")));
        }
    }
}