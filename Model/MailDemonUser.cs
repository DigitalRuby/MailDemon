﻿using DnsClient.Internal;

using Microsoft.Extensions.Logging;

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
        /// <param name="validateUserNameAndPassword">True to validate user name and password, false otherwise</param>
        public MailDemonUser(string name, string displayName, string password, string address, string forwardAddress, bool authenticated, bool validateUserNameAndPassword = true)
        {
            if (validateUserNameAndPassword)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ArgumentException("Name must not be null or empty", nameof(name));
                }
                if (string.IsNullOrWhiteSpace(password))
                {
                    throw new ArgumentException("Password must not be null or empty", nameof(password));
                }
            }
            UserName = (name ?? string.Empty).Trim();
            DisplayName = (string.IsNullOrWhiteSpace(displayName) ? UserName : displayName);
            Password = (password ?? string.Empty).ToSecureString();
            if (!string.IsNullOrWhiteSpace(forwardAddress))
            {
                ForwardAddress = MailboxAddress.Parse(forwardAddress);
            }
            Authenticated = authenticated;
            MailAddress = new MailboxAddress(DisplayName, address);
        }

        /// <summary>
        /// Authenticate a user
        /// </summary>
        /// <param name="userName">User name</param>
        /// <param name="password">Password</param>
        /// <param name="logger">Logger</param>
        /// <returns>True if authenticated, false otherwise</returns>
        public bool Authenticate(string userName, string password, Microsoft.Extensions.Logging.ILogger logger)
        {
            logger.LogDebug("Attempting auth for {user}", userName);
            return (UserName.Equals(userName, StringComparison.OrdinalIgnoreCase) &&
                Password.ToUnsecureString().Equals(password, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// ToString
        /// </summary>
        /// <returns>String</returns>
        public override string ToString()
        {
            string password = Password.ToUnsecureString();
            return $"User Name: {UserName}, Display Name: {DisplayName}, Address: {MailAddress}, Forward: {ForwardAddress}, Password: {password}";
        }

        /// <summary>
        /// User name
        /// </summary>
        public string UserName { get; private set; }

        /// <summary>
        /// Display name
        /// </summary>
        public string DisplayName { get; private set; }

        /// <summary>
        /// User password
        /// </summary>
        public SecureString Password { get; private set; }

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
