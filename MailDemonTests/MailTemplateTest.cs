#region Imports

using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using MailDemon;

#endregion Imports

namespace MailDemonTests
{
    public class MailTemplateTests
    {
        private static readonly MailListSubscription model = new MailListSubscription { FirstName = "Bob", LastName = "Smith", EmailAddress = "bobsmith@anotherdomain.com", Company = "Fake Company" };
        private RazorRenderer viewRenderer;

        [SetUp]
        public void Setup()
        {
            TearDown();
            viewRenderer = new RazorRenderer(Path.Combine(Directory.GetCurrentDirectory(), "../../.."));
        }

        [TearDown]
        public void TearDown()
        {
            MailDemonDatabaseLiteDB.DeleteDatabase(true);
        }

        [Test]
        public void TestTemplateCacheDatabase()
        {
            MailTemplate template = new MailTemplate { Name = "test", Text = "<b>Hello World</b> @Model.FirstName" };

            using (var db = new MailDemonDatabaseLiteDB())
            {
                db.Insert<MailList>(new MailList { Name = "test" });
                db.Insert<MailTemplate>(template);
            }

            string html = viewRenderer.RenderViewToStringAsync("test", model, isMainPage: false).Sync();
            Assert.AreEqual("<b>Hello World</b> Bob", html);
            html = viewRenderer.RenderViewToStringAsync("test", model, isMainPage: false).Sync();
            Assert.AreEqual("<b>Hello World</b> Bob", html);

            template.Text += " <br/>New Line<br/>";
            template.LastModified = DateTime.UtcNow;
            template.Dirty = true;

            using (var db = new MailDemonDatabaseLiteDB())
            {
                db.Update(template);
            }

            html = viewRenderer.RenderViewToStringAsync("test", model, isMainPage: false).Sync();
            Assert.AreEqual("<b>Hello World</b> Bob <br/>New Line<br/>", html);
            html = viewRenderer.RenderViewToStringAsync("test", model, isMainPage: false).Sync();
            Assert.AreEqual("<b>Hello World</b> Bob <br/>New Line<br/>", html);
        }

        [Test]
        public void TestTemplateCacheFile()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "test.cshtml");
            try
            {
                File.WriteAllText(path, "<b>Hello World</b> @Model.FirstName");
                string html = viewRenderer.RenderViewToStringAsync(path, model, isMainPage: false).Sync();
                Assert.AreEqual("<b>Hello World</b> Bob", html);
                html = viewRenderer.RenderViewToStringAsync(path, model, isMainPage: false).Sync();
                Assert.AreEqual("<b>Hello World</b> Bob", html);

                File.AppendAllText(path, " <br/>New Line<br/>");

                html = viewRenderer.RenderViewToStringAsync(path, model, isMainPage: false).Sync();
                Assert.AreEqual("<b>Hello World</b> Bob <br/>New Line<br/>", html);
                html = viewRenderer.RenderViewToStringAsync(path, model, isMainPage: false).Sync();
                Assert.AreEqual("<b>Hello World</b> Bob <br/>New Line<br/>", html);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void TestStringTemplate()
        {
            string result = viewRenderer.RenderStringToStringAsync("test", "Hello @Model.FirstName", model, isMainPage: false).Sync();
            Assert.AreEqual("Hello Bob", result);
        }
    }
}