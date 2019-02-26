#region Imports

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using NUnit.Framework;

using MailDemon;

using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Http;

#endregion Imports

namespace MailDemonTests
{
    public class MailRegistrationTest : ITempDataProvider
    {
        private readonly HomeController homeController = new HomeController(new MailDemonDatabase());
        private readonly HttpContext httpContext = new DefaultHttpContext();
        private readonly Dictionary<string, object> tempData = new Dictionary<string, object>();

        private void Cleanup()
        {
            MailDemonDatabase.DeleteDatabase();
        }

        private string Subscribe()
        {
            homeController.SubscribePost("TestList", new Dictionary<string, object>
            {
                { "ff_firstName", "Bob" },
                { "ff_lastName", "Smith" },
                { "ff_emailAddress", "testemail@domain.com" },
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
                Assert.AreEqual("testemail@domain.com", reg.EmailAddress);
                Assert.IsEmpty(reg.Fields["company"] as string);
                Assert.IsNotNull(reg.SubscribeToken);
                Assert.IsNull(reg.UnsubscribeToken);
                Assert.AreEqual(default(DateTime), reg.SubscribedDate);
                Assert.AreEqual(default(DateTime), reg.UnsubscribedDate);
            }

            // verify the signup confirm has no errors
            homeController.SubscribeConfirm("TestList");

            // perform the final subscribe action
            homeController.SubscribeSuccess("TestList", reg.SubscribeToken);

            // validate there is an unsubscribe in the db
            using (var db = new MailDemonDatabase())
            {
                reg = db.Select<MailListRegistration>().FirstOrDefault();
                Assert.AreEqual("TestList", reg.ListName);
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
            homeController.Unsubscribe("TestList", unsubscribeToken);

            // validate that we are unsubscribed
            using (var db = new MailDemonDatabase())
            {
                MailListRegistration reg = db.Select<MailListRegistration>().FirstOrDefault();
                Assert.AreNotEqual(default(DateTime), reg.UnsubscribedDate);
            }
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
            homeController.SubscribePost("TestList", new Dictionary<string, object>
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
    }
}
