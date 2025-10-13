using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
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
    }

    public class Certificates : IDisposable
    {
        private static Encoding ASCII => Encoding.ASCII;

        private readonly string error;
        private readonly string certHash;
        //private readonly Lock _sync = new(); 
        private readonly SslStreamCertificateContext certificateContext;
        //private X509Certificate2 TargetCertificate => certificateContext.TargetCertificate;

        private volatile bool isDelete = false;
        private volatile bool isDispose = false;
        private long _refCount = 0;

        public Certificates(string domain, string sslType, string sslPath, string password)
        {
            this.Domain = domain;
            this.SslType = sslType;
            this.SslPath = sslPath;
            this.Password = password;
            certificateContext = LoadCertificate(out certHash, out error); //new X509Certificate2(SslPath, Password);
        }

        public Certificates(CertEntiy certEntiy) : this(certEntiy.Domain, certEntiy.SslType, certEntiy.Arg0, certEntiy.Arg1)
        {
        }

        public string Domain { get; }

        public string SslType { get; }

        public string SslPath { get; }

        public string Password { get; }

        public string CertHash => certHash;

        public bool IsError => !string.IsNullOrEmpty(error);

        public string Error => error;

        //public bool IsDelete => isDelete;

        //public bool IsDispose => isDispose;

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
        }

        private SslStreamCertificateContext LoadCertificate(out string CertHash, out string Error)
        {
            try
            {
                Error = null;
                CertHash = GetFileHash(SslPath);
                return SslType switch
                {
                    "Pfx" => LoadPfxCertificate(),
                    "Pem" => LoadPemCertificate(), //X509Certificate2.CreateFromPemFile(SslPath, Password),
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

        private SslStreamCertificateContext LoadPfxCertificate()
        {
            var x509Certificate2 = X509CertificateLoader.LoadPkcs12FromFile(SslPath, Password, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
            var sslStreamCertificateContext = SslStreamCertificateContext.Create(x509Certificate2, null, true);
            return sslStreamCertificateContext;
        }

        private SslStreamCertificateContext LoadPemCertificate()
        {
            string certContents = File.ReadAllText(SslPath, ASCII);
            string keyContents = File.ReadAllText(Password, ASCII);

            return LoadPemConvert(certContents, keyContents);
            //ReadOnlySpan<byte> pfxBytes = LoadPemConvert(certContents, keyContents);
            //return X509CertificateLoader.LoadPkcs12(pfxBytes, null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
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

        private static SslStreamCertificateContext LoadPemConvert(string cert, string privkey)
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
                    loadedCertificates.Add(index is 0 ? GetCertPem(pemBlock, privkey) : GetCertPem(pemBlock));
                }
                //SslCertificateTrust.CreateForX509Store();
                //var sslStreamCertificateContext = SslStreamCertificateContext.Create(loadedCertificates[0], loadedCertificates, true);
                //return sslStreamCertificateContext;
                var pfxBytes = loadedCertificates.Export(X509ContentType.Pkcs12) ?? throw new Exception("关联证书链失败！");
                var certificate2 = X509CertificateLoader.LoadPkcs12(pfxBytes, null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                var sslStreamCertificateContext = SslStreamCertificateContext.Create(certificate2, null, true);
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
