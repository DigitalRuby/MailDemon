using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace MailDemon
{
    /// <summary>
    /// Stream that can read a MimeMessage from a base stream using DATA
    /// </summary>
    public class SmtpMimeMessageStream : Stream
    {
        private Stream baseStream;
        private CancellationToken cancelToken;
        private int state; // 0 = none, 1 = has \r, 2 = has \n, 3 = has ., 4 = has \r 5 = has \n done!

        public SmtpMimeMessageStream(Stream baseStream, CancellationToken cancelToken)
        {
            this.baseStream = baseStream;
            this.cancelToken = cancelToken;
        }

        public override bool CanRead => baseStream.CanRead;
        public override bool CanSeek => baseStream.CanSeek;
        public override bool CanWrite => baseStream.CanWrite;
        public override long Length => baseStream.Length;
        public override long Position { get => baseStream.Position; set => baseStream.Position = value; }

        public override void Flush()
        {
            baseStream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (cancelToken.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }
            else if (state == 5)
            {
                state = 0;
                return 0;
            }

            int read = baseStream.Read(buffer, offset, count);
            if (read > 0)
            {
                int end = offset + read;
                for (int i = offset; i < end; i++)
                {
                    switch (state)
                    {
                        case 0:
                            if (buffer[i] == '\r')
                            {
                                state = 1;
                            }
                            else
                            {
                                state = 0;
                            }
                            break;

                        case 1:
                            if (buffer[i] == '\n')
                            {
                                state = 2;
                            }
                            else
                            {
                                state = 0;
                            }
                            break;

                        case 2:
                            if (buffer[i] == '.')
                            {
                                state = 3;
                            }
                            else if (buffer[i] == '\r')
                            {
                                state = 1;
                            }
                            else
                            {
                                state = 0;
                            }
                            break;

                        case 3:
                            if (buffer[i] == '\r')
                            {
                                state = 4;
                            }
                            else
                            {
                                state = 0;
                            }
                            break;

                        case 4:
                            if (buffer[i] == '\n')
                            {
                                state = 5;

                                // don't return the ending \r\n.\r\n, that is part of the protocol
                                // there is a rare chance that a read of this terminator can split over
                                // two Read(...) method calls, in which case the \r\n.\r\n will be
                                // part of the message... this edge case is not handled yet
                                return (read >= 5 ? read - 5 : read);
                            }
                            else
                            {
                                state = 0;
                            }
                            break;
                    }
                }
            }
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return baseStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            baseStream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            baseStream.Write(buffer, offset, count);
        }
    }
}
