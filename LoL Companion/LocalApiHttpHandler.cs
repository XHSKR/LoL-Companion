using System.Net.Http;
using System.Security.Authentication;

namespace LoL_Companion
{
    internal static class LocalApiHttpHandler
    {
        public static HttpClientHandler Create()
        {
            return new HttpClientHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                SslProtocols = SslProtocols.Tls12,
                ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                    request.RequestUri.Host == "127.0.0.1"
            };
        }
    }
}
