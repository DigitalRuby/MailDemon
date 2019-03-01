#region Imports

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using NUnit.Framework;

using MailDemon;

using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Http;
using MimeKit;
using MailKit.Net.Smtp;

#endregion Imports

namespace MailDemonTests
{
    public class MailRegistrationTest : ITempDataProvider, IMailCreator, IMailSender
    {
        private const string listName = "TestList";
        private const string scheme = "https";
        private const string domainName = "testdomain.com";
        private const string mailAddress = "testemail@testemaildomain.com";
        private const string fromAddress = "sender@mydomain.com";
        private const string fromName = "Test Sender";
        private const string subject = "Mail Subject";
        private const string company = "Test Company";
        private const string fullAddress = "123 test st, state, country 99999";
        private const string website = "https://testwebsite.com";

        private readonly HomeController homeController;
        private readonly HttpContext httpContext = new DefaultHttpContext();
        private readonly Dictionary<string, object> tempData = new Dictionary<string, object>();
        private readonly Dictionary<string, int> createdMail = new Dictionary<string, int>();
        private int sentMail;
        private string templateName;

        private void VerifyCreatedMail(string templateName, object model, string key, string value)
        {
            Assert.AreEqual(1, sentMail);
            Assert.AreEqual(1, createdMail.Count);
            KeyValuePair<string, int> mail = createdMail.First();
            Assert.AreEqual(1, mail.Value);
            ExpandoObject obj = new ExpandoObject();
            ((IDictionary<string, object>)obj)[key] = value;
            string fullText = GetMailCreationFullText(subject, templateName, model, obj);
            Assert.IsTrue(Regex.IsMatch(mail.Key, $@"^{fullText}$", RegexOptions.IgnoreCase));
            createdMail.Clear();
            sentMail = 0;
        }

        private string GetMailCreationFullText(string subject, string templateName, object model, ExpandoObject extraInfo)
        {
            (extraInfo as IDictionary<string, object>).Remove(MailListRegistration.VarMailList);
            string extraInfoText = string.Join(";", extraInfo.Select(x => x.Key + "=" + x.Value));
            string fullText = (subject + ";" + templateName + ";" + (model?.ToString()) + ";" + extraInfoText);
            return fullText;
        }

        private void Cleanup()
        {
            MailDemonDatabase.DeleteDatabase();
            createdMail.Clear();
        }

        private string Subscribe()
        {
            using (var db = new MailDemonDatabase())
            {
                db.Insert<MailList>(new MailList { Name = listName, FromEmailAddress = fromAddress, FromEmailName = fromName,
                    Company = company, PhysicalAddress = fullAddress, Website = website });
            }

            homeController.SubscribeInitialPost(listName, new Dictionary<string, string>
            {
                { "ff_firstName", "Bob" },
                { "ff_lastName", "Smith" },
                { "ff_emailAddress", mailAddress },
                { "ff_company_optional", null }
            }).Sync();

            // check database for registration
            MailListRegistration reg = null;
            using (var db = new MailDemonDatabase())
            {
                reg = db.Select<MailListRegistration>().FirstOrDefault();
                Assert.NotNull(reg);
                Assert.AreEqual("Bob", reg.Fields["firstName"]);
                Assert.AreEqual("Smith", reg.Fields["lastName"]);
                Assert.AreEqual(mailAddress, reg.EmailAddress);
                Assert.IsEmpty(reg.Fields["company"] as string);
                Assert.IsNotNull(reg.SubscribeToken);
                Assert.IsNull(reg.UnsubscribeToken);
                Assert.AreEqual(default(DateTime), reg.SubscribedDate);
                Assert.AreEqual(default(DateTime), reg.UnsubscribedDate);
            }

            // verify we sent the right confirmation email
            VerifyCreatedMail(MailTemplate.GetFullTemplateName(listName, MailTemplate.NameSubscribeConfirm), reg, MailListRegistration.VarSubscribeUrl,
                $@"{scheme}://{domainName}/SubscribeConfirm\?token=.{{16,}}");

            // verify the subscribe confirm has no errors
            homeController.SubscribeConfirm(listName);

            // perform the final subscribe action
            homeController.SubscribeWelcome(listName, reg.SubscribeToken).Sync();

            // verify we sent the right welcome mail
            VerifyCreatedMail(MailTemplate.GetFullTemplateName(listName, MailTemplate.NameSubscribeWelcome), reg, MailListRegistration.VarUnsubscribeUrl,
                $@"{scheme}://{domainName}/Unsubscribe/TestList\?token=.{{16,}}");

            // validate there is an unsubscribe in the db
            using (var db = new MailDemonDatabase())
            {
                reg = db.Select<MailListRegistration>().FirstOrDefault();
                Assert.AreEqual(listName, reg.ListName);
                Assert.AreEqual("127.0.0.1", reg.IPAddress);
                Assert.IsNotNull(reg.UnsubscribeToken);
                Assert.AreNotEqual(default(DateTime), reg.SubscribedDate);
                Assert.AreEqual(default(DateTime), reg.UnsubscribedDate);
            }

            return reg.UnsubscribeToken;
        }

        private void Unsubscribe(string unsubscribeToken)
        {
            // now unsubscribe
            homeController.Unsubscribe(listName, unsubscribeToken);

            // validate that we are unsubscribed
            using (var db = new MailDemonDatabase())
            {
                MailListRegistration reg = db.Select<MailListRegistration>().FirstOrDefault();
                Assert.AreNotEqual(default(DateTime), reg.UnsubscribedDate);
            }
        }

        public MailRegistrationTest()
        {
             homeController = new HomeController(new MailDemonDatabase(), this, this);
        }

        [SetUp]
        public void Setup()
        {
            Cleanup();
            MailDemonDatabase.DatabaseOptions = "Journal=false; Flush=true;";
            homeController.RequireCaptcha = false;
            homeController.TempData = new TempDataDictionary(httpContext, this);
            homeController.ControllerContext.HttpContext = httpContext;
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
            httpContext.Request.Scheme = scheme;
            httpContext.Request.Host = new HostString(domainName);
        }

        [TearDown]
        public void TearDown()
        {
            Cleanup();
            homeController.Dispose();
        }

        [Test]
        public void TestRegister()
        {
            string unsubscribeToken = Subscribe();
            Unsubscribe(unsubscribeToken);
        }

        [Test]
        public void TestRegisterFails()
        {
            homeController.SubscribeInitialPost(listName, new Dictionary<string, string>
            {
                { "ff_firstName", "Bob" },
                { "ff_lastName", "Smith" },
                { "ff_company_optional", "company" }
            }).Sync();

            // check database for registration not exist
            using (var db = new MailDemonDatabase())
            {
                Assert.AreEqual(0, db.Select<MailListRegistration>().Count());
            }
        }

        IDictionary<string, object> ITempDataProvider.LoadTempData(HttpContext context)
        {
            return tempData;
        }

        void ITempDataProvider.SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            
        }

        Task<MimeMessage> IMailCreator.CreateMailAsync(string templateName, object model, ExpandoObject extraInfo)
        {
            string fullText = GetMailCreationFullText(subject, templateName, model, extraInfo);
            this.templateName = templateName;
            if (!createdMail.TryGetValue(fullText, out int count))
            {
                count = 0;
            }
            createdMail[fullText] = ++count;
            MimeMessage msg = new MimeMessage
            {
                Body = (new BodyBuilder
                {
                    HtmlBody = "<html><body>Mail Body: " + templateName + "</body></html>"
                }).ToMessageBody(),
                Subject = subject
            };
            return Task.FromResult(msg);
        }

        Task IMailSender.SendMailAsync(MimeMessage message, MailboxAddress from, string toDomain, IEnumerable<MailboxAddress> toAddresses, Action<MimeMessage> onPrepare)
        {
            Assert.AreEqual(subject, message.Subject);
            Assert.AreEqual("<html><body>Mail Body: " + templateName + "</body></html>", message.HtmlBody);
            sentMail++;
            return Task.CompletedTask;
        }
    }
}
