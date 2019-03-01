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
    [Authorize]
    [ResponseCache(NoStore = true)]
    public class HomeController : Controller
    {
        private readonly MailDemonDatabase db;
        private readonly IMailCreator mailCreator;
        private readonly IMailSender mailSender;

        public bool RequireCaptcha { get; set;  } = true;

        private async Task SendMailAsync(MailListRegistration reg, string fullTemplateName)
        {
            MailboxAddress fromAddress = new MailboxAddress(reg.MailList.FromEmailName, reg.MailList.FromEmailAddress);
            string toDomain = reg.EmailAddress.GetDomainFromEmailAddress();
            MailboxAddress[] toAddresses = new MailboxAddress[] { new MailboxAddress(reg.EmailAddress) };
            MimeMessage message = await mailCreator.CreateMailAsync(fullTemplateName, reg, reg.ViewBagObject as ExpandoObject);
            await mailSender.SendMailAsync(message, fromAddress, toDomain, toAddresses);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            db.Dispose();
        }

        public HomeController(MailDemonDatabase db, IMailCreator mailCreator, IMailSender mailSender)
        {
            this.db = db;
            this.mailCreator = mailCreator;
            this.mailSender = mailSender;
        }

        [AllowAnonymous]
        public IActionResult Index()
        {
            return View();
        }

        [AllowAnonymous]
        public IActionResult SubscribeInitial(string id)
        {
            string result = TempData["result"] as string;
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }
            MailListRegistration model = (string.IsNullOrWhiteSpace(result) ? new MailListRegistration() : JsonConvert.DeserializeObject<MailListRegistration>(result));
            model.ListName = id;
            model.TemplateName = MailTemplate.GetFullTemplateName(id, MailTemplate.NameSubscribeInitial);
            return View(model);
        }

        [HttpPost]
        [ActionName("Subscribe")]
        [AllowAnonymous]
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
            MailListRegistration model = new MailListRegistration { Message = error, Error = !string.IsNullOrWhiteSpace(error) };
            string email = null;
            model.ListName = (id ?? string.Empty).Trim();
            if (formFields.ContainsKey("TemplateName"))
            {
                model.TemplateName = formFields["TemplateName"];
            }
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
                    model.Error = true;
                    model.Message += "<br/>" + Resources.EmailIsInvalid;
                }
                return View(nameof(SubscribeInitial), model);
            }
            else
            {
                try
                {
                    MailListRegistration reg = db.PreSubscribeToMailingList(model.Fields, email, model.ListName, HttpContext.GetRemoteIPAddress().ToString());
                    string url = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/{nameof(SubscribeConfirm)}?token={reg.SubscribeToken}";
                    reg.SubscribeUrl = url;
                    string templateFullName = MailTemplate.GetFullTemplateName(id, MailTemplate.NameSubscribeConfirm);
                    await SendMailAsync(reg, templateFullName);
                    return RedirectToAction(nameof(SubscribeConfirm), new { id = model.ListName });
                }
                catch (Exception ex)
                {
                    MailDemonLog.Error(ex);
                    model.Error = true;
                    model.Message += "<br/>" + ex.Message;
                    return View(nameof(SubscribeInitial), model);
                }
            }
        }

        [AllowAnonymous]
        public IActionResult SubscribeConfirm(string id)
        {
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }

            // the link will be sent via email
            return View("SubscribeConfirmNoLink");
        }

        [AllowAnonymous]
        public async Task<IActionResult> SubscribeWelcome(string id, string token)
        {
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }
            token = (token ?? string.Empty).Trim();
            MailListRegistration reg = db.ConfirmSubscribeToMailingList(id, token);
            if (reg == null)
            {
                return NotFound();
            }

            string url = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/{nameof(Unsubscribe)}/{id}?token={reg.UnsubscribeToken}";
            reg.UnsubscribeUrl = url;
            string templateFullName = MailTemplate.GetFullTemplateName(id, MailTemplate.NameSubscribeWelcome);
            await SendMailAsync(reg, templateFullName);
            return View(templateFullName);
        }

        [AllowAnonymous]
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

        [AllowAnonymous]
        public IActionResult Login(string returnUrl)
        {
            return View(new LoginModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ActionName(nameof(Login))]
        [AllowAnonymous]
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

        public IActionResult Lists()
        {
            return View(db.Select<MailList>().OrderBy(l => l.Name));
        }

        public IActionResult EditList(string id)
        {

            MailList list = db.Select<MailList>(l => l.Name == id).FirstOrDefault() ?? new MailList();
            return View(new MailListModel { Value = list, Message = TempData["Message"] as string});
        }

        [HttpPost]
        [ActionName(nameof(EditList))]
        public IActionResult EditListPost(string id, MailListModel model, string action)
        {
            if (action == "delete")
            {
                return EditListDelete(id);
            }

            try
            {
                model.Value.Name = model.Value.Name?.Trim();
                if (model.Value.Name.Length > 16)
                {
                    throw new ArgumentException("List name is too long");
                }
                else if (!model.Value.FromEmailAddress.TryParseEmailAddress(out _))
                {
                    throw new ArgumentException(Resources.EmailIsInvalid);
                }
                model.Value.Company = model.Value.Company?.Trim();
                model.Value.Website = model.Value.Website?.Trim();
                if (!MailTemplate.ValidateName(model.Value.Name))
                {
                    throw new ArgumentException("Invalid list name, use only letters, numbers, spaces, period, hyphen or underscore.");
                }
                MailList existingList = db.Select<MailList>(l => l.Name == id).FirstOrDefault();
                if (existingList != null && existingList.Name != model.Value.Name)
                {
                    throw new ArgumentException("Cannot rename list once it is created");
                }
                db.Upsert(model.Value);
                TempData["Message"] = Resources.Success;
                return RedirectToAction(nameof(EditList), new { id = model.Value.Name });
            }
            catch (Exception ex)
            {
                MailDemonLog.Error(ex);
                model.Error = true;
                model.Message = ex.Message;
                return View(model);
            }
        }

        private IActionResult EditListDelete(string id)
        {
            try
            {
                MailList list = db.Select<MailList>(l => l.Name == id).FirstOrDefault();
                if (list != null)
                {
                    db.Delete<MailList>(list.Id);
                    db.Delete<MailTemplate>(t => t.Name.StartsWith(list.Name + MailTemplate.FullNameSeparator));
                }
            }
            catch (Exception ex)
            {
                MailDemonLog.Error(ex);
            }
            return RedirectToAction(nameof(EditList));
        }

        public IActionResult Templates(string id)
        {
            List<MailTemplateBase> templates = new List<MailTemplateBase>();
            foreach (MailTemplate template in db.Select<MailTemplate>(t => string.IsNullOrWhiteSpace(id) || t.Name.StartsWith(id + MailTemplate.FullNameSeparator)))
            {
                templates.Add(new MailTemplateBase { Id = template.Id, LastModified = template.LastModified, Name = template.Name });
            }
            return View(templates.OrderBy(t => t.Name));
        }

        public IActionResult EditTemplate(string id)
        {
            MailTemplate template = db.Select<MailTemplate>(t => t.Name == id).FirstOrDefault() ?? new MailTemplate();
            if (template.Id == 0 && string.IsNullOrWhiteSpace(template.Name))
            {
                template.Name = id + MailTemplate.FullNameSeparator;
            }
            return View(new MailTemplateModel { Value = template, Message = TempData["Message"] as string });
        }

        [HttpPost]
        [ActionName(nameof(EditTemplate))]
        public IActionResult EditTemplatePost(string id, MailTemplateModel model, string action)
        {
            if (action == "delete")
            {
                return EditTemplateDelete(id);
            }

            try
            {
                model.Value.Name = model.Value.Name?.Trim();
                if (model.Value.Name.Length > 64)
                {
                    throw new ArgumentException("Template name is too long");
                }
                if (!model.Value.GetListNameAndTemplateName(out string listName, out string templateName) ||
                    !MailTemplate.ValidateName(listName) ||
                    !MailTemplate.ValidateName(templateName))
                {
                    throw new ArgumentException($"Invalid template name, use only letters, numbers, spaces, period, hyphen or underscore. Name format is [listname]{MailTemplate.FullNameSeparator}[templatename].");
                }
                if (db.Select<MailList>().FirstOrDefault(l => l.Name == listName) == null)
                {
                    throw new ArgumentException(string.Format(Resources.ListNotFound, listName));
                }
                model.Value.LastModified = DateTime.UtcNow;
                model.Value.Dirty = true;
                db.Upsert(model.Value);
                TempData["Message"] = Resources.Success;
                return RedirectToAction(nameof(EditTemplate), new { id = model.Value.Name });
            }
            catch (Exception ex)
            {
                MailDemonLog.Error(ex);
                model.Error = true;
                model.Message = ex.Message;
                return View(model);
            }
        }

        private IActionResult EditTemplateDelete(string id)
        {
            try
            {
                MailTemplate template = db.Select<MailTemplate>(t => t.Name == id).FirstOrDefault();
                if (template != null)
                {
                    db.Delete<MailTemplate>(template.Id);
                }
            }
            catch (Exception ex)
            {
                MailDemonLog.Error(ex);
            }
            return RedirectToAction(nameof(EditTemplate));
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
                FirstName = "Bob",
                LastName = "Smith",
                Company = "Fake Company",
                ListName = "Default",
                SubscribedDate = DateTime.UtcNow,
                SubscribeToken = Guid.NewGuid().ToString("N"),
                Expires = DateTime.MinValue
            };
            return View(id, tempReg);
        }

        public IActionResult Error(string code)
        {
            var feature = this.HttpContext.Features.Get<IExceptionHandlerFeature>();
            return View((object)(feature?.Error?.ToString() ?? "Code: " + (code ?? "Unknown")));
        }
    }
}