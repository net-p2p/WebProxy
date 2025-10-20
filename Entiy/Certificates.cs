using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Tool;
using Tool.Sockets.Kernels;

namespace WebProxy.Entiy
{
    public record CertEntiy
    {
        public string Domain { get; init; }
        public string SslType { get; init; }
        public string Arg0 { get; init; }
        public string Arg1 { get; init; }
        public List<SslApplicationProtocol> ApplicationProtocols { get; set; }
        public SslProtocols EnabledSslProtocols { get; init; }
        public List<TlsCipherSuite> TlsCiphers { get; init; }
        public bool AllowRenegotiation { get; init; }
        public bool AllowTlsResume { get; init; }
        public bool ClientCertificateRequired { get; init; }
        public X509RevocationMode CertificateRevocationCheckMode { get; init; }
        public EncryptionPolicy EncryptionPolicy { get; init; }
    }

    public class Certificates : IDisposable
    {
        private static Encoding ASCII => Encoding.ASCII;

        private readonly string error;
        private readonly string certHash;
        private readonly X509KeyStorageFlags flags;
        //private readonly Lock _sync = new();
        private readonly SslStreamCertificateContext certificateContext;

        private CertEntiy certEntiy;
        private volatile bool isDelete = false;
        private volatile bool isDispose = false;
        private long _refCount = 0;

        public Certificates(CertEntiy certEntiy, ILogger logger)
        {
            flags = OperatingSystem.IsWindows() ? X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet : X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet;
            this.certEntiy = certEntiy;
            certificateContext = LoadCertificate(logger, out certHash, out error);
        }

        public string Domain => certEntiy.Domain;

        public string SslType => certEntiy.SslType;

        public string SslPath => certEntiy.Arg0;

        public string Password => certEntiy.Arg1;

        public CertEntiy CertEntiy => certEntiy;

        public string CertHash => certHash;

        public bool IsError => !string.IsNullOrEmpty(error);

        public string Error => error;

        //public bool IsDelete => isDelete;

        //public bool IsDispose => isDispose;

        public void UpCertEntiy(CertEntiy certEntiy, ILogger logger) 
        {
            if (certEntiy is not null)
            {
                this.certEntiy = certEntiy;
                IsTlsCiphers(logger);
            }
        }

        public void Delete()
        {
            isDelete = true;
            //using (_sync.EnterScope())
            {
                if (Volatile.Read(ref _refCount) is 0L)
                {
                    Dispose();
                }
            }
        }

        public SslStreamCertificateContext BorrowCert()
        {
            //using (_sync.EnterScope())
            {
                if (isDispose) return null;
                _refCount.Increment();
                if (isDispose)
                {
                    ReturnCert();
                    return null;
                }
                return certificateContext;
            }
        }

        public void ReturnCert()
        {
            //using (_sync.EnterScope())
            {
                var i = _refCount.Decrement();
                if (i is 0L && isDelete)
                {
                    Dispose();
                }
            }
        }

        public void Dispose()
        {
            isDispose = true;
            if (certificateContext is not null)
            {
                certificateContext.TargetCertificate?.Dispose();
                foreach (var x509 in certificateContext.IntermediateCertificates) x509?.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        public void GetSubjects(ILogger logger)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                var targetCertificate = certificateContext.TargetCertificate;
                var certificates = certificateContext.IntermediateCertificates;

                StringBuilder builder = new();
                builder.Append("域名证书：").AppendLine(targetCertificate.Subject);
                builder.Append("包含证书链数：").AppendLine(certificates.Count.ToString());
                foreach (var element in certificates)
                    builder.AppendLine(element.Subject);

                //using var chain = X509Chain.Create();
                //chain.Build(targetCertificate);
                //builder.AppendLine("关联证书链：");
                //foreach (var element in chain.ChainElements)
                //    builder.AppendLine(element.Certificate.Subject);

                builder.AppendLine("包含关联链路：");
                foreach (var cert2 in targetCertificate.Extensions)
                    builder.AppendLine(cert2.Oid.FriendlyName);

                logger.LogDebug("{Msg}", builder.ToString());
            }
            IsTlsCiphers(logger);
        }

        private void IsTlsCiphers(ILogger logger) 
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsAndroid())
            {
                if (certEntiy.TlsCiphers.Count > 0)
                {
                    logger.LogWarning("Hosts[{Domain}] 当前系统环境，不支持设置 CipherSuites 属性。", certEntiy.Domain);
                }
            }
        }

        private SslStreamCertificateContext LoadCertificate(ILogger logger, out string CertHash, out string Error)
        {
            try
            {
                Error = null;
                CertHash = GetFileHash(SslPath);
                return SslType switch
                {
                    "Pfx" => LoadPfxCertificate(logger),
                    "Pem" => LoadPemCertificate(logger), //X509Certificate2.CreateFromPemFile(SslPath, Password),
                    _ => throw new NotSupportedException($"不支持该格式:[{SslType}]的证书文件"),
                };
            }
            catch (Exception ex)
            {
                CertHash = null;
                Error = ex.Message;
                return null;
            }
        }

        private SslStreamCertificateContext LoadPfxCertificate(ILogger logger)
        {
            var x509Cates = X509CertificateLoader.LoadPkcs12CollectionFromFile(SslPath, Password, flags);
            X509Certificate2 leafCert = null;
            foreach (var x509 in x509Cates)
            {
                logger.LogDebug("解析Pfx:{Subject}", x509.Subject);
                if (x509.HasPrivateKey)
                {
                    leafCert = x509;
                }
            }
            x509Cates.Remove(leafCert);
            var sslCertificateTrust = SslCertificateTrust.CreateForX509Collection(x509Cates);
            var sslStreamCertificateContext = SslStreamCertificateContext.Create(leafCert, null, true, sslCertificateTrust);
            UpSslTrust(sslStreamCertificateContext, x509Cates);
            return sslStreamCertificateContext;
        }

        private SslStreamCertificateContext LoadPemCertificate(ILogger logger)
        {
            string certContents = File.ReadAllText(SslPath, ASCII);
            string keyContents = File.ReadAllText(Password, ASCII);

            return LoadPemConvert(logger, certContents, keyContents, flags);
            //ReadOnlySpan<byte> pfxBytes = LoadPemConvert(certContents, keyContents);
            //return X509CertificateLoader.LoadPkcs12(pfxBytes, null, flags);
        }

        public override string ToString()
        {
            return isDispose ? "已被释放！" : certificateContext.TargetCertificate.Subject;
        }

        /// <summary>
        /// 解析PEM格式的证书链，提取所有证书的PEM字符串位置
        /// </summary>
        private static List<Range> ParsePemCertificates(ReadOnlySpan<char> pemContent)
        {
            var ranges = new List<Range>();
            ReadOnlySpan<char> content = pemContent;
            int tempEnd = 0;

            while (PemEncoding.TryFind(content, out var fields))
            {
                Range location = fields.Location, _temp = new(location.Start.Value + tempEnd, location.End.Value + tempEnd);
                ranges.Add(_temp);
                tempEnd = _temp.End.Value;
                content = content[location.End..];
            }

            return ranges;
        }

        private static X509Certificate2 GetCertPem(ReadOnlySpan<char> pemBlock)
        {
            int maxBytes = ASCII.GetMaxByteCount(pemBlock.Length);
            using BytesCore bytes = new(maxBytes);
            int actualBytes = ASCII.GetBytes(pemBlock, bytes.Span);
            var certificate2 = X509CertificateLoader.LoadCertificate(bytes.Span[..actualBytes]);
            return certificate2;
        }

        private static X509Certificate2 GetCertPem(ReadOnlySpan<char> pemBlock, string privkey)
        {
            using var certificate2 = GetCertPem(pemBlock);
            using var hellman = RSA.Create();
            hellman.ImportFromPem(privkey);
            var x509 = certificate2.CopyWithPrivateKey(hellman);
            return x509;
        }

        private static SslStreamCertificateContext LoadPemConvert(ILogger logger, string cert, string privkey, X509KeyStorageFlags flags)
        {
            ReadOnlySpan<char> pemContent = cert.AsSpan();
            // 解析PEM文件中的所有证书
            var certificatePems = ParsePemCertificates(pemContent);

            if (certificatePems.Count is 0) throw new ArgumentException("未找到有效的证书");

            // 创建证书集合
            var loadedCertificates = new X509Certificate2Collection();
            try
            {
                foreach (var (index, pemRange) in certificatePems.Index())
                {
                    var pemBlock = pemContent[pemRange];
                    var x509 = index is 0 ? GetCertPem(pemBlock, privkey) : GetCertPem(pemBlock);
                    logger.LogDebug("解析Pem:{Subject}", x509.Subject);
                    loadedCertificates.Add(x509);
                }

                var pfxBytes = loadedCertificates.Export(X509ContentType.Pkcs12) ?? throw new Exception("关联证书链失败！");
                var certificate2 = X509CertificateLoader.LoadPkcs12(pfxBytes, null, flags);

                var copyCates = new X509Certificate2Collection(loadedCertificates);
                copyCates.RemoveAt(0);
                var sslCertificateTrust = SslCertificateTrust.CreateForX509Collection(copyCates);
                var sslStreamCertificateContext = SslStreamCertificateContext.Create(certificate2, null, true, sslCertificateTrust);
                UpSslTrust(sslStreamCertificateContext, copyCates);
                return sslStreamCertificateContext;
            }
            finally
            {
                foreach (var certificate in loadedCertificates)
                {
                    certificate?.Dispose();
                }
            }
        }

        private static void UpSslTrust(SslStreamCertificateContext sslStreamCertificateContext, X509Certificate2Collection x509Cates) 
        {
            foreach (var (index, _x509) in sslStreamCertificateContext.IntermediateCertificates.Index())
            {
                var x509 = x509Cates.FirstOrDefault(c => c.Equals(_x509));
                if (x509 is not null)
                {
                    using (x509)
                    {
                        x509Cates.Remove(x509);
                        x509Cates.Add(_x509);
                    }
                }
            }
        }


        private static string GetFileHash(string file)
        {
            if (File.Exists(file))
            {
                using FileStream fileStream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                return GetFileHash(fileStream);
            }
            else
            {
                throw new Exception("证书文件不存在！");
            }
        }

        private static string GetFileHash(FileStream stream)
        {
            using SHA256 mySHA256 = SHA256.Create();
            stream.Position = 0;
            byte[] hashValue = mySHA256.ComputeHash(stream);
            return PrintByteArray(hashValue); //Convert.ToHexString
        }

        private static string PrintByteArray(byte[] array)
        {
            StringBuilder stringBuilder = new();
            for (int i = 0; i < array.Length; i++)
            {
                stringBuilder.Append($"{array[i]:X2}");
            }
            return stringBuilder.ToString();
        }
    }
}
