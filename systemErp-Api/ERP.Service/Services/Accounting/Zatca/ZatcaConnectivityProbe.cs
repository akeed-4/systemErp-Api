using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using ERP.Core.DTOs.Accounting.Sales;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// فحوص الشبكة منخفضة المستوى نحو بوابة الهيئة: DNS ثم TCP ثم TLS، كل خطوة بمهلتها وزمنها وتفاصيل خطئها.
/// تكشف أين ينقطع الاتصال الخارجي على الاستضافة (حجب منفذ، فشل تحليل اسم، اعتراض TLS). لا ترسل أي بيانات اعتماد.
/// </summary>
internal static class ZatcaConnectivityProbe
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(10);

    public static async Task<ZatcaConnectivityStepDto> DnsAsync(string host, CancellationToken ct)
        => await RunAsync("dns", async token =>
        {
            var addresses = await Dns.GetHostAddressesAsync(host, token);
            if (addresses.Length == 0) throw new SocketException((int)SocketError.HostNotFound);
            return string.Join(", ", addresses.Select(a => a.ToString()));
        }, ct);

    public static async Task<ZatcaConnectivityStepDto> TcpAsync(string host, int port, CancellationToken ct)
        => await RunAsync("tcp", async token =>
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, token);
            return $"Connected to {client.Client.RemoteEndPoint}";
        }, ct);

    public static async Task<ZatcaConnectivityStepDto> TlsAsync(string host, int port, CancellationToken ct)
        => await RunAsync("tls", async token =>
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, token);
            SslPolicyErrors policyErrors = SslPolicyErrors.None;
            await using var ssl = new SslStream(client.GetStream(), false, (_, _, _, errors) => { policyErrors = errors; return true; });
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, token);
            var cert = ssl.RemoteCertificate;
            var detail = $"{ssl.SslProtocol}, {ssl.NegotiatedCipherSuite}; cert: {cert?.Subject} | issuer: {cert?.Issuer} | expires: {cert?.GetExpirationDateString()}";
            // شهادة لا يثق بها الخادم (مثلاً وسيط يفك TLS) تُفشل HttpClient لاحقاً: تُعرض هنا صراحةً
            if (policyErrors != SslPolicyErrors.None) throw new AuthenticationFailure($"Certificate validation failed: {policyErrors}. {detail}");
            return detail;
        }, ct);

    private static async Task<ZatcaConnectivityStepDto> RunAsync(string name, Func<CancellationToken, Task<string>> step, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(StepTimeout);
        var watch = Stopwatch.StartNew();
        try
        {
            var detail = await step(timeout.Token);
            return new ZatcaConnectivityStepDto { Name = name, Success = true, ElapsedMs = watch.ElapsedMilliseconds, Detail = detail };
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new ZatcaConnectivityStepDto
            {
                Name = name,
                Success = false,
                ElapsedMs = watch.ElapsedMilliseconds,
                Detail = ex is OperationCanceledException ? $"No result within {StepTimeout.TotalSeconds:0}s (timeout)." : ex.Message,
                ExceptionType = ex.GetType().FullName,
                ExceptionChain = ZatcaApiClient.ExceptionChain(ex),
            };
        }
    }

    private sealed class AuthenticationFailure(string message) : Exception(message);
}
