using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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
            ReadOnlySpan<byte> certContents = File.ReadAllBytes(SslPath);

            using var certificate2 = X509CertificateLoader.LoadCertificate(certContents);
            if (certificate2.HasPrivateKey)
            {
                return GetPfx(certificate2);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(Password))
                {
                    ReadOnlySpan<char> keyContents = File.ReadAllText(Password);
                    using var hellman = RSA.Create();
                    hellman.ImportFromPem(keyContents);
                    using var x509 = certificate2.CopyWithPrivateKey(hellman);
                    return GetPfx(x509);
                }

            }
            throw new NotSupportedException($"{SslPath} 证书缺少私钥无法用于SSL验证握手！");

            static X509Certificate2 GetPfx(X509Certificate2 certificate2) 
            {
               return X509CertificateLoader.LoadPkcs12(certificate2.Export(X509ContentType.Pkcs12), null);
            }
        }
    }
}
