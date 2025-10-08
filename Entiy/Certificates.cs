using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Tool.Sockets.Kernels;
using System.Linq;

namespace WebProxy.Entiy
{
    public class Certificates : IDisposable
    {
        private readonly string error;

        private bool isDelete = false;
        private bool isDispose = false;

        public Certificates(string domain, string sslType, string sslPath, string password)
        {
            this.Domain = domain;
            this.SslType = sslType;
            this.SslPath = sslPath;
            this.Password = password;
            Certificate = LoadCertificate(out error); //new X509Certificate2(SslPath, Password);
        }

        public string Domain { get; init; }

        public string SslType { get; init; }

        public string SslPath { get; init; }

        public string Password { get; init; }

        public X509Certificate2 Certificate { get; }

        public bool IsError => !string.IsNullOrEmpty(error);

        public string Error => error;

        public bool IsDelete => isDelete;

        public bool IsDispose => isDispose;

        public void Delete()
        {
            isDelete = true;
        }

        public void Dispose()
        {
            isDispose = true;
            Certificate?.Dispose();
            GC.SuppressFinalize(this);
        }

        private X509Certificate2 LoadCertificate(out string Error)
        {
            try
            {
                Error = null;
                return SslType switch
                {
                    "Pfx" => X509CertificateLoader.LoadPkcs12FromFile(SslPath, Password),
                    "Pem" => LoadPemCertificate(), //X509Certificate2.CreateFromPemFile(SslPath, Password),
                    _ => throw new NotSupportedException($"不支持该格式:[{SslType}]的证书文件"),
                };
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return null;
            }
        }

        private X509Certificate2 LoadPemCertificate()
        {
            string certContents = File.ReadAllText(SslPath);
            string keyContents = File.ReadAllText(Password);

            ReadOnlySpan<byte> pfxBytes = LoadPemConvert(certContents, keyContents);
            return X509CertificateLoader.LoadPkcs12(pfxBytes, null);
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
            int maxBytes = Encoding.UTF8.GetMaxByteCount(pemBlock.Length);
            using BytesCore bytes = new(maxBytes);
            int actualBytes = Encoding.UTF8.GetBytes(pemBlock, bytes.Span);
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

        private static byte[] LoadPemConvert(string cert, string privkey)
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

                return loadedCertificates.Export(X509ContentType.Pkcs12) ?? throw new Exception("关联证书链失败！");
            }
            finally
            {
                foreach (var certificate in loadedCertificates)
                {
                    certificate?.Dispose();
                }
            }
        }
    }
}
