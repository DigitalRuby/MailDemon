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

#endregion Imports

namespace MailDemonTests
{
    public class MailRegistrationTest : ITempDataProvider, IMailSendService
    {
        private const string listName = "TestList";
        private const string scheme = "https";
        private const string domainName = "testdomain.com";
        private const string mailAddress = "testemail@testemaildomain.com";

        private readonly HomeController homeController;
        private readonly HttpContext httpContext = new DefaultHttpContext();
        private readonly Dictionary<string, object> tempData = new Dictionary<string, object>();
        private readonly Dictionary<string, int> sentMail = new Dictionary<string, int>();

        private void VerifySentMail(string templateName, object model, string key, string value)
        {
            Assert.AreEqual(1, sentMail.Count);
            KeyValuePair<string, int> mail = sentMail.First();
            Assert.AreEqual(1, mail.Value);
            ExpandoObject obj = new ExpandoObject();
            ((IDictionary<string, object>)obj)[key] = value;
            string fullText = GetFullText(mailAddress, listName, templateName, model, obj);
            Assert.IsTrue(Regex.IsMatch(mail.Key, $@"^{fullText}$", RegexOptions.IgnoreCase));
            sentMail.Clear();
        }

        private string GetFullText(string to, string listName, string templateName, object model, ExpandoObject extraInfo)
        {
            string extraInfoText = string.Join(";", extraInfo.Select(x => x.Key + "=" + x.Value));
            string fullText = (to + ";" + listName + ";" + templateName + ";" + (model?.ToString()) + ";" + extraInfoText);
            return fullText;
        }

        private void Cleanup()
        {
            MailDemonDatabase.DeleteDatabase();
            sentMail.Clear();
        }

        private string Subscribe()
        {
            using (var db = new MailDemonDatabase())
            {
                db.Insert<MailList>(new MailList { Name = listName });
            }

            homeController.SubscribePost(listName, new Dictionary<string, string>
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

            // verify the subscribe confirm has no errors
            homeController.SubscribeConfirm(listName);

            // verify we sent the right confirmation email
            VerifySentMail(MailTemplate.NameConfirmation, reg, MailTemplate.VarConfirmUrl, $@"{scheme}://{domainName}/SubscribeSuccess\?token=.{{16,}}");

            // perform the final subscribe action
            homeController.SubscribeSuccess(listName, reg.SubscribeToken).Sync();

            // verify we sent the right welcome mail
            VerifySentMail(MailTemplate.NameWelcome, reg, MailTemplate.VarUnsubscribeUrl, $@"{scheme}://{domainName}/Unsubscribe/TestList\?token=.{{16,}}");

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
             homeController = new HomeController(new MailDemonDatabase(), this);
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
            homeController.SubscribePost(listName, new Dictionary<string, string>
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

        Task IMailSendService.SendMail(string to, string listName, string templateName, object model, ExpandoObject extraInfo)
        {
            string fullText = GetFullText(to, listName, templateName, model, extraInfo);
            if (!sentMail.TryGetValue(fullText, out int count))
            {
                count = 0;
            }
            sentMail[fullText] = ++count;
            return Task.CompletedTask;
        }
    }
}
