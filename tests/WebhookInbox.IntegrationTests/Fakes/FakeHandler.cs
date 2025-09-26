public class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_fn(request));
}