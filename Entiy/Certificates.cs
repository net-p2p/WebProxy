using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace WebProxy.Entiy
{
    public class Certificates
    {
        public Certificates(string domain, string sslPath, string password)
        {
            this.Domain = domain;
            this.SslPath = sslPath;
            this.Password = password;
            Certificate = X509CertificateLoader.LoadPkcs12FromFile(sslPath, password); //new X509Certificate2(SslPath, Password);
        }

        public string Domain { get; init; }

        public string SslPath { get; init; }

        public string Password { get; init; }

        public X509Certificate2 Certificate { get; }
    }
}
