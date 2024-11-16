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
using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using NUnit.Framework.Legacy;

#endregion Imports

namespace MailDemonTests
{
    public class MailRegistrationTest : ITempDataProvider, IMailSender, IAuthority, IMailDemonDatabaseProvider
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
        private IMailDemonDatabaseProvider dbProvider;

        private void Cleanup()
        {
            dbProvider = null;
            homeController?.Dispose();
            homeController = null;
            MailDemonDatabase.DeleteDatabase(true);
            sentMailCount = 0;
            mailCreator = null;
        }

        private string Subscribe()
        {
            using (var db = dbProvider.GetDatabase())
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
            using (var db = dbProvider.GetDatabase())
            {
                reg = db.Subscriptions.FirstOrDefault();
                ClassicAssert.NotNull(reg);
                ClassicAssert.AreEqual("Bob", reg.Fields["firstName"]);
                ClassicAssert.AreEqual("Smith", reg.Fields["lastName"]);
                ClassicAssert.AreEqual(mailAddress, reg.EmailAddress);
                ClassicAssert.IsEmpty(reg.Fields["company"] as string);
                ClassicAssert.IsNotNull(reg.SubscribeToken);
                ClassicAssert.IsNull(reg.UnsubscribeToken);
                ClassicAssert.AreEqual(default(DateTime), reg.SubscribedDate);
                ClassicAssert.AreEqual(default(DateTime), reg.UnsubscribedDate);
                ClassicAssert.AreNotEqual(default(DateTime), reg.Expires);
            }

            // verify the subscribe confirm has no errors
            homeController.SubscribeConfirm(listName);
            ClassicAssert.AreEqual(1, sentMailCount);
            sentMailCount = 0;

            // perform the final subscribe action
            expectedSubject = "Hello2 Bob";
            expectedBody = "<!-- Subject: Hello2 Bob --> Body2: Last Name Smith, Subscribe: , Unsubscribe: https://testdomain.com/Unsubscribe/TestList?token={unsubscribe-token}";
            homeController.SubscribeWelcome(listName, reg.SubscribeToken).Sync();
            ClassicAssert.AreEqual(1, sentMailCount);
            sentMailCount = 0;

            // validate there is an unsubscribe in the db
            using (var db = dbProvider.GetDatabase())
            {
                reg = db.Subscriptions.FirstOrDefault();
                ClassicAssert.AreEqual(listName, reg.ListName);
                ClassicAssert.AreEqual("127.0.0.1", reg.IPAddress);
                ClassicAssert.IsNotNull(reg.UnsubscribeToken);
                ClassicAssert.AreNotEqual(default(DateTime), reg.SubscribedDate);
                ClassicAssert.AreEqual(default(DateTime), reg.UnsubscribedDate);
                ClassicAssert.AreEqual(DateTime.MaxValue, reg.Expires);
            }

            return reg.UnsubscribeToken;
        }

        private void Unsubscribe(string unsubscribeToken)
        {
            // now unsubscribe
            homeController.Unsubscribe(listName, unsubscribeToken);

            // validate that we are unsubscribed
            using var db = dbProvider.GetDatabase();
            MailListSubscription reg = db.Subscriptions.FirstOrDefault();
            ClassicAssert.AreNotEqual(default(DateTime), reg.UnsubscribedDate);
        }

        [SetUp]
        public void Setup()
        {
            Cleanup();
            dbProvider = this as IMailDemonDatabaseProvider;
            using (var db = dbProvider.GetDatabase())
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
            mailCreator = new MailCreator(new RazorRenderer(null, Directory.GetCurrentDirectory(), Assembly.GetExecutingAssembly())) { IgnoreElements = authority };
            homeController = new HomeController(this, null, mailCreator, this, null, this)
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
            using var db = dbProvider.GetDatabase();
            ClassicAssert.AreEqual(0, db.Subscriptions.Count());
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

        Task IMailSender.SendMailAsync(IReadOnlyCollection<MailToSend> messages, bool synchronous)
        {
            MailToSend mail = null;
            foreach (MailToSend msg in messages)
            {
                mail = msg;
                break;
            }
            MimeMessage message = mail?.Message;
            ClassicAssert.NotNull(message);
            string expectedBodyReplaced = expectedBody.Replace("{subscribe-token}", mail.Subscription.SubscribeToken).Replace("{unsubscribe-token}", mail.Subscription.UnsubscribeToken);
            ClassicAssert.AreEqual(expectedSubject, message.Subject);
            ClassicAssert.IsTrue(message.HtmlBody.IndexOf(expectedBodyReplaced) >= 0);
            mail.Callback?.Invoke(mail.Subscription, string.Empty);
            sentMailCount++;

            return Task.CompletedTask;
        }

        MailDemonDatabase IMailDemonDatabaseProvider.GetDatabase(Microsoft.Extensions.Configuration.IConfiguration config)
        {
            return new MailDemonDatabase();
        }
    }
}
