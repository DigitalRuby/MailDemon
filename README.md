# & Mail Demon &

Mail Demon is a simple and lightweight C# smtp server and mail list system for sending unlimited emails and text messages. With a focus on simplicity, async and performance, you'll be able to easily send thousands of messages per second even on a cheap Linux VPS. Memory usage and CPU usage are optimized to the max. Security and spam prevention is also built in using SPF validation.

Mail Demon requires .NET core 2.2+ installed, or you can build a stand-alone executable to remove this dependancy.

Make sure to set appsettings.json to your required parameters before attempting to use.

Thanks to MimeKit and MailKit for their tremendous codebase.

Mail Demon is great for sending notifications, announcements and even text messages. See <a href='http://smsemailgateway.com/'>SMS Email Gateway</a> for more information on text messages.

## IPBan Integration
Mail Demon is integrated with IPBan - https://github.com/DigitalRuby/IPBan. If you have installed IPBan on your Linux or Windows box, then Mail Demon will send failed login attempts from SMTP or the mail list login to IPBan via a custom log file, blocking those attackers that exceed the failed login threshold for IPBan.

## Project Setup Instructions
- Download code, open in Visual Studio or VS Code, set release configuration.
- Update appsettings.json with your settings. I recommend an SSL certificate. Lets encrypt is a great option. Make sure to set the users to something other than the default.
- Right click on project, select 'publish' option.
- Find the publish folder (right click on project and open in explorer), then browse to bin/release/publish/netcoreapp and make sure it looks good.
- If you don't want to install .NET core, set your publish profile to "self contained".
- FTP or copy files to your server.
- For Windows, use Firedaemon and set the command to run your .dll or .exe from your publish step.
- For Linux setup a service (put binaries in /opt/MailDemon):

```
sudo nano /lib/systemd/system/MailDemon.service
```

```
[Unit]
Description=Mail Demon Service
After=network.target

[Service]
WorkingDirectory=/opt/MailDemon
ExecStart=/usr/bin/dotnet /opt/MailDemon/MailDemon.dll
Restart=on-failure

[Install]
WantedBy=multi-user.target

sudo systemctl daemon-reload 
sudo systemctl enable MailDemon
sudo systemctl start MailDemon
sudo systemctl enable MailDemon
systemctl status MailDemon
```

## Smtp Setup Instructions
- Ensure you have setup DNS for your domain (TXT, A and MX record)
  - Setup SPF record: v=spf1 mx -all
  - Setup MX record: @ or smtp or email, etc.
  - Setup A and/or AAAA record: @ or smtp or email, etc.
  - Setup DMARC record, https://en.wikipedia.org/wiki/DMARC
  - Setup DKIM, https://en.wikipedia.org/wiki/DomainKeys_Identified_Mail
- Setup reverse dns for your ip address to your A and/or AAAA record. Your hosting provider should have a way to do this.

Supported smtp extensions:
- 250-SIZE
- 250-8BITMIME
- 250-AUTH PLAIN
- 250-PIPELINING
- 250-ENHANCEDSTATUSCODES
- 250-BINARYMIME
- 250-CHUNKING
- 250-STARTTLS
- 250 SMTPUTF8

Known Issues:
- Hotmail.com, live.com and outlook.com have had an invalid SSL certificate for quite a while now. I've added them to appsettings.json. You may need to add additional entries for mail services with bad certificates.

## Mail List Setup

Mail Demon contains an integrated mail list management website and mail list sending service. In order to use this service, you must setup your `appsettings.json` file and make some additional optional customization.

- Make sure your smtp settings are correct in `appsettings.json`.
- Setup `appsettings.json`, `mailDemonWeb section`.
  - Set `enableWeb` to true.
  - Set the authority to your scheme and host, i.e. https://yourdomain.com.
  - Set your admin user/password.
  - Set google recaptcha keys (https://www.google.com/recaptcha).
  - Set ssl certificate (.pem public and private files along with password).
- Use `--server.urls` parameter to set the kestrel binding for the web server.
- Login with https://yourdomain.com/MailDemonLogin. Replace yourdomain.com with your actual domain name. Use the admin user/password from the `appsettings.json` file. Nothing will show up until you login.
- Create a new mailing list using menu at top.
- List name is meant to be more like a short variable name, somewhat human readable, but short and unique. List title is what subscribers will see.
- Send your victims, I mean subscribers, to https://yourdomain.com/SubscribeInitial/[listname]. Replace yourdomain.com with your actual domain name. Replace [listname] with the actual list name.
- Create new templates by selecting lists at the top, then using create template button.
- The template name format is `[listName]@[templateName]` (without brackets). Just like lists, the template name is a short, human readable and unique name.
- The template title is NOT the subject of the email, it is just informational for you only.
- Full razor syntax, `@Html`, etc. is supported. The model for the templates is the MailListSubscription class.
- Feel free to create and edit templates in visual studio and then paste them into the template text box.
- Each template should have a layout. A layout is a template that you will never email, it just wraps other templates. You can name your layout `[listName]@[layoutName]` (without brackets). You can start with `_LayoutMail.cshtml` and customize and provide your own css link. You should also provide an unsubscribe link, along with a physical mailing address to comply with anti-spam laws.
- Set the layout of your template like this: `@{ Layout = "listName@layoutName"; }`
- To set the email subject, add a `<!-- Subject: ... -->` to the body of your template, it will then be set as the email subject. This is required in order to send email. See `SubscribeConfirmDefault.cshtml` for an example.
- To bulk send email from a mail list, select (or create) the template from the list to send, edit it, add your subject and save. Then use the send button to perform the bulk email operation. Errors will be logged.
- There are three magic template names that can override the default behavior for a list:
  - SubscribeInitial (see SubscribeInitialDefault.cshtml). This is the initial sign-up form.
  - SubscribeConfirm (see SubscribeConfirmDefault.cshtml). This is the confirmation email with a link to activate the subscription.
  - SubscribeWelcome (see SubscribeWelcomeDefault.cshtml). This is the welcome email to notify of the active subscription, along with an unsubscribe link.
- Note that the MailDemon.db file contains all the lists, templates, subscribers, etc. Backup this file regularly!
- You can also store your templates in the Views/Shared directory. Follow the same naming convention for a template name 

## Database
Mail Demon uses sqlite by default with entity framework. In the future, adding MySQL, SQL Server and Azure are desired. The default database name is 'MailDemon.sqlite'. Mail Demon used to use LiteDB, but I ran into data corruption and other strange exceptions, so if 'MailDemon.db' file exists when the application starts, it will be migrated into 'MailDemon.sqlite'.

Enjoy!

--Jeff
