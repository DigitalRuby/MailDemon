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
        private static readonly MailListRegistration model = new MailListRegistration { FirstName = "Bob", LastName = "Smith", EmailAddress = "bobsmith@anotherdomain.com", Company = "Fake Company" };
        private RazorRenderer viewRenderer;

        [SetUp]
        public void Setup()
        {
            TearDown();
            viewRenderer = new RazorRenderer();
        }

        [TearDown]
        public void TearDown()
        {
            MailDemonDatabase.DeleteDatabase();
        }

        [Test]
        public void TestTemplateCacheDatabase()
        {
            MailTemplate template = new MailTemplate { Name = "test", Text = "<b>Hello World</b> @Model.Fields[\"firstName\"].ToString()" };

            using (var db = new MailDemonDatabase())
            {
                db.Insert<MailList>(new MailList { Name = "test" });
                db.Insert<MailTemplate>(template);
            }

            string html = viewRenderer.RenderToStringAsync("test", model).Sync();
            Assert.AreEqual("<b>Hello World</b> Bob", html);
            html = viewRenderer.RenderToStringAsync("test", model).Sync();
            Assert.AreEqual("<b>Hello World</b> Bob", html);

            template.Text += " <br/>New Line<br/>";
            template.LastModified = DateTime.UtcNow;
            template.Dirty = true;

            using (var db = new MailDemonDatabase())
            {
                db.Update(template);
            }

            html = viewRenderer.RenderToStringAsync("test", model).Sync();
            Assert.AreEqual("<b>Hello World</b> Bob <br/>New Line<br/>", html);
            html = viewRenderer.RenderToStringAsync("test", model).Sync();
            Assert.AreEqual("<b>Hello World</b> Bob <br/>New Line<br/>", html);
        }

        [Test]
        public void TestTemplateCacheFile()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "test.cshtml");
            try
            {
                File.WriteAllText(path, "<b>Hello World</b> @Model.Fields[\"firstName\"].ToString()");
                string html = viewRenderer.RenderToStringAsync(path, model).Sync();
                Assert.AreEqual("<b>Hello World</b> Bob", html);
                html = viewRenderer.RenderToStringAsync(path, model).Sync();
                Assert.AreEqual("<b>Hello World</b> Bob", html);

                File.AppendAllText(path, " <br/>New Line<br/>");

                html = viewRenderer.RenderToStringAsync(path, model).Sync();
                Assert.AreEqual("<b>Hello World</b> Bob <br/>New Line<br/>", html);
                html = viewRenderer.RenderToStringAsync(path, model).Sync();
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
    }
}