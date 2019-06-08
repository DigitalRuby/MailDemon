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
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;

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
        private readonly IBulkMailSender bulkMailSender;
        private readonly IAuthority authority;

        public bool RequireCaptcha { get; set;  } = true;

        private ExpandoObject GetViewBagForTemplate()
        {
            dynamic viewBag = new ExpandoObject();
            viewBag.Title = "Email";
            viewBag.Layout = "/Views/_LayoutMail.cshtml";
            viewBag.BaseUrl = authority.Authority;
            return viewBag;
        }

        private IEnumerable<MailToSend> GetMessages(MailListSubscription sub, MimeMessage message, MailboxAddress fromAddress, IEnumerable<MailboxAddress> toAddresses)
        {
            foreach (MailboxAddress toAddress in toAddresses)
            {
                message.From.Clear();
                message.To.Clear();
                message.From.Add(fromAddress);
                message.To.Add(toAddress);
                yield return new MailToSend { Subscription = sub, Message = message };
            }
        }

        private async Task SendMailAsync(MailListSubscription reg, string fullTemplateName)
        {
            MailboxAddress fromAddress = new MailboxAddress(reg.MailList.FromEmailName, reg.MailList.FromEmailAddress);
            string toDomain = reg.EmailAddress.GetDomainFromEmailAddress();
            MailboxAddress[] toAddresses = new MailboxAddress[] { new MailboxAddress(reg.EmailAddress) };
            MimeMessage message = await mailCreator.CreateMailAsync(fullTemplateName, reg, GetViewBagForTemplate(), null);
            await mailSender.SendMailAsync(toDomain, GetMessages(reg, message, fromAddress, toAddresses));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            db.Dispose();
        }

        public HomeController(MailDemonDatabase db,
            IMailCreator mailCreator, IMailSender mailSender, IBulkMailSender bulkMailSender, IAuthority authority)
        {
            this.db = db;
            this.mailCreator = mailCreator ?? throw new ArgumentNullException(nameof(mailCreator));
            this.mailSender = mailSender ?? throw new ArgumentNullException(nameof(mailSender));
            this.bulkMailSender = bulkMailSender;
            this.authority = authority;
        }

        [AllowAnonymous]
        public IActionResult Index()
        {
            if (User.Identity.IsAuthenticated)
            {
                return View();
            }
            return Ok();
        }

        [AllowAnonymous]
        public IActionResult Error(string code)
        {
            if (User.Identity.IsAuthenticated)
            {
                var feature = this.HttpContext.Features.Get<IExceptionHandlerFeature>();
                return View((object)(feature?.Error?.ToString() ?? "Code: " + (code ?? "Unknown")));
            }
            return Ok();
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
            MailListSubscription model = (string.IsNullOrWhiteSpace(result) ? new MailListSubscription() : JsonConvert.DeserializeObject<MailListSubscription>(result));
            model.MailList = db.Lists.FirstOrDefault(l => l.Name == id);
            if (model.MailList == null)
            {
                return NotFound();
            }
            model.ListName = id;
            model.TemplateName = MailTemplate.GetFullTemplateName(id, MailTemplate.NameSubscribeInitial);
            return View(model);
        }

        [HttpPost]
        [ActionName(nameof(SubscribeInitial))]
        [AllowAnonymous]
        public async Task<IActionResult> SubscribeInitialPost(string id, Dictionary<string, string> formFields)
        {
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }
            string error = null;
            if (RequireCaptcha && formFields.TryGetValue("captcha", out string captchaValue))
            {
                error = await MailDemonWebApp.Instance.Recaptcha.Verify(captchaValue, nameof(SubscribeInitial), HttpContext.GetRemoteIPAddress().ToString());
            }
            MailListSubscription model = new MailListSubscription { Message = error, Error = !string.IsNullOrWhiteSpace(error) };
            MailList list = db.Lists.FirstOrDefault(l => l.Name == id);
            if (list == null)
            {
                return NotFound();
            }
            model.MailList = list;
            model.ListName = model.MailList.Name;
            if (formFields.ContainsKey("TemplateName"))
            {
                model.TemplateName = formFields["TemplateName"];
            }
            string email = null;
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
                            model.EmailAddress = email;
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
            model.EmailAddressDomain = model.GetDomainFromEmailAddress();
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
                    model.IPAddress = HttpContext.GetRemoteIPAddress().ToString();
                    if (!db.PreSubscribeToMailingList(ref model))
                    {
                        throw new InvalidOperationException(Resources.AlreadySubscribed.FormatHtml(id));
                    }
                    else
                    {
                        string url = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/{nameof(SubscribeWelcome)}/{id}?token={model.SubscribeToken}";
                        model.SubscribeUrl = url;
                        string templateFullName = MailTemplate.GetFullTemplateName(id, MailTemplate.NameSubscribeConfirm);
                        await SendMailAsync(model, templateFullName);
                        return RedirectToAction(nameof(SubscribeConfirm), new { id = model.ListName });
                    }
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
            MailList list = db.Lists.FirstOrDefault(l => l.Name == id);
            if (list == null)
            {
                return NotFound();
            }

            // the link will be sent via email
            return View("SubscribeConfirmNoLink", list);
        }

        [AllowAnonymous]
        public async Task<IActionResult> SubscribeWelcome(string id, string token)
        {
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }

            // stupid bing/outlook email preview
            string userAgent = Request.Headers["User-Agent"].ToString();
            if (string.IsNullOrWhiteSpace(userAgent) || userAgent.Contains("preview", StringComparison.OrdinalIgnoreCase))
            {
                return Content(string.Empty);
            }

            token = (token ?? string.Empty).Trim();
            MailListSubscription reg = db.ConfirmSubscribeToMailingList(id, token);
            if (reg == null)
            {
                return NotFound();
            }

            // temp property does not go in db
            reg.UnsubscribeUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/{nameof(Unsubscribe)}/{id}?token={reg.UnsubscribeToken}";

            string templateFullName = MailTemplate.GetFullTemplateName(id, MailTemplate.NameSubscribeWelcome);
            await SendMailAsync(reg, templateFullName);
            return View(reg);
        }

        [AllowAnonymous]
        public IActionResult Unsubscribe(string id, string token)
        {
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }

            // stupid bing/outlook email preview
            string userAgent = Request.Headers["User-Agent"].ToString();
            if (string.IsNullOrWhiteSpace(userAgent) || userAgent.Contains("preview", StringComparison.OrdinalIgnoreCase))
            {
                return Content(string.Empty);
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
        public IActionResult MailDemonLogin(string returnUrl)
        {
            return View("Login", new LoginModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ActionName(nameof(MailDemonLogin))]
        [AllowAnonymous]
        public async Task<IActionResult> MailDemonLoginPost(LoginModel login)
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
            else if (login.UserName != MailDemonWebApp.Instance.AdminLogin.Key || login.Password != MailDemonWebApp.Instance.AdminLogin.Value)
            {
                login.Error = true;
                login.Message = Resources.LoginFailed;
            }
            else
            {
                var claims = new[] { new Claim(ClaimTypes.Name, login.UserName), new Claim(ClaimTypes.Role, "Administrator") };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
                IPBan.IPBanPlugin.IPBanLoginSucceeded("HTTPS", login.UserName, HttpContext.GetRemoteIPAddress().ToString());
                if (string.IsNullOrWhiteSpace(login.ReturnUrl))
                {
                    return Redirect("/");
                }
                else
                {
                    return Redirect(login.ReturnUrl);
                }
            }

            IPBan.IPBanPlugin.IPBanLoginFailed("HTTPS", login.UserName, HttpContext.GetRemoteIPAddress().ToString());

            return View("Login", login);
        }

        public IActionResult Lists()
        {
            return View(db.Lists.OrderBy(l => l.Name).ToArray());
        }

        public IActionResult EditList(string id)
        {

            MailList list = db.Lists.FirstOrDefault(l => l.Name == id) ?? new MailList();
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
                    throw new ArgumentException(Resources.NameIsTooLong.FormatHtml(16));
                }
                else if (!model.Value.FromEmailAddress.TryParseEmailAddress(out _))
                {
                    throw new ArgumentException(Resources.EmailIsInvalid);
                }
                model.Value.Company = model.Value.Company?.Trim();
                model.Value.Website = model.Value.Website?.Trim();
                if (!MailTemplate.ValidateName(model.Value.Name))
                {
                    throw new ArgumentException(Resources.NameInvalidChars);
                }
                MailList existingList = db.Lists.FirstOrDefault(l => l.Name == model.Value.Name);
                if (existingList != null && (existingList.Name != model.Value.Name || model.Value.Id == 0))
                {
                    throw new ArgumentException(Resources.NameCannotChange);
                }
                if (model.Value.Id == 0)
                {
                    db.Lists.Add(model.Value);
                }
                else
                {
                    db.Update(model.Value);
                }
                db.SaveChanges();
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
                MailList list = db.Lists.FirstOrDefault(l => l.Name == id);
                if (list != null)
                {
                    db.Subscriptions.RemoveRange(db.Subscriptions.Where(r => r.ListName == id));
                    db.Templates.RemoveRange(db.Templates.Where(t => t.Name.StartsWith(list.Name + MailTemplate.FullNameSeparator)));
                    db.Lists.Remove(list);
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
            foreach (MailTemplate template in db.Templates.Where(t => string.IsNullOrWhiteSpace(id) || t.Name.StartsWith(id + MailTemplate.FullNameSeparator)))
            {
                templates.Add(new MailTemplateBase { Id = template.Id, LastModified = template.LastModified, Name = template.Name });
            }
            return View(templates.OrderBy(t => t.Name));
        }

        public IActionResult EditTemplate(string id)
        {
            MailTemplate template = db.Templates.FirstOrDefault(t => t.Name == id) ?? new MailTemplate { Text = "<!-- Subject: ReplaceWithYourSubject -->\r\n" };
            if (template.Id == 0 && string.IsNullOrWhiteSpace(template.Name) && id.IndexOf(MailTemplate.FullNameSeparator) < 0)
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
            else if (action == "send")
            {
                model.All = Request.Form.ContainsKey("All");
                return EditTemplateSend(id, model.All);
            }

            try
            {
                model.Value.Name = model.Value.Name?.Trim();
                if (model.Value.Name.Length > 64)
                {
                    throw new ArgumentException(Resources.NameIsTooLong.FormatHtml(64));
                }
                if (!model.Value.GetListNameAndTemplateName(out string listName, out string templateName) ||
                    !MailTemplate.ValidateName(listName) ||
                    !MailTemplate.ValidateName(templateName))
                {
                    throw new ArgumentException(Resources.TemplateNameInvalidChars.FormatHtml(MailTemplate.FullNameSeparator));
                }
                if (db.Lists.FirstOrDefault(l => l.Name == listName) == null)
                {
                    throw new ArgumentException(string.Format(Resources.ListNotFound, listName));
                }
                model.Value.LastModified = DateTime.UtcNow;
                model.Value.Dirty = true;
                if (model.Value.Id == 0)
                {
                    db.Templates.Add(model.Value);
                }
                else
                {
                    db.Update(model.Value);
                }
                db.SaveChanges();
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
                MailTemplate template = db.Templates.FirstOrDefault(t => t.Name == id);
                if (template != null)
                {
                    db.Templates.Remove(template);
                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                MailDemonLog.Error(ex);
            }
            return RedirectToAction(nameof(EditTemplate));
        }

        private IActionResult EditTemplateSend(string id, bool all)
        {
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }
            string listName = MailTemplate.GetListName(id);
            MailList list = db.Lists.FirstOrDefault(l => l.Name == listName);
            if (list == null)
            {
                return NotFound();
            }
            if (bulkMailSender == null)
            {
                return NotFound();
            }
            string unsubscribeUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/{nameof(Unsubscribe)}/{id}?token={{0}}";
            bulkMailSender.SendBulkMail(list, mailCreator, mailSender, all, id, unsubscribeUrl).ConfigureAwait(false).GetAwaiter();
            TempData["Message"] = Resources.SendStarted;
            return RedirectToAction(nameof(HomeController.EditTemplate), new { id });
        }

        [HttpGet]
        [HttpPost]
        public async Task<IActionResult> DebugTemplate(string id)
        {
            if (HttpContext.Request.Method == "POST")
            {
                // switch to GET request, to avoid stupid double form post popup
                return RedirectToAction(nameof(DebugTemplate), new { id });
            }

            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }
            string listName = MailTemplate.GetListName(id);
            MailList list = db.Lists.FirstOrDefault(l => l.Name == listName);
            if (list == null)
            {
                return NotFound();
            }

            string unsubscribeToken = Guid.NewGuid().ToString("N");
            MailListSubscription tempReg = new MailListSubscription
            {
                EmailAddress = "test@domain.com",
                IPAddress = HttpContext.GetRemoteIPAddress().ToString(),
                FirstName = "Bob",
                LastName = "Smith",
                Company = "Fake Company",
                ListName = list.Name,
                SubscribedDate = DateTime.UtcNow,
                SubscribeToken = Guid.NewGuid().ToString("N"),
                Expires = DateTime.MinValue,
                MailList = list,
                UnsubscribeToken = unsubscribeToken,
                UnsubscribeUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/{nameof(Unsubscribe)}/{list.Name}?token={unsubscribeToken}"
            };
            MimeMessage msg = await mailCreator.CreateMailAsync(id, tempReg, GetViewBagForTemplate(), (_html, subject) =>
            {
                return _html.Replace("<body>", "<body><div style='padding: 10px; width: 100%; background-color: #2A2A2A; border-bottom: 1px solid #444444;'>SUBJECT: " + System.Web.HttpUtility.HtmlEncode(subject) + "</div>");
            });
            string html = msg.HtmlBody;
            
            return Content(msg.HtmlBody, "text/html");
        }

        public IActionResult Subscribers(string id)
        {
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return NotFound();
            }
            MailList list = db.Lists.FirstOrDefault(l => l.Name == id);
            if (list == null)
            {
                return NotFound();
            }
            ICollection<MailListSubscription> subscribers = db.Subscriptions.Where(s => s.ListName == id).Take(1000).ToList();
            ViewBag.ListName = id;
            return View(subscribers);
        }

        [HttpPost]
        public IActionResult Subscribers(string id, string action, long? subId)
        {
            if (action == "delete" && subId != null)
            {
                MailListSubscription sub = db.Subscriptions.FirstOrDefault(s => s.Id == subId);
                if (sub != null)
                {
                    db.Subscriptions.Remove(sub);
                    db.SaveChanges();
                }
            }
            return RedirectToAction(nameof(Subscribers), new { id });
        }
    }

    /// <summary>
    /// Authority (base url)
    /// </summary>
    public interface IAuthority
    {
        /// <summary>
        /// Authority
        /// </summary>
        string Authority { get; }
    }
}