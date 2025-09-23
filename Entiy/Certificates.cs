using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace WebProxy.Entiy
{
    public class Certificates: IDisposable
    {
        private readonly string error;

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

        public void Dispose()
        {
            Certificate?.Dispose();
            GC.SuppressFinalize(this);
        }

        ~Certificates()
        {
            Dispose();
        }

        private X509Certificate2 LoadCertificate(out string Error)
        {
            try
            {
                Error = null;
                return SslType switch
                {
                    "Pfx" => X509CertificateLoader.LoadPkcs12FromFile(SslPath, Password),
                    "Pem" => X509Certificate2.CreateFromPemFile(SslPath, Password),
                    _ => throw new NotSupportedException($"不支持该格式:[{SslType}]的证书文件"),
                };
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return null;
            }
        }
    }
}
