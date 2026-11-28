// TLS front for a LocalDB instance: a loopback TCP endpoint that negotiates TDS 7.x
// encryption with a throwaway self-signed certificate and relays the decrypted stream to
// LocalDB's named pipe. LocalDB cannot encrypt, so this is the cheapest encrypted server
// there is. Test tool only; not in SqlPilot.sln.
//
//   TlsFront <port> <pipe e.g. LOCALDB#5F8A78B0\tsql\query> [file to write the certificate to]
//
// The pipe name is the "Instance pipe name" of `sqllocaldb info <instance>`, without
// "np:\\.\pipe\". Connect as 127.0.0.1,<port> with a SQL login. The certificate is new on
// every start and Windows does not trust it; to test with one it does, pass a file name and
// import that file into a trusted root store for as long as the front runs. See "Testing
// encryption without a SQL Server" in docs/SSMS_INTEGRATION_NOTES.md.
using System;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

static class Program
{
    static void Main(string[] args)
    {
        // Every connection blocks two pool threads; SQL Pilot indexes 20 databases at a time.
        ThreadPool.SetMinThreads(64, 64);

        int port = int.Parse(args[0]);
        string pipe = args[1];
        var cert = MakeCert();
        if (args.Length > 2) File.WriteAllBytes(args[2], cert.Export(X509ContentType.Cert));

        // 127.0.0.1 only: SqlClient turns "localhost" into the machine name, so ::1 is never asked for.
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Log(0, $"listening on 127.0.0.1:{port} -> \\\\.\\pipe\\{pipe}, certificate thumbprint {cert.Thumbprint}");
        for (int id = 1; ; id++)
        {
            var client = listener.AcceptTcpClient();
            int connection = id;
            Task.Run(() => Handle(connection, client, pipe, cert));
        }
    }

    // TLSFRONT_TRACE=1 logs every packet's type and size, both ways.
    static readonly bool Trace = Environment.GetEnvironmentVariable("TLSFRONT_TRACE") == "1";

    // Which program a connection belongs to, and the packet size it asks for.
    static string DescribeLogin(byte[] p)
    {
        try
        {
            int packetSize = BitConverter.ToInt32(p, 8 + 8);
            int ibApp = BitConverter.ToUInt16(p, 8 + 48), cchApp = BitConverter.ToUInt16(p, 8 + 50);
            string app = System.Text.Encoding.Unicode.GetString(p, 8 + ibApp, cchApp * 2);
            return $"from \"{app}\", packet size {packetSize}, option flags 0x{p[8 + 24]:X2} 0x{p[8 + 25]:X2} 0x{p[8 + 26]:X2} 0x{p[8 + 27]:X2}";
        }
        catch { return "(not parsed)"; }
    }

    static void Log(int id, string msg) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} #{id} {msg}");

    // Named for the address clients are told to use, so that once it is trusted the name
    // check passes too. Not a CA: trusting it vouches for this one endpoint and nothing else.
    static X509Certificate2 MakeCert()
    {
        using (var rsa = RSA.Create(2048))
        {
            var req = new CertificateRequest("CN=127.0.0.1", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            san.AddIpAddress(IPAddress.Loopback);
            san.AddDnsName("127.0.0.1");
            req.CertificateExtensions.Add(san.Build());
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
            // SChannel wants the key in a store, hence the round trip through a PFX.
            using (var c = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(30)))
                return new X509Certificate2(c.Export(X509ContentType.Pfx, "x"), "x");
        }
    }

    static byte[] ReadPacket(Stream s)
    {
        var header = new byte[8];
        ReadExact(s, header, 0, 8);
        int len = (header[2] << 8) | header[3];
        if (len < 8) throw new IOException("bad TDS length " + len);
        var packet = new byte[len];
        Buffer.BlockCopy(header, 0, packet, 0, 8);
        ReadExact(s, packet, 8, len - 8);
        return packet;
    }

    static void ReadExact(Stream s, byte[] buf, int off, int count)
    {
        while (count > 0)
        {
            int n = s.Read(buf, off, count);
            if (n <= 0) throw new EndOfStreamException();
            off += n; count -= n;
        }
    }

    // Returns the previous ENCRYPTION option value, or -1 when the option is absent.
    static int PatchEncryption(byte[] packet, byte value)
    {
        for (int i = 8; i + 4 < packet.Length && packet[i] != 0xFF; i += 5)
        {
            if (packet[i] != 0x01) continue;
            int pos = 8 + ((packet[i + 1] << 8) | packet[i + 2]);
            int old = packet[pos];
            packet[pos] = value;
            return old;
        }
        return -1;
    }

    static string EncName(int v)
    {
        switch (v)
        {
            case 0: return "OFF (login only)";
            case 1: return "ON";
            case 2: return "NOT_SUP";
            case 3: return "REQ";
            default: return v.ToString();
        }
    }

    static void Handle(int id, TcpClient client, string pipeName, X509Certificate2 cert)
    {
        NamedPipeClientStream pipe = null;
        try
        {
            client.NoDelay = true;
            var net = client.GetStream();

            var prelogin = ReadPacket(net);
            if (prelogin[0] != 0x12) { Log(id, $"first packet is 0x{prelogin[0]:X2}, not PRELOGIN (strict/TDS 8 client?)"); return; }
            int asked = PatchEncryption(prelogin, 0x02);
            Log(id, $"client PRELOGIN asks encryption {EncName(asked)}");

            pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(5000);
            pipe.Write(prelogin, 0, prelogin.Length);
            var response = ReadPacket(pipe);
            // Client asked ON -> answer ON. Client asked OFF -> answer REQ, as a server
            // with Force Encryption does, so everything after is encrypted either way.
            PatchEncryption(response, asked == 1 ? (byte)0x01 : (byte)0x03);
            net.Write(response, 0, response.Length);

            var wrap = new HandshakeStream(net);
            var ssl = new SslStream(wrap, false);
            try
            {
                ssl.AuthenticateAsServer(cert, false, SslProtocols.Tls12, false);
            }
            catch (Exception ex)
            {
                Log(id, $"TLS handshake FAILED (client rejected the certificate or hung up): {ex.GetType().Name}: {ex.Message}");
                return;
            }
            wrap.Wrap = false;
            // Not yet proof of trust: a client told to match a certificate file checks after the
            // handshake and hangs up. The LOGIN7 line is the proof.
            Log(id, $"TLS handshake ok ({ssl.SslProtocol})");

            bool first = true;
            var up = Task.Run(() =>
            {
                try
                {
                    while (true)
                    {
                        var p = ReadPacket(ssl);
                        if (first) { first = false; Log(id, p[0] == 0x10 ? "LOGIN7 (client accepted the certificate) " + DescribeLogin(p) : $"first packet inside TLS: 0x{p[0]:X2}"); }
                        else if (Trace) Log(id, $"  > type 0x{p[0]:X2} status 0x{p[1]:X2} {p.Length} bytes");
                        pipe.Write(p, 0, p.Length);
                    }
                }
                catch (Exception ex) { if (!(ex is EndOfStreamException)) Log(id, $"client side ended: {ex.GetType().Name}: {ex.Message}"); }
                finally { try { pipe.Dispose(); } catch { } }
            });
            try
            {
                // One TLS record per TDS packet, as SQL Server sends them. The client's
                // native SNI sizes its buffer by the packet size and kills its process
                // (fast-fail in Microsoft.Data.SqlClient.SNI) on a record that holds more.
                while (true)
                {
                    var p = ReadPacket(pipe);
                    if (Trace) Log(id, $"  < type 0x{p[0]:X2} status 0x{p[1]:X2} {p.Length} bytes");
                    ssl.Write(p, 0, p.Length);
                }
            }
            catch (Exception ex) { if (Trace) Log(id, $"server side ended: {ex.GetType().Name}: {ex.Message}"); }
            finally { try { client.Close(); } catch { } }
            up.Wait(2000);
            Log(id, "closed");
        }
        catch (Exception ex)
        {
            Log(id, $"error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { pipe?.Dispose(); } catch { }
            try { client.Close(); } catch { }
        }
    }

    // During the TDS 7.x handshake TLS records travel inside PRELOGIN (0x12) packets;
    // afterwards they go straight onto the socket.
    sealed class HandshakeStream : Stream
    {
        readonly Stream _inner;
        byte[] _pending; int _pos;
        public volatile bool Wrap = true;
        public HandshakeStream(Stream inner) { _inner = inner; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!Wrap && (_pending == null || _pos >= _pending.Length)) return _inner.Read(buffer, offset, count);
            if (_pending == null || _pos >= _pending.Length) { _pending = ReadPacket(_inner); _pos = 8; }
            int n = Math.Min(count, _pending.Length - _pos);
            Buffer.BlockCopy(_pending, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!Wrap) { _inner.Write(buffer, offset, count); return; }
            int len = count + 8;
            var p = new byte[len];
            p[0] = 0x12; p[1] = 0x01; p[2] = (byte)(len >> 8); p[3] = (byte)len;
            Buffer.BlockCopy(buffer, offset, p, 8, count);
            _inner.Write(p, 0, len);
        }

        public override void Flush() { _inner.Flush(); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
