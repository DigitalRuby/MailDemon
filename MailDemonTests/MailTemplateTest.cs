#region Imports

using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using MailDemon;

using RazorLight;
using RazorLight.Caching;

#endregion Imports

namespace MailDemonTests
{
    public class MailTemplateTests
    {
        private static readonly MailListRegistration model = new MailListRegistration { Fields = new Dictionary<string, object> { { "firstName", "Bob" } } };
        private RazorLightEngine engine;

        [SetUp]
        public void Setup()
        {
            TearDown();
            MailDemonDatabase.DatabaseOptions = "Journal=false; Flush=true;";
            RazorLightEngineBuilder builder = new RazorLightEngineBuilder();
            builder.AddDefaultNamespaces("System", "System.IO", "System.Text", "MailDemon");
            builder.UseCachingProvider(new MemoryCachingProvider());
            builder.UseProject(new MailDemonRazorLightDatabaseProject(Directory.GetCurrentDirectory()));
            engine = builder.Build();
        }

        [TearDown]
        public void TearDown()
        {
            MailDemonDatabase.DeleteDatabase();
        }

        [Test]
        public void TestTemplateCacheDatabase()
        {
            MailTemplate template = new MailTemplate { Name = "test", Template = "<b>Hello World</b> @Model.Fields[\"firstName\"].ToString()".ToUtf8Bytes() };

            using (var db = new MailDemonDatabase())
            {
                db.Insert<MailList>(new MailList { Name = "test" });
                db.Insert<MailTemplate>(template);
            }

            var found = engine.TemplateCache.RetrieveTemplate("test");
            string html;
            if (found.Success)
            {
                Assert.Fail("Template should not be cached initially");
            }
            else
            {
                html = engine.CompileRenderAsync("test", model).Sync();
                Assert.AreEqual("<b>Hello World</b> Bob", html);
            }

            found = engine.TemplateCache.RetrieveTemplate("test");
            if (!found.Success)
            {
                Assert.Fail("Template should be cached after compile");
            }
            html = engine.RenderTemplateAsync(found.Template.TemplatePageFactory(), model).Sync();
            Assert.AreEqual("<b>Hello World</b> Bob", html);

            template.Template = ((template.Template.ToUtf8String()) + " <br/>New Line<br/>").ToUtf8Bytes();
            template.Dirty = true;

            using (var db = new MailDemonDatabase())
            {
                db.Update(template);
            }

            var found2 = engine.TemplateCache.RetrieveTemplate("test");

            // template should not be cached
            if (found2.Success)
            {
                Assert.Fail("Template should not be cached after modification");
            }
            else
            {
                html = engine.CompileRenderAsync("test", model).Sync();
                Assert.AreEqual("<b>Hello World</b> Bob <br/>New Line<br/>", html);
            }
        }

        [Test]
        public void TestTemplateCacheFile()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "test.cshtml");
            File.WriteAllText(path, "<b>Hello World</b> @Model.Fields[\"firstName\"].ToString()");
            var found = engine.TemplateCache.RetrieveTemplate(path);
            string html;
            if (found.Success)
            {
                Assert.Fail("Template should not be cached initially");
            }
            else
            {
                html = engine.CompileRenderAsync(path, model).Sync();
                Assert.AreEqual("<b>Hello World</b> Bob", html);
            }

            found = engine.TemplateCache.RetrieveTemplate(path);
            if (!found.Success)
            {
                Assert.Fail("Template should be cached after compile");
            }
            html = engine.RenderTemplateAsync(found.Template.TemplatePageFactory(), model).Sync();
            Assert.AreEqual("<b>Hello World</b> Bob", html);

            File.AppendAllText(path, " <br/>New Line<br/>");

            var found2 = engine.TemplateCache.RetrieveTemplate(path);

            // template should not be cached
            if (found2.Success)
            {
                Assert.Fail("Template should not be cached after modification");
            }
            else
            {
                html = engine.CompileRenderAsync(path, model).Sync();
                Assert.AreEqual("<b>Hello World</b> Bob <br/>New Line<br/>", html);
            }
        }
    }
}