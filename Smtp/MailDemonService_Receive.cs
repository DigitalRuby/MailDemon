using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using DnsClient;

using MimeKit;

using NetTools;

namespace MailDemon
{
    public partial class MailDemonService
    {
        //private static readonly Regex ipRegex = new Regex(@"ip[46]\:(?<ip>[^ ]+) ?", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
        //private static readonly Regex domainRegex = new Regex(@"include\:(?<domain>[^ ]+) ?", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Walk SPF records until a valid value is found. Two levels deep walk.
        /// </summary>
        /// <param name="writer">Stream writer</param>
        /// <param name="connectionEndPoint">Connected end point</param>
        /// <param name="fromAddress">Mail address from</param>
        /// <param name="fromAddressDomain">Mail address from domain</param>
        /// <returns>Task</returns>
        /// <exception cref="InvalidOperationException">SPF fails to validate</exception>
        public static async Task ValidateSPF(StreamWriter writer, IPEndPoint connectionEndPoint, string fromAddress, string fromAddressDomain)
        {
            MailDemonLog.Write(LogLevel.Info, "Validating spf for end point {0}, from address: {1}, from domain: {2}", connectionEndPoint.Address, fromAddress, fromAddressDomain);

            // example smtp host: mail-it1-f173.google.com
            IPHostEntry entry = await Dns.GetHostEntryAsync(connectionEndPoint.Address);

            var spfValidator = new ARSoft.Tools.Net.Spf.SpfValidator();
            ARSoft.Tools.Net.Spf.ValidationResult result = await spfValidator.CheckHostAsync(connectionEndPoint.Address, fromAddressDomain, fromAddress);

            if (result.Result == ARSoft.Tools.Net.Spf.SpfQualifier.Pass)
            {
                return;
            }
            else if (result.Result == ARSoft.Tools.Net.Spf.SpfQualifier.None)
            {
                // no spf record... what to do?
                // TODO: Maybe email back to the address and tell them to setup SPF records...?
            }

            /*
            LookupClient lookup = new LookupClient();
            IDnsQueryResponse dnsQueryRoot = await lookup.QueryAsync(fromAddressDomain, QueryType.TXT);
            foreach (var record in dnsQueryRoot.AllRecords)
            {
                MatchCollection ipMatches = ipRegex.Matches(record.ToString());
                foreach (Match ipMatch in ipMatches)
                {
                    if (IPAddressRange.TryParse(ipMatch.Groups["ip"].Value, out IPAddressRange testIp))
                    {
                        foreach (IPAddress ip in entry.AddressList)
                        {
                            if (testIp.Contains(ip))
                            {
                                // good
                                return;
                            }
                        }
                    }
                }

                MatchCollection matches = domainRegex.Matches(record.ToString());
                foreach (Match match in matches)
                {
                    string domainHost = match.Groups["domain"].Value;
                    IDnsQueryResponse dnsQuery = await lookup.QueryAsync(domainHost, QueryType.TXT);
                    foreach (var record2 in dnsQuery.AllRecords)
                    {
                        MatchCollection ipMatches2 = ipRegex.Matches(record2.ToString());
                        foreach (Match ipMatch in ipMatches2)
                        {
                            if (IPAddressRange.TryParse(ipMatch.Groups["ip"].Value, out IPAddressRange testIp))
                            {
                                foreach (IPAddress ip in entry.AddressList)
                                {
                                    if (testIp.Contains(ip))
                                    {
                                        // good
                                        return;
                                    }
                                }
                            }
                        }

                        MatchCollection matches2 = domainRegex.Matches(record2.ToString());
                        foreach (Match match2 in matches2)
                        {
                            string domainHost2 = match2.Groups["domain"].Value;
                            IDnsQueryResponse dnsQuery3 = await lookup.QueryAsync(domainHost2, QueryType.TXT);
                            foreach (var record3 in dnsQuery3.AllRecords)
                            {
                                MatchCollection ipMatches3 = ipRegex.Matches(record3.ToString());
                                foreach (Match ipMatch in ipMatches3)
                                {
                                    if (IPAddressRange.TryParse(ipMatch.Groups["ip"].Value, out IPAddressRange testIp))
                                    {
                                        foreach (IPAddress ip in entry.AddressList)
                                        {
                                            if (testIp.Contains(ip))
                                            {
                                                // good
                                                return;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            */

            if (writer != null)
            {
                await writer.WriteLineAsync($"500 invalid command - SPF records from mail domain '{fromAddressDomain}' do not match connection host '{entry.HostName}'");
            }
            throw new InvalidOperationException($"SPF validation failed for host '{entry.HostName}', address domain '{fromAddressDomain}'");
        }

        private async Task ReceiveMail(Stream reader, StreamWriter writer, string line, IPEndPoint endPoint)
        {
            IPHostEntry entry = await Dns.GetHostEntryAsync(endPoint.Address);
            using (MailFromResult result = await ParseMailFrom(null, reader, writer, line, endPoint))
            {
                // mail demon doesn't have an inbox yet, only forwarding, so see if any of the to addresses can be forwarded
                foreach (var kv in result.ToAddresses)
                {
                    foreach (string address in kv.Value)
                    {
                        MailDemonUser user = users.FirstOrDefault(u => u.Address == address);

                        // if no user or the forward address points to a user, fail
                        if (user == null || users.FirstOrDefault(u => u.Address == user.ForwardAddress) != null)
                        {
                            await writer.WriteLineAsync($"500 invalid command - cannot forward");
                            await writer.FlushAsync();
                        }

                        // setup forward headers
                        string forwardToAddress = (string.IsNullOrWhiteSpace(user.ForwardAddress) ? globalForwardAddress : user.ForwardAddress);
                        if (string.IsNullOrWhiteSpace(forwardToAddress))
                        {
                            await writer.WriteLineAsync($"500 invalid command - cannot forward 2");
                            await writer.FlushAsync();
                        }
                        else
                        {
                            string forwardDomain = forwardToAddress.Substring(forwardToAddress.IndexOf('@') + 1);

                            // create new object to forward on
                            MailFromResult newResult = new MailFromResult
                            {
                                Stream = result.Stream,
                                From = user.MailAddress,
                                Message = result.Message,
                                ToAddresses = new Dictionary<string, List<string>> { { forwardDomain, new List<string> { forwardToAddress } } }
                            };

                            newResult.Message.Subject = $"FW from {result.From}: {result.Message.Subject}";
                            newResult.Message.Cc.Clear();
                            newResult.Message.Bcc.Clear();
                            newResult.Message.ResentFrom.Add(result.From);

                            // forward the message on and clear the forward headers
                            MailDemonLog.Write(LogLevel.Info, "Forwarding message, from: {0}, to: {1}, forward: {2}", result.From, address, forwardToAddress);
                            result.Stream = null; // we took ownership of the stream
                            SendMail(writer, newResult, endPoint, true).GetAwaiter();
                            return; // only forward to the first valid address
                        }
                    }
                }
            }
        }
    }
}
