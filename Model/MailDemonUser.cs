﻿using System;
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
        /// <param name="password">Password</param>
        /// <param name="address">Full email address</param>
        /// <param name="forwardAddress">Forward address</param>
        public MailDemonUser(string name, string password, string address, string forwardAddress)
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
            Password = new SecureString();
            foreach (char c in password)
            {
                Password.AppendChar(c);
            }
            PasswordPlain = new SecureString();
            foreach (char c in string.Format("\0{0}\0{1}", name, password))
            {
                PasswordPlain.AppendChar(c);
            }
            Address = (address ?? string.Empty).Trim();
            ForwardAddress = (forwardAddress ?? string.Empty).Trim();
        }

        /// <summary>
        /// User name
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// User password
        /// </summary>
        public SecureString Password { get; private set; }

        /// <summary>
        /// User login+password for plain authentication
        /// </summary>
        public SecureString PasswordPlain { get; private set; }

        /// <summary>
        /// Full email address
        /// </summary>
        public string Address { get; private set; }

        /// <summary>
        /// Forwarding email address
        /// </summary>
        public string ForwardAddress { get; private set; }
    }
}
