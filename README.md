# & MailDemon &

Mail Demon is a simple and lightweight C# smtp server. Code is simple and easy to follow.

After fussing with hMailServer on Windows and PostFix on Linux and being very frustrated with both, I decided to write my own smtp server. Now you can send messages with ease.

MailDemon requires .NET core 2.1 installed, or you can build a stand-alone executable to remove this dependancy.

Make sure to set appsettings.json to your required parameters or things will not work.

The code is the documentation right now, see MailDemon.cs and MailDemonApp.cs.

Thanks to MimeKit and MailKit for their tremendous codebase.

Known Issues:
- Still trying to get CHUNKING to initiate properly
- Hotmail.com, live.com and outlook.com have had an invalid SSL certificate for quite a while now. I've added them to appsettings.json. You may need to add additional entries for mail services with bad certificates.

Enjoy!

--Jeff