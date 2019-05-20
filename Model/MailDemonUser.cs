using MimeKit;
using System;
using System.Collections.Generic;
using System.Security;
using System.Text;

namespace MailDemon
{
    /// <summary>
    /// A user
    /// </summary>
    public class MailDemonUser
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="name">Name</param>
        /// <param name="displayName">Display name</param>
        /// <param name="password">Password</param>
        /// <param name="address">Full email address</param>
        /// <param name="forwardAddress">Forward address</param>
        /// <param name="authenticated">Whether the user is authenticated</param>
        public MailDemonUser(string name, string displayName, string password, string address, string forwardAddress, bool authenticated)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Name must not be null or empty", nameof(name));
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentException("Password must not be null or empty", nameof(password));
            }
            Name = name.Trim();
            DisplayName = (string.IsNullOrWhiteSpace(displayName) ? Name : displayName);
            Password = password.ToSecureString();
            PasswordPlain = string.Format("{0}:{1}", name, password).ToSecureString();
            if (!string.IsNullOrWhiteSpace(forwardAddress))
            {
                ForwardAddress = new MailboxAddress(forwardAddress);
            }
            Authenticated = authenticated;
            MailAddress = new MailboxAddress(DisplayName, address);
        }

        /// <summary>
        /// Authenticate against plain auth
        /// </summary>
        /// <param name="authPlain">Plain auth. \0 should be replaced with (null) first.</param>
        /// <returns>True if authenticate, false otherwise</returns>
        public bool Authenticate(string authPlain)
        {
            string passwordPlain = PasswordPlain.ToUnsecureString();
            MailDemonLog.Debug("Attempting auth {0} against user auth {1}", authPlain, passwordPlain);
            return (passwordPlain == authPlain);
        }

        /// <summary>
        /// ToString
        /// </summary>
        /// <returns>String</returns>
        public override string ToString()
        {
            string password = Password.ToUnsecureString();
            string passwordPlain = PasswordPlain.ToUnsecureString();
            return $"Name: {Name}, Display Name: {DisplayName}, Address: {MailAddress}, Forward: {ForwardAddress}, Password: {password}, Password Plain: {passwordPlain}";
        }

        /// <summary>
        /// User name
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Display name
        /// </summary>
        public string DisplayName { get; private set; }

        /// <summary>
        /// User password
        /// </summary>
        public SecureString Password { get; private set; }

        /// <summary>
        /// User login+password for plain authentication
        /// </summary>
        public SecureString PasswordPlain { get; private set; }

        /// <summary>
        /// Email address object
        /// </summary>
        public MailboxAddress MailAddress { get; private set; }

        /// <summary>
        /// Forwarding email address
        /// </summary>
        public MailboxAddress ForwardAddress { get; private set; }

        /// <summary>
        /// Whether the user is authenticated
        /// </summary>
        public bool Authenticated { get; private set; }
    }
}
