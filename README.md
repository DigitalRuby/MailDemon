# & Mail Demon &

Mail Demon is a simple and lightweight C# smtp server for sending unlimited emails and text messages. With a focus on simplicity, async and performance, you'll be able to easily send thousands of messages per second even on a cheap Linux VPS. Memory usage and CPU usage are optimized to the max. Security and spam prevention is also built in using SPF validation.

Mail Demon requires .NET core 2.2+ installed, or you can build a stand-alone executable to remove this dependancy.

Make sure to set appsettings.json to your required parameters before attempting to use.

Thanks to MimeKit and MailKit for their tremendous codebase.

Mail Demon is great for sending notifications, announcements and even text messages. See <a href='http://smsemailgateway.com/'>SMS Email Gateway</a> for more information on text messages.

Setup Instructions:
- Download code, open in Visual Studio, set release configuration.
- Update appsettings.json with your settings. I recommend an SSL certificate. Lets encrypt is a great option. Make sure to set the users to something other than the default.
- Right click on project, select 'publish' option.
- Find the publish folder (right click on project and open in explorer), then browse to bin/release/publish/netcoreapp and make sure it looks good.
- If you don't want to install .NET core, set your publish profile to "self contained".
- FTP or copy files to your server.
- For Windows, use Firedaemon and set the command to run your .dll or .exe from your publish step.
- For Linux setup a service:

```
sudo nano /lib/systemd/system/MailDemon.service
[Unit]
Description=Mail Demon Service
After=network.target

[Service]
ExecStart=/usr/bin/dotnet /root/MailDemon/MailDemon.dll
Restart=on-failure

[Install]
WantedBy=multi-user.target

sudo systemctl daemon-reload 
sudo systemctl enable MailDemon
sudo systemctl start MailDemon 
systemctl status MailDemon
```

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

Enjoy!

--Jeff
