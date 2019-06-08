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
    public class MailRegistrationTest : ITempDataProvider, IMailSender, IAuthority
    {
        private const string listName = "TestList";
        private const string scheme = "https";
        private const string domainName = "testdomain.com";
        private const string mailAddress = "testemail@testemaildomain.com";
        private const string fromAddress = "sender@mydomain.com";
        private const string fromName = "Test Sender";
        private const string company = "Test Company";
        private const string fullAddress = "123 test st, state, country 99999";
        private const string website = "https://testwebsite.com";
        private readonly string templateName = MailTemplate.GetFullTemplateName(listName, MailTemplate.NameSubscribeConfirm);
        private readonly string templateName2 = MailTemplate.GetFullTemplateName(listName, MailTemplate.NameSubscribeWelcome);
        private const string templateText = "<!-- Subject: Hello @Model.FirstName --> Body: Last Name @Model.LastName, Subscribe: @Model.SubscribeUrl, Unsubscribe: @Model.UnsubscribeUrl";
        private const string templateText2 = "<!-- Subject: Hello2 @Model.FirstName --> Body2: Last Name @Model.LastName, Subscribe: @Model.SubscribeUrl, Unsubscribe: @Model.UnsubscribeUrl";
        private const string authority = "http://localhostignore:5000";

        private readonly HttpContext httpContext = new DefaultHttpContext();
        private readonly Dictionary<string, object> tempData = new Dictionary<string, object>();

        private HomeController homeController;
        private string expectedSubject;
        private string expectedBody;
        private int sentMailCount;
        private MailCreator mailCreator;

        private void Cleanup()
        {
            homeController?.Dispose();
            homeController = null;
            MailDemonDatabase.DeleteDatabase(true);
            sentMailCount = 0;
            mailCreator = null;
        }

        private string Subscribe()
        {
            using (var db = new MailDemonDatabase())
            {
                db.Lists.Add(new MailList { Name = listName, FromEmailAddress = fromAddress, FromEmailName = fromName,
                    Company = company, PhysicalAddress = fullAddress, Website = website });
                db.SaveChanges();
            }

            expectedSubject = "Hello Bob";
            expectedBody = "<!-- Subject: Hello Bob --> Body: Last Name Smith, Subscribe: https://testdomain.com/SubscribeWelcome/TestList?token={subscribe-token}, Unsubscribe: ";
            homeController.SubscribeInitialPost(listName, new Dictionary<string, string>
            {
                { "ff_firstName", "Bob" },
                { "ff_lastName", "Smith" },
                { "ff_emailAddress", mailAddress },
                { "ff_company_optional", null }
            }).Sync();

            // check database for registration
            MailListSubscription reg = null;
            using (var db = new MailDemonDatabase())
            {
                reg = db.Subscriptions.FirstOrDefault();
                Assert.NotNull(reg);
                Assert.AreEqual("Bob", reg.Fields["firstName"]);
                Assert.AreEqual("Smith", reg.Fields["lastName"]);
                Assert.AreEqual(mailAddress, reg.EmailAddress);
                Assert.IsEmpty(reg.Fields["company"] as string);
                Assert.IsNotNull(reg.SubscribeToken);
                Assert.IsNull(reg.UnsubscribeToken);
                Assert.AreEqual(default(DateTime), reg.SubscribedDate);
                Assert.AreEqual(default(DateTime), reg.UnsubscribedDate);
                Assert.AreNotEqual(default(DateTime), reg.Expires);
            }

            // verify the subscribe confirm has no errors
            homeController.SubscribeConfirm(listName);
            Assert.AreEqual(1, sentMailCount);
            sentMailCount = 0;

            // perform the final subscribe action
            expectedSubject = "Hello2 Bob";
            expectedBody = "<!-- Subject: Hello2 Bob --> Body2: Last Name Smith, Subscribe: https://testdomain.com/SubscribeWelcome/TestList?token={subscribe-token}, Unsubscribe: https://testdomain.com/Unsubscribe/TestList?token={unsubscribe-token}";
            homeController.SubscribeWelcome(listName, reg.SubscribeToken).Sync();
            Assert.AreEqual(1, sentMailCount);
            sentMailCount = 0;

            // validate there is an unsubscribe in the db
            using (var db = new MailDemonDatabase())
            {
                reg = db.Subscriptions.FirstOrDefault();
                Assert.AreEqual(listName, reg.ListName);
                Assert.AreEqual("127.0.0.1", reg.IPAddress);
                Assert.IsNotNull(reg.UnsubscribeToken);
                Assert.AreNotEqual(default(DateTime), reg.SubscribedDate);
                Assert.AreEqual(default(DateTime), reg.UnsubscribedDate);
                Assert.AreEqual(DateTime.MaxValue, reg.Expires);
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
                MailListSubscription reg = db.Subscriptions.FirstOrDefault();
                Assert.AreNotEqual(default(DateTime), reg.UnsubscribedDate);
            }
        }

        [SetUp]
        public void Setup()
        {
            Cleanup();
            using (var db = new MailDemonDatabase())
            {
                db.Initialize();
                MailList list = new MailList
                {
                    Name = listName,
                    Company = company,
                    PhysicalAddress = fullAddress,
                    Title = listName + " title",
                    FromEmailAddress = fromAddress,
                    FromEmailName = fromName,
                    Website = website
                };
                MailTemplate template = new MailTemplate
                {
                    Name = templateName,
                    LastModified = DateTime.UtcNow,
                    Text = templateText,
                    Title = "confirm"
                };
                MailTemplate template2 = new MailTemplate
                {
                    Name = templateName2,
                    LastModified = DateTime.UtcNow,
                    Text = templateText2,
                    Title = "welcome"
                };
                db.Lists.Add(list);
                db.Templates.Add(template);
                db.Templates.Add(template2);
                db.SaveChanges();
            }
            mailCreator = new MailCreator(new RazorRenderer(AppDomain.CurrentDomain.BaseDirectory)) { IgnoreElements = authority };
            homeController = new HomeController(new MailDemonDatabase(), mailCreator, this, null, this)
            {
                RequireCaptcha = false,
                TempData = new TempDataDictionary(httpContext, this)
            };
            homeController.ControllerContext.HttpContext = httpContext;
            httpContext.Request.Headers["User-Agent"] = "Test";
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
            httpContext.Request.Scheme = scheme;
            httpContext.Request.Host = new HostString(domainName);
        }

        [TearDown]
        public void TearDown()
        {
            Cleanup();
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
                Assert.AreEqual(0, db.Subscriptions.Count());
            }
        }

        IDictionary<string, object> ITempDataProvider.LoadTempData(HttpContext context)
        {
            return tempData;
        }

        void ITempDataProvider.SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            
        }

        string IAuthority.Authority
        {
            get { return authority; }
        }

        Task IMailSender.SendMailAsync(string toDomain, IEnumerable<MailToSend> messages)
        {
            MailToSend mail = messages.FirstOrDefault();
            MimeMessage message = mail?.Message;
            Assert.NotNull(message);
            string expectedBodyReplaced = expectedBody.Replace("{subscribe-token}", mail.Subscription.SubscribeToken).Replace("{unsubscribe-token}", mail.Subscription.UnsubscribeToken);
            Assert.AreEqual(expectedSubject, message.Subject);
            Assert.IsTrue(message.HtmlBody.IndexOf(expectedBodyReplaced) >= 0);
            mail.Callback?.Invoke(mail.Subscription, string.Empty);
            sentMailCount++;
            return Task.CompletedTask;
        }
    }
}
