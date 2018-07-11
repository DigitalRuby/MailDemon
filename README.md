# & Mail Demon &

Mail Demon is a simple and lightweight C# smtp server for sending unlimited emails and text messages. With a focus on simplicity and performance, you'll be able to easily send thousands of messages per second even on a cheap Linux VPS.

Mail Demon requires .NET core 2.1 installed, or you can build a stand-alone executable to remove this dependancy.

Make sure to set appsettings.json to your required parameters before attempting to use.

Thanks to MimeKit and MailKit for their tremendous codebase.

Mail Demon is great for sending notifications, announcements and even text messages. See <a href='http://smsemailgateway.com/'>SMS Email Gateway</a> for more information on text messages.

Setup Instructions:
- Download code, open in Visual Studio, set release configuration.
- Update appsettings.json with your settings. I recommend an SSL certificate. Lets encrypt is a great option.
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
  - Setup A record: @ or smtp or email, etc.
- Setup reverse dns for your ip address to your A record. Your hosting provider should have a way to do this.

Known Issues:
- BDAT and BINARYMIME extensions not implemented yet.
- Hotmail.com, live.com and outlook.com have had an invalid SSL certificate for quite a while now. I've added them to appsettings.json. You may need to add additional entries for mail services with bad certificates.

Enjoy!

--Jeff
